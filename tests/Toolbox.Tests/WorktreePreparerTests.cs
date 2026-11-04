using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Chuẩn bị worktree mới: file local đưa vào, diff áp đúng, không ghi đè, không lẫn repo khác.</summary>
    public class WorktreePreparerTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static void Git(string cwd, string args)
        {
            using (var process = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }))
            {
                var error = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, "git " + args + ": " + error.Result);
            }
        }

        /// <summary>Checkout chính: package.json đã sửa (chưa commit) và pnpm-lock.yaml chưa theo dõi. Worktree tạo từ HEAD.</summary>
        private static (string Main, string Worktree, Workspace Workspace) Create(TempDir dir)
        {
            var main = dir.Combine("main");
            Directory.CreateDirectory(main);
            Git(main, "init -q -b main");
            File.WriteAllText(Path.Combine(main, "package.json"), "{\"v\":1}\n");
            Git(main, "add package.json");
            Git(main, "-c user.email=t@t -c user.name=t commit -q -m init");
            File.WriteAllText(Path.Combine(main, "package.json"), "{\"v\":2}\n");
            File.WriteAllText(Path.Combine(main, "pnpm-lock.yaml"), "lock-moi\n");

            var worktree = dir.Combine("wt");
            Git(main, "worktree add -q --detach \"" + worktree + "\" HEAD");

            var workspace = new Workspace { Name = "Repo", Path = main };
            workspace.Worktree.Files.Add("package.json");
            workspace.Worktree.Files.Add("pnpm-lock.yaml");
            return (main, worktree, workspace);
        }

        private static string Status(PrepareReport report, string file) =>
            report.Lines.Single(l => l.File == file).Status;

        [Fact]
        public void Prepare_CopyFileChuaTheoDoiVaApDiffFileDaSua()
        {
            using (var dir = new TempDir())
            {
                var (_, worktree, workspace) = Create(dir);

                var report = WorktreePreparer.Prepare(workspace, worktree);

                Assert.Null(report.Error);
                Assert.True(report.Ok, string.Join("; ", report.Lines.Select(l => l.File + ":" + l.Status + " " + l.Detail)));
                Assert.Equal("applied", Status(report, "package.json"));
                Assert.Equal("copied", Status(report, "pnpm-lock.yaml"));
                Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(worktree, "package.json")).Trim());
                Assert.Equal("lock-moi", File.ReadAllText(Path.Combine(worktree, "pnpm-lock.yaml")).Trim());
            }
        }

        [Fact]
        public void Prepare_ChayLaiKhongDoiGi()
        {
            using (var dir = new TempDir())
            {
                var (_, worktree, workspace) = Create(dir);
                WorktreePreparer.Prepare(workspace, worktree);

                var second = WorktreePreparer.Prepare(workspace, worktree);

                Assert.True(second.Ok);
                Assert.Equal("unchanged", Status(second, "package.json"));
                Assert.Equal("unchanged", Status(second, "pnpm-lock.yaml"));
            }
        }

        [Fact]
        public void Prepare_WorktreeDaCoFileKhac_KhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                var (_, worktree, workspace) = Create(dir);
                File.WriteAllText(Path.Combine(worktree, "pnpm-lock.yaml"), "khac\n");

                var report = WorktreePreparer.Prepare(workspace, worktree);

                Assert.Equal("skipped", Status(report, "pnpm-lock.yaml"));
                Assert.False(report.Ok);
                Assert.Equal("khac", File.ReadAllText(Path.Combine(worktree, "pnpm-lock.yaml")).Trim());
            }
        }

        [Fact]
        public void Prepare_ThuMucKhongPhaiWorktreeCuaRepo_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var (_, _, workspace) = Create(dir);
                var stranger = dir.Combine("ngoai");
                Directory.CreateDirectory(stranger);

                var report = WorktreePreparer.Prepare(workspace, stranger);

                Assert.NotNull(report.Error);
                Assert.Empty(report.Lines);
                Assert.False(File.Exists(Path.Combine(stranger, "pnpm-lock.yaml")));
            }
        }

        [Fact]
        public void Prepare_ChinhWorkspace_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var (main, _, workspace) = Create(dir);

                var report = WorktreePreparer.Prepare(workspace, main);

                Assert.NotNull(report.Error);
            }
        }

        [Fact]
        public void Prepare_DuongDanRaNgoaiWorkspace_BiTuChoi()
        {
            using (var dir = new TempDir())
            {
                var (_, worktree, workspace) = Create(dir);
                workspace.Worktree.Files.Add("../bi-chan.txt");

                var report = WorktreePreparer.Prepare(workspace, worktree);

                Assert.Equal("error", Status(report, "../bi-chan.txt"));
                Assert.False(File.Exists(Path.Combine(dir.Combine("bi-chan.txt"))));
            }
        }

        [Fact]
        public void McpPrepareWorktree_ChayLenhCaiDatVaTheoDoiDuoc()
        {
            using (var dir = new TempDir())
            {
                var (_, worktree, workspace) = Create(dir);
                workspace.Worktree.Command = "echo cai-dat-xong";
                var storePath = dir.Combine("workspaces.json");
                var store = WorkspaceStore.Load(storePath);
                var saved = store.Add(workspace);
                store.Save();
                var server = new McpServer(() => ToolStore.Load(dir.Combine("tools.json")), saved.Id, () => WorkspaceStore.Load(storePath), dir.Combine("runs"));

                var response = Call(server, "prepare_worktree", "{\"path\":" + Json.Serialize(worktree) + "}");

                Assert.False(IsError(response), Text(response));
                var install = (Dictionary<string, object>)Payload(response)["install"];
                var taskId = (string)install["id"];
                WaitUntil(() => (string)Payload(Call(server, "run_status", "{\"id\":\"" + taskId + "\"}"))["state"] == "done");
                var tail = Payload(Call(server, "run_status", "{\"id\":\"" + taskId + "\",\"tail\":10}"));
                Assert.Contains("cai-dat-xong", string.Join("\n", ((ArrayList)tail["tail"]).Cast<object>()));
                server.Close();
            }
        }

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)response["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> response) =>
            (bool)((Dictionary<string, object>)response["result"])["isError"];

        private static Dictionary<string, object> Payload(Dictionary<string, object> response) =>
            Json.Deserialize<Dictionary<string, object>>(Text(response));

        private static void WaitUntil(Func<bool> condition, int seconds = 20)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Hết giờ chờ điều kiện trong test.");
                Thread.Sleep(100);
            }
        }
    }
}
