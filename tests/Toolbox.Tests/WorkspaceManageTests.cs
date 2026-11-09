using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Agent tạo, sửa, xoá tool, profile, lệnh riêng, bộ chạy và worktree; kiểm tham chiếu; ghi bền.</summary>
    public class WorkspaceManageTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private sealed class Fixture
        {
            public string ToolsPath;
            public string WorkspacesPath;
            public string Repo;
            public string WorkspaceId;
            public string GitActionId;
        }

        private static Fixture Create(TempDir dir)
        {
            var repo = dir.Combine("repo");
            Directory.CreateDirectory(repo);
            var path = dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(path);
            var workspace = store.Add(new Workspace { Name = "Repo A", Path = repo });
            store.Save();
            return new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = path,
                Repo = repo,
                WorkspaceId = workspace.Id,
                GitActionId = TestData.GitAction,
            };
        }

        private static McpServer Scoped(Fixture f) =>
            new McpServer(() => ToolStore.Load(f.ToolsPath), f.WorkspaceId, () => WorkspaceStore.Load(f.WorkspacesPath), Path.Combine(Path.GetDirectoryName(f.WorkspacesPath), "runs"));

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)response["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> response) =>
            (bool)((Dictionary<string, object>)response["result"])["isError"];

        private static Workspace Saved(Fixture f) => WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId);

        /// <summary>Thêm tool git vào workspace qua MCP, dùng làm nền cho profile và bộ chạy.</summary>
        private static void AddGit(McpServer server, Fixture f)
        {
            var response = Call(server, "add_tool", "{\"kind\":\"git\"}");
            Assert.False(IsError(response), Text(response));
        }

        [Fact]
        public void AddTool_ThenCreateProfile_ProfileDuocLuuVaHienTrongListSetup()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);

                var created = Call(server, "create_profile",
                    "{\"label\":\"Git status\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");

                Assert.False(IsError(created), Text(created));
                var profile = Saved(f).Profiles.Single();
                Assert.Equal("Git status", profile.Label);
                Assert.Equal("git", profile.Kind);
                Assert.Contains("Git status", Text(Call(server, "list_setup", "{}")));
                server.Close();
            }
        }

        [Fact]
        public void Pairs_QuaMcp_TaoSuaVaChayCanAnswers()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);
                var head = "\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"";

                var bad = Call(server, "create_profile", "{\"label\":\"X\"," + head + ",\"pairs\":[{\"key\":\"--x;calc\"}]}");
                Assert.True(IsError(bad));
                Assert.Empty(Saved(f).Profiles);

                var created = Call(server, "create_profile",
                    "{\"label\":\"Có cặp\"," + head + ",\"pairs\":[{\"key\":\"--file\",\"value\":\"A B.txt\"},{\"key\":\"\",\"ask\":true}]}");
                Assert.False(IsError(created), Text(created));
                var profile = Saved(f).Profiles.Single();
                Assert.Equal(2, profile.Pairs.Count);
                Assert.Single(profile.PendingAsks);

                // Còn cặp hỏi lúc chạy mà không có answers: từ chối, nói rõ cần gì.
                var run = Call(server, "run_profile", "{\"id\":\"" + profile.Id + "\"}");
                Assert.True(IsError(run));
                Assert.Contains("answers", Text(run));

                var cleared = Call(server, "update_profile", "{\"id\":\"" + profile.Id + "\",\"pairs\":[]}");
                Assert.False(IsError(cleared), Text(cleared));
                Assert.False(Saved(f).Profiles.Single().HasPairs);
                server.Close();
            }
        }

        [Fact]
        public void CreateProfile_ToolChuaCoTrongWorkspace_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);

                var response = Call(server, "create_profile",
                    "{\"label\":\"X\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");

                Assert.True(IsError(response));
                Assert.Contains("add_tool", Text(response));
                Assert.Empty(Saved(f).Profiles);
            }
        }

        [Fact]
        public void UpdateThenDeleteProfile_ThayDoiDuocLuu()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);
                Call(server, "create_profile", "{\"label\":\"Cũ\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");
                var id = Saved(f).Profiles.Single().Id;

                var updated = Call(server, "update_profile", "{\"id\":\"" + id + "\",\"label\":\"Mới\"}");
                Assert.False(IsError(updated), Text(updated));
                Assert.Equal("Mới", Saved(f).Profiles.Single().Label);

                var deleted = Call(server, "delete_profile", "{\"id\":\"" + id + "\"}");
                Assert.False(IsError(deleted), Text(deleted));
                Assert.Empty(Saved(f).Profiles);
                server.Close();
            }
        }

        [Fact]
        public void RemoveTool_DangCoProfileDung_TuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);
                Call(server, "create_profile", "{\"label\":\"P\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");

                var response = Call(server, "remove_tool", "{\"kind\":\"git\"}");

                Assert.True(IsError(response));
                Assert.Contains("profile", Text(response));
                Assert.Single(Saved(f).Bindings);
                server.Close();
            }
        }

        [Fact]
        public void CreateStack_BuocTroDenProfileVaCustom_DuocLuu()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);
                Call(server, "create_profile", "{\"label\":\"P\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");
                var profileId = Saved(f).Profiles.Single().Id;
                Call(server, "create_custom", "{\"label\":\"Chào\",\"commandLine\":\"echo hi\"}");
                var customId = Saved(f).Customs.Single().Id;

                var steps = "[{\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\",\"profile\":\"" + profileId + "\"},"
                    + "{\"kind\":\"custom\",\"action\":\"" + customId + "\"}]";
                var response = Call(server, "create_stack", "{\"label\":\"Bộ\",\"steps\":" + steps + "}");

                Assert.False(IsError(response), Text(response));
                var stack = Saved(f).Stacks.Single();
                Assert.Equal(2, stack.Steps.Count);
                server.Close();
            }
        }

        [Fact]
        public void CreateStack_BuocHong_TuChoiCaBo()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);

                var response = Call(server, "create_stack",
                    "{\"label\":\"Hỏng\",\"steps\":[{\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\",\"profile\":\"khong-co\"}]}");

                Assert.True(IsError(response));
                Assert.Contains("Bước 1", Text(response));
                Assert.Empty(Saved(f).Stacks);
            }
        }

        [Fact]
        public void DeleteProfile_DangCoBuocBoChay_TuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                AddGit(server, f);
                Call(server, "create_profile", "{\"label\":\"P\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");
                var profileId = Saved(f).Profiles.Single().Id;
                Call(server, "create_stack", "{\"label\":\"Bộ\",\"steps\":[{\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\",\"profile\":\"" + profileId + "\"}]}");

                var response = Call(server, "delete_profile", "{\"id\":\"" + profileId + "\"}");

                Assert.True(IsError(response));
                Assert.Single(Saved(f).Profiles);
                server.Close();
            }
        }

        [Fact]
        public void DeleteCustom_DangCoBuocBoChay_TuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);
                Call(server, "create_custom", "{\"label\":\"Chào\",\"commandLine\":\"echo hi\"}");
                var customId = Saved(f).Customs.Single().Id;
                Call(server, "create_stack", "{\"label\":\"Bộ\",\"steps\":[{\"kind\":\"custom\",\"action\":\"" + customId + "\"}]}");

                var response = Call(server, "delete_custom", "{\"id\":\"" + customId + "\"}");

                Assert.True(IsError(response));
                Assert.Single(Saved(f).Customs);
                server.Close();
            }
        }

        [Fact]
        public void SetWorktree_LuuFileVaLenh()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Scoped(f);

                var response = Call(server, "set_worktree",
                    "{\"files\":[\"pnpm-lock.yaml\",\" package.json \"],\"command\":\"pnpm install --frozen-lockfile\"}");

                Assert.False(IsError(response), Text(response));
                var worktree = Saved(f).Worktree;
                Assert.Equal(new[] { "pnpm-lock.yaml", "package.json" }, worktree.Files.ToArray());
                Assert.Equal("pnpm install --frozen-lockfile", worktree.Command);
                server.Close();
            }
        }

        [Fact]
        public void BindLibraryTool_GanToolThuVienMaKhongDoiTool()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var exe = dir.WriteFile("conn.exe", "x");
                var tools = ToolStore.Load(f.ToolsPath);
                tools.Add(new ToolEntry { Id = "conn", Name = "Dịch connection string", Path = exe });
                tools.Save();
                var server = Scoped(f);

                var response = Call(server, "bind_library_tool", "{\"id\":\"conn\"}");

                Assert.False(IsError(response), Text(response));
                Assert.NotNull(Saved(f).FindLibrary("conn"));
                Assert.Equal("Dịch connection string", ToolStore.Load(f.ToolsPath).Find("conn").Name);
                server.Close();
            }
        }

        [Fact]
        public void ManageGhiQuaKhoa_KhongMatThayDoiCuaTienTrinhKhac()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var gui = WorkspaceStore.Load(f.WorkspacesPath);
                var server = Scoped(f);
                AddGit(server, f);
                Call(server, "create_custom", "{\"label\":\"Chào\",\"commandLine\":\"echo hi\"}");

                // GUI đồng bộ lại từ đĩa (như khi chuyển trang) rồi thấy thay đổi của agent.
                gui.MergeFrom(WorkspaceStore.Load(f.WorkspacesPath));

                var merged = gui.Find(f.WorkspaceId);
                Assert.Single(merged.Bindings);
                Assert.Single(merged.Customs);
                server.Close();
            }
        }

        [Fact]
        public void GlobalMode_KhongSuaDuocCauHinhWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = new McpServer(() => ToolStore.Load(f.ToolsPath), null, null, dir.Combine("runs"));

                var response = Call(server, "create_custom", "{\"label\":\"Chào\",\"commandLine\":\"echo hi\"}");

                Assert.True(IsError(response));
            }
        }
    }
}
