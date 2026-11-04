using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Bộ công cụ gom: số công cụ cố định, tìm có phân trang, lưu và xoá theo type, đề xuất, tên cũ vẫn gọi được.</summary>
    public class McpGenericTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private sealed class Fixture
        {
            public string ToolsPath, WorkspacesPath, AgentsPath, ProposalsPath, ActivityPath;
            public string WorkspaceId, AgentId, GitActionId, LibPath;
        }

        private static Fixture Create(TempDir dir, int extraProfiles = 0)
        {
            var f = new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = dir.Combine("workspaces.json"),
                AgentsPath = dir.Combine("agents.json"),
                ProposalsPath = dir.Combine("proposals.json"),
                ActivityPath = dir.Combine("activity.jsonl"),
                LibPath = dir.WriteFile("lib.exe", "q"),
                GitActionId = ToolCatalog.Find("git").Actions[0].Id,
            };
            Directory.CreateDirectory(dir.Combine("repo"));

            var tools = ToolStore.Load(f.ToolsPath);
            tools.Add(new ToolEntry { Id = "inside", Name = "Inside", Path = dir.WriteFile("inside.exe", "x"), Description = "công cụ nội bộ", Args = "--a" });
            tools.Add(new ToolEntry { Id = "libtool", Name = "Lib Tool", Path = f.LibPath, Description = "chưa gắn đâu" });
            tools.Save();

            var workspaces = WorkspaceStore.Load(f.WorkspacesPath);
            var ws = workspaces.Add(new Workspace { Name = "Repo A", Path = dir.Combine("repo") });
            ws.BindLibrary(tools.Find("inside"));
            ws.Bindings.Add(new ToolBinding { Kind = "git" });
            for (var i = 0; i < extraProfiles; i++)
                ws.Profiles.Add(new Profile { Id = "p" + i, Label = "profile số " + i, Kind = "git", ActionId = f.GitActionId });
            workspaces.Save();
            f.WorkspaceId = ws.Id;

            var agents = AgentStore.Load(f.AgentsPath);
            f.AgentId = agents.Add(new AgentRecord { Name = "Claude", WorkspaceId = ws.Id }).Id;
            agents.Save();
            return f;
        }

        private static McpServer Server(Fixture f) => new McpServer(
            () => ToolStore.Load(f.ToolsPath), f.WorkspaceId, () => WorkspaceStore.Load(f.WorkspacesPath),
            runsDir: Path.Combine(Path.GetDirectoryName(f.WorkspacesPath), "runs"),
            agent: new AgentContext { AgentId = f.AgentId, AgentsPath = f.AgentsPath, ProposalsPath = f.ProposalsPath, ActivityPath = f.ActivityPath });

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> r) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)r["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> r) => (bool)((Dictionary<string, object>)r["result"])["isError"];

        private static Dictionary<string, object> Data(Dictionary<string, object> r) => Json.Deserialize<Dictionary<string, object>>(Text(r));

        private static string[] ToolNames(McpServer server)
        {
            var response = Json.Deserialize<Dictionary<string, object>>(server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
            return ((ArrayList)((Dictionary<string, object>)response["result"])["tools"]).Cast<Dictionary<string, object>>().Select(t => (string)t["name"]).ToArray();
        }

        [Fact]
        public void ToolsList_TrongWorkspaceCoNguCanh_MuoiHaiCongCuCoDinh()
        {
            using (var dir = new TempDir())
            {
                var names = ToolNames(Server(Create(dir)));
                Assert.Equal(12, names.Length);
                Assert.Equal(new[] { "overview", "search", "get", "save", "delete", "attach", "run", "logs", "propose", "prepare_worktree", "run_status", "run_stop" }, names);
            }
        }

        [Fact]
        public void ToolsList_SoCongCuKhongDoiKhiWorkspaceCoNhieuProfile()
        {
            using (var dir = new TempDir())
                Assert.Equal(12, ToolNames(Server(Create(dir, extraProfiles: 300))).Length);
        }

        [Fact]
        public void ToolsList_AgentToanMay_GiuBoCu()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var global = new McpServer(() => ToolStore.Load(f.ToolsPath), null, () => WorkspaceStore.Load(f.WorkspacesPath),
                    agent: new AgentContext { AgentId = f.AgentId, AgentsPath = f.AgentsPath, ProposalsPath = f.ProposalsPath, ActivityPath = f.ActivityPath });
                Assert.Contains("register_tool", ToolNames(global));
                Assert.DoesNotContain("overview", ToolNames(global));
            }
        }

        [Fact]
        public void Overview_TraSoLuongKhongLietKe()
        {
            using (var dir = new TempDir())
            {
                var data = Data(Call(Server(Create(dir, extraProfiles: 40)), "overview", "{}"));
                var counts = (Dictionary<string, object>)data["counts"];
                Assert.Equal(40, counts["profiles"]);
                Assert.Equal(2, counts["tools"]);
                Assert.Equal("Repo A", data["workspace"]);
                Assert.False(data.ContainsKey("items"));
            }
        }

        [Fact]
        public void Search_PhanTrangVaTimTheoTuKhoa()
        {
            using (var dir = new TempDir())
            {
                var server = Server(Create(dir, extraProfiles: 50));

                var first = Data(Call(server, "search", "{\"type\":\"profile\",\"limit\":20}"));
                Assert.Equal(50, first["total"]);
                Assert.Equal(20, ((ArrayList)first["items"]).Count);
                Assert.Equal(20, first["nextOffset"]);

                var last = Data(Call(server, "search", "{\"type\":\"profile\",\"limit\":20,\"offset\":40}"));
                Assert.Equal(10, ((ArrayList)last["items"]).Count);
                Assert.False(last.ContainsKey("nextOffset"));

                var one = Data(Call(server, "search", "{\"type\":\"profile\",\"query\":\"SO 7\"}"));
                Assert.Equal(1, one["total"]);
            }
        }

        [Fact]
        public void Search_Library_ThayToolChuaGanVaKhongLoDuongDan()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f), "search", "{\"type\":\"library\"}");
                var text = Text(response);
                Assert.Contains("libtool", text);
                Assert.DoesNotContain(f.LibPath.Replace("\\", "\\\\"), text);
                Assert.DoesNotContain("lib.exe", text);
            }
        }

        [Fact]
        public void Search_TypeSai_BaoLoi()
        {
            using (var dir = new TempDir())
                Assert.True(IsError(Call(Server(Create(dir)), "search", "{\"type\":\"linh tinh\"}")));
        }

        [Fact]
        public void Get_ProfileVaToolKhongCoDuongDan()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir, extraProfiles: 1);
                var server = Server(f);
                Assert.Equal("profile số 0", Data(Call(server, "get", "{\"type\":\"profile\",\"id\":\"p0\"}"))["label"]);
                var tool = Text(Call(server, "get", "{\"type\":\"tool\",\"id\":\"inside\"}"));
                Assert.Contains("công cụ nội bộ", tool);
                Assert.DoesNotContain("inside.exe", tool);
                Assert.True(IsError(Call(server, "get", "{\"type\":\"profile\",\"id\":\"khong-co\"}")));
            }
        }

        [Fact]
        public void Save_TaoRoiSuaProfile_Delete_XoaVaGhiNhatKy()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var created = Call(server, "save", "{\"type\":\"profile\",\"label\":\"Pull\",\"kind\":\"git\",\"action\":\"" + f.GitActionId + "\"}");
                Assert.False(IsError(created), Text(created));
                var id = WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).Profiles.Single().Id;

                Assert.False(IsError(Call(server, "save", "{\"type\":\"profile\",\"id\":\"" + id + "\",\"label\":\"Pull gọn\"}")));
                Assert.Equal("Pull gọn", WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).Profiles.Single().Label);

                Assert.False(IsError(Call(server, "delete", "{\"type\":\"profile\",\"id\":\"" + id + "\"}")));
                Assert.Empty(WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).Profiles);

                var log = ActivityLog.Read(f.ActivityPath);
                Assert.Equal(new[] { "delete", "save", "save" }, log.Select(e => e.Action).ToArray());
                Assert.All(log, e => Assert.Equal("applied", e.Result));
            }
        }

        [Fact]
        public void Save_CustomVaStack_TaoDuoc()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);
                Assert.False(IsError(Call(server, "save", "{\"type\":\"custom\",\"label\":\"Seed\",\"commandLine\":\"pwsh seed.ps1\"}")));
                var custom = WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).Customs.Single();
                var stack = Call(server, "save", "{\"type\":\"stack\",\"label\":\"Chạy\",\"steps\":[{\"kind\":\"custom\",\"action\":\"" + custom.Id + "\",\"wait\":\"exit\"}]}");
                Assert.False(IsError(stack), Text(stack));
                Assert.Single(WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).Stacks);
            }
        }

        [Fact]
        public void Save_ToolCoIdTrongWorkspace_ChiGuiDeXuat()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f), "save", "{\"type\":\"tool\",\"id\":\"inside\",\"args\":\"--b\",\"reason\":\"cần\"}");
                Assert.False(IsError(response), Text(response));
                Assert.Equal("--a", ToolStore.Load(f.ToolsPath).Find("inside").Args);
                Assert.Single(ProposalStore.Load(f.ProposalsPath).Pending);
                Assert.Equal("pending", ActivityLog.Read(f.ActivityPath).Single().Result);
            }
        }

        [Fact]
        public void Attach_ToolThuVien_GanVaoWorkspace()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Assert.False(IsError(Call(Server(f), "attach", "{\"id\":\"libtool\"}")));
                Assert.NotNull(WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId).FindLibrary("libtool"));
                Assert.Equal("Lib Tool", ToolStore.Load(f.ToolsPath).Find("libtool").Name);
            }
        }

        [Fact]
        public void Attach_ThieuIdVaKind_BaoLoi()
        {
            using (var dir = new TempDir())
                Assert.True(IsError(Call(Server(Create(dir)), "attach", "{}")));
        }

        [Fact]
        public void Propose_Delete_GuiDeXuatXoaVaToolConNguyen()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f), "propose", "{\"change\":\"delete\",\"id\":\"inside\",\"reason\":\"không dùng nữa\"}");
                Assert.False(IsError(response), Text(response));
                var proposal = ProposalStore.Load(f.ProposalsPath).Pending.Single();
                Assert.Equal(ProposalKinds.DeleteTool, proposal.Kind);
                Assert.NotNull(ToolStore.Load(f.ToolsPath).Find("inside"));
            }
        }

        [Fact]
        public void Propose_ToolChuaGan_TuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Assert.True(IsError(Call(Server(f), "propose", "{\"change\":\"delete\",\"id\":\"libtool\"}")));
                Assert.Empty(ProposalStore.Load(f.ProposalsPath).Proposals);
            }
        }

        [Fact]
        public void Run_TypeSai_BaoLoi_VaCheDoNoneChan()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Assert.True(IsError(Call(Server(f), "run", "{\"type\":\"linh tinh\"}")));

                AgentStore.Update(f.AgentsPath, s => { s.Find(f.AgentId).RunMode = RunModes.None; return true; });
                var blocked = Call(Server(f), "run", "{\"type\":\"profile\",\"id\":\"x\"}");
                Assert.True(IsError(blocked));
                Assert.Contains("không được phép chạy", Text(blocked));
            }
        }

        [Fact]
        public void TenCu_VanGoiDuocDuDaKhongQuangBa()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f), "list_setup", "{}");
                Assert.False(IsError(response), Text(response));
            }
        }

        [Fact]
        public void Logs_IdLa_BaoLoi()
        {
            using (var dir = new TempDir())
                Assert.True(IsError(Call(Server(Create(dir)), "logs", "{\"id\":\"khong-co\"}")));
        }
    }
}
