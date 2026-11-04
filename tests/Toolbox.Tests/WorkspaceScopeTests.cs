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
    /// <summary>Agent theo workspace: chỉ thấy và quản lý tool đã gắn, workspace phải đăng ký; gắn/gỡ tool từ thư viện.</summary>
    public class WorkspaceScopeTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private sealed class Fixture
        {
            public string ToolsPath;
            public string WorkspacesPath;
            public string Repo;
            public string InsidePath;
            public string OutsidePath;
            public Workspace Workspace;
        }

        /// <summary>Thư viện có hai tool: "inside" (đã gắn vào workspace) và "outside" (chưa gắn).</summary>
        private static Fixture Create(TempDir dir)
        {
            var fixture = new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = dir.Combine("workspaces.json"),
                Repo = dir.Combine("repo"),
                InsidePath = dir.WriteFile("inside.exe", "x"),
                OutsidePath = dir.WriteFile("outside.exe", "y"),
            };
            Directory.CreateDirectory(fixture.Repo);

            var tools = ToolStore.Load(fixture.ToolsPath);
            var inside = new ToolEntry { Id = "inside", Name = "Inside", Path = fixture.InsidePath, Args = "--mac-dinh" };
            var outside = new ToolEntry { Id = "outside", Name = "Outside", Path = fixture.OutsidePath };
            tools.Add(inside);
            tools.Add(outside);
            tools.Save();

            var workspaces = WorkspaceStore.Load(fixture.WorkspacesPath);
            fixture.Workspace = workspaces.Add(new Workspace { Name = "Repo A", Path = fixture.Repo });
            fixture.Workspace.BindLibrary(inside);
            workspaces.Save();
            return fixture;
        }

        private static McpServer Scoped(Fixture f, string workspaceId) =>
            new McpServer(() => ToolStore.Load(f.ToolsPath), workspaceId, () => WorkspaceStore.Load(f.WorkspacesPath));

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)response["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> response) =>
            (bool)((Dictionary<string, object>)response["result"])["isError"];

        [Fact]
        public void ListTools_ChiTraToolDaGanVaoWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var text = Text(Call(Scoped(f, f.Workspace.Id), "list_tools", "{}"));

                Assert.Contains("\"inside\"", text);
                Assert.DoesNotContain("\"outside\"", text);
            }
        }

        [Fact]
        public void Update_ToolNgoaiWorkspace_BiTuChoiVaKhongDoiGi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var response = Call(Scoped(f, f.Workspace.Id), "update_tool", "{\"id\":\"outside\",\"name\":\"Đổi tên\"}");

                Assert.True(IsError(response));
                Assert.Equal("Outside", ToolStore.Load(f.ToolsPath).Find("outside").Name);
            }
        }

        [Fact]
        public void Unregister_TrongWorkspace_ChiGoKhoiWorkspaceKhongXoaKhoiThuVien()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var response = Call(Scoped(f, f.Workspace.Id), "unregister_tool", "{\"id\":\"inside\"}");

                Assert.False(IsError(response));
                Assert.NotNull(ToolStore.Load(f.ToolsPath).Find("inside"));
                var saved = WorkspaceStore.Load(f.WorkspacesPath).Find(f.Workspace.Id);
                Assert.Null(saved.FindLibrary("inside"));
            }
        }

        [Fact]
        public void Register_TrongWorkspace_TaoToolVaGanVaoWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var source = dir.WriteFile(@"src\connstr.exe", "z");

                var response = Call(Scoped(f, f.Workspace.Id), "register_tool",
                    "{\"name\":\"Dịch connection string\",\"path\":" + Json.Serialize(source) + "}");

                Assert.False(IsError(response), Text(response));
                var created = ToolStore.Load(f.ToolsPath).Tools.Single(t => t.Name == "Dịch connection string");
                var saved = WorkspaceStore.Load(f.WorkspacesPath).Find(f.Workspace.Id);
                Assert.NotNull(saved.FindLibrary(created.Id));
            }
        }

        [Fact]
        public void Register_TrungToolNgoaiWorkspace_TuChoiVaKhongSua()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var response = Call(Scoped(f, f.Workspace.Id), "register_tool",
                    "{\"name\":\"Đổi tên\",\"path\":" + Json.Serialize(f.OutsidePath) + "}");

                Assert.True(IsError(response));
                Assert.Equal("Outside", ToolStore.Load(f.ToolsPath).Find("outside").Name);
                Assert.Null(WorkspaceStore.Load(f.WorkspacesPath).Find(f.Workspace.Id).FindLibrary("outside"));
            }
        }

        [Fact]
        public void RunTool_NgoaiWorkspace_BiTuChoiTruocKhiChay()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var response = Call(Scoped(f, f.Workspace.Id), "run_tool", "{\"id\":\"outside\"}");

                Assert.True(IsError(response));
                Assert.Contains("không thuộc workspace", Text(response));
            }
        }

        [Fact]
        public void WorkspaceChuaDangKy_MoiLenhDeuBaoLoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);

                var response = Call(Scoped(f, "khong-co"), "list_tools", "{}");

                Assert.True(IsError(response));
                Assert.Contains("chưa được đăng ký trong Toolbox", Text(response));
            }
        }

        [Fact]
        public void ToanMay_UnregisterGoToolKhoiMoiWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = new McpServer(() => ToolStore.Load(f.ToolsPath), null, () => WorkspaceStore.Load(f.WorkspacesPath));

                var response = Call(server, "unregister_tool", "{\"id\":\"inside\"}");

                Assert.False(IsError(response));
                Assert.Null(ToolStore.Load(f.ToolsPath).Find("inside"));
                Assert.Null(WorkspaceStore.Load(f.WorkspacesPath).Find(f.Workspace.Id).FindLibrary("inside"));
            }
        }

        [Fact]
        public void LibraryBinding_ArgsRongThiDungThamSoThuVien()
        {
            var tool = new ToolEntry { Id = "t", Args = "--mac-dinh" };
            var binding = new LibraryBinding { ToolId = "t" };

            Assert.Equal("--mac-dinh", binding.EffectiveArgs(tool));

            binding.Args = "--rieng";
            Assert.Equal("--rieng", binding.EffectiveArgs(tool));
        }

        [Fact]
        public void WorkspaceStore_UnbindToolEverywhere_GoTrenMoiWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var store = WorkspaceStore.Load(f.WorkspacesPath);
                store.Add(new Workspace { Name = "Repo B", Path = dir.Combine("repo-b") }).BindLibrary(new ToolEntry { Id = "inside", Name = "Inside" });

                Assert.Equal(2, store.UnbindToolEverywhere("inside"));
                Assert.All(store.Workspaces, w => Assert.Null(w.FindLibrary("inside")));
            }
        }

        [Fact]
        public void ReadConfig_ConnectionCoCoGioiHanWorkspace_NhanRaDungId()
        {
            using (var dir = new TempDir())
            {
                var repo = dir.Combine("repo");
                Directory.CreateDirectory(repo);
                var slashRepo = repo.Replace('\\', '/');
                var config = dir.WriteFile(".claude.json", $@"{{
  ""projects"": {{
    ""{slashRepo}"": {{ ""mcpServers"": {{ ""toolbox"": {{ ""command"": ""C:\\a\\Toolbox.exe"", ""args"": [""--mcp"", ""--workspace"", ""ab12cd34""] }} }} }}
  }}
}}");

                var result = ClaudeCode.ReadConfig(config);

                var entry = Assert.Single(result.Workspaces).Entry("toolbox");
                Assert.True(entry.HasWorkspaceScope);
                Assert.True(entry.ScopedTo("AB12CD34"));
                Assert.False(entry.ScopedTo("khac"));
            }
        }

        [Fact]
        public void MergeFrom_NhanWorkspaceMoiVaToolGanCuaTienTrinhKhac()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var gui = WorkspaceStore.Load(f.WorkspacesPath);

                // Tiến trình khác (agent) đăng ký workspace mới và gỡ tool khỏi workspace cũ.
                var agent = WorkspaceStore.Load(f.WorkspacesPath);
                var added = agent.Add(new Workspace { Name = "Mới", Path = dir.Combine("moi") });
                agent.Find(f.Workspace.Id).UnbindLibrary("inside");
                agent.Save();

                gui.MergeFrom(agent);

                Assert.NotNull(gui.Find(added.Id));
                Assert.Null(gui.Find(f.Workspace.Id).FindLibrary("inside"));
            }
        }

        [Fact]
        public void DisplayMemberConverter_LayGiaTriTheoDisplayMemberPath()
        {
            var converter = new DisplayMemberConverter();
            var item = new { Name = "Dev" };

            Assert.Equal("Dev", converter.Convert(new object[] { item, "Name" }, typeof(string), null, null));
            Assert.Null(converter.Convert(new object[] { null, "Name" }, typeof(string), null, null));
            Assert.Same(item, converter.Convert(new object[] { item, null }, typeof(object), null, null));
        }
    }
}
