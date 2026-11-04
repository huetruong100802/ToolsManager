using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>Agent dùng cấu hình chạy của workspace qua MCP: liệt kê, chạy, theo dõi, dừng.</summary>
    public class WorkspaceRunTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private sealed class Fixture
        {
            public string ToolsPath;
            public string WorkspacesPath;
            public string RunsDir;
            public string Repo;
            public string WorkspaceId;
        }

        /// <summary>Một workspace có một lệnh riêng in ra chuỗi nhận biết.</summary>
        private static Fixture Create(TempDir dir, Action<Workspace> configure = null)
        {
            var repo = dir.Combine("repo");
            Directory.CreateDirectory(repo);
            var path = dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(path);
            var workspace = store.Add(new Workspace { Name = "Repo A", Path = repo });
            workspace.Customs.Add(new CustomCommand { Id = "chao", Label = "Chào", CommandLine = "echo xin-chao-toolbox", Cwd = "." });
            configure?.Invoke(workspace);
            store.Save();
            return new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = path,
                RunsDir = dir.Combine("runs"),
                Repo = repo,
                WorkspaceId = workspace.Id,
            };
        }

        private static McpServer Scoped(Fixture f) =>
            new McpServer(() => ToolStore.Load(f.ToolsPath), f.WorkspaceId, () => WorkspaceStore.Load(f.WorkspacesPath), f.RunsDir);

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

        [Fact]
        public void ListSetup_LietKeLenhRiengCuaWorkspace()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "list_setup", "{}");

                Assert.False(IsError(response), Text(response));
                Assert.Contains("\"chao\"", Text(response));
                Assert.Contains("xin-chao-toolbox", Text(response));
            }
        }

        [Fact]
        public void ToolsList_CoToolChayChiKhiPhienGanWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var scoped = ToolNames(Scoped(f));
                var global = ToolNames(new McpServer(() => ToolStore.Load(f.ToolsPath)));

                Assert.Contains("run_custom", scoped);
                Assert.Contains("run_status", scoped);
                Assert.DoesNotContain("run_custom", global);
                Assert.DoesNotContain("list_setup", global);
                Assert.Contains("list_tools", global);
            }
        }

        private static List<string> ToolNames(McpServer server)
        {
            var response = Json.Deserialize<Dictionary<string, object>>(server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
            var tools = (ArrayList)((Dictionary<string, object>)response["result"])["tools"];
            return tools.Cast<Dictionary<string, object>>().Select(t => (string)t["name"]).ToList();
        }

        [Fact]
        public void RunCustom_ChayLenhVaDocDuocLog()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "run_custom", "{\"id\":\"chao\"}");
                Assert.False(IsError(response), Text(response));
                var runId = (string)Payload(response)["id"];

                string taskId = null;
                WaitUntil(() =>
                {
                    var ids = (ArrayList)Payload(Call(server, "run_status", "{\"id\":\"" + runId + "\"}"))["startedTaskIds"];
                    if (ids.Count == 0)
                        return false;
                    taskId = (string)ids[0];
                    return true;
                });
                WaitUntil(() => (string)Payload(Call(server, "run_status", "{\"id\":\"" + taskId + "\"}"))["state"] == "done");

                var task = Payload(Call(server, "run_status", "{\"id\":\"" + taskId + "\",\"tail\":10}"));
                var tail = string.Join("\n", ((ArrayList)task["tail"]).Cast<object>().Select(x => x.ToString()));
                Assert.Contains("xin-chao-toolbox", tail);
                Assert.Equal(0, Convert.ToInt32(task["exitCode"]));
                server.Close();
            }
        }

        [Fact]
        public void RunProfile_IdKhongCo_BaoLoiKhongChay()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "run_profile", "{\"id\":\"khong-co\"}");

                Assert.True(IsError(response));
                Assert.Contains("list_setup", Text(response));
            }
        }

        [Fact]
        public void RunCustom_WaitReadyKhongCoCong_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "run_custom", "{\"id\":\"chao\",\"waitReady\":true}");

                Assert.True(IsError(response));
                Assert.Contains("cổng", Text(response));
            }
        }

        [Fact]
        public void RunAction_ToolChuaGanVaoWorkspace_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "run_action", "{\"kind\":\"git\",\"action\":\"status\"}");

                Assert.True(IsError(response));
                Assert.Contains("chưa được gắn", Text(response));
            }
        }

        [Fact]
        public void RunStatus_IdLaCuaPhienKhac_BaoKhongCo()
        {
            using (var dir = new TempDir())
            {
                var server = Scoped(Create(dir));

                var response = Call(server, "run_status", "{\"id\":\"run-khong-ton-tai\"}");

                Assert.True(IsError(response));
            }
        }

        [Fact]
        public void PhienToanMay_KhongChayDuocLenhWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = new McpServer(() => ToolStore.Load(f.ToolsPath), null, null, f.RunsDir);

                var response = Call(server, "run_custom", "{\"id\":\"chao\"}");

                Assert.True(IsError(response));
                Assert.Contains("workspace", Text(response));
            }
        }

        [Fact]
        public void SessionRuns_Dispose_DungMoiTacVuDangChay()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Combine("runs"));
                var info = host.Start(new TaskSpec
                {
                    Label = "Sống lâu",
                    CommandLine = "ping -n 60 127.0.0.1 > nul",
                    Cwd = dir.Combine("."),
                });
                Assert.True(info.IsRunning);

                using (new SessionRuns(host)) { }

                WaitUntil(() => !info.IsRunning, seconds: 15);
                Assert.NotEqual(TaskState.Running, info.State);
            }
        }
    }
}
