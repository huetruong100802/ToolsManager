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
    /// <summary>Danh tính agent: sửa tool dùng chung thành đề xuất, chặn khi bị gỡ hoặc tắt quyền chạy, ghi nhật ký.</summary>
    public class McpAgentTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private sealed class Fixture
        {
            public string ToolsPath, WorkspacesPath, AgentsPath, ProposalsPath, ActivityPath;
            public string OutsidePath, NewPath;
            public Workspace Workspace;
            public AgentRecord Agent;
        }

        private static Fixture Create(TempDir dir, string runMode = RunModes.Auto)
        {
            var f = new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = dir.Combine("workspaces.json"),
                AgentsPath = dir.Combine("agents.json"),
                ProposalsPath = dir.Combine("proposals.json"),
                ActivityPath = dir.Combine("activity.jsonl"),
                OutsidePath = dir.WriteFile("outside.exe", "y"),
                NewPath = dir.WriteFile("brand-new.exe", "z"),
            };
            var inside = new ToolEntry { Id = "inside", Name = "Inside", Path = dir.WriteFile("inside.exe", "x"), Args = "--mac-dinh" };
            var tools = ToolStore.Load(f.ToolsPath);
            tools.Add(inside);
            tools.Add(new ToolEntry { Id = "outside", Name = "Outside", Path = f.OutsidePath });
            tools.Save();

            Directory.CreateDirectory(dir.Combine("repo"));
            var workspaces = WorkspaceStore.Load(f.WorkspacesPath);
            f.Workspace = workspaces.Add(new Workspace { Name = "Repo A", Path = dir.Combine("repo") });
            f.Workspace.BindLibrary(inside);
            workspaces.Save();

            var agents = AgentStore.Load(f.AgentsPath);
            f.Agent = agents.Add(new AgentRecord { Name = "Claude Code · Repo A", Client = "claude", WorkspaceId = f.Workspace.Id, RunMode = runMode });
            agents.Save();
            return f;
        }

        private static AgentContext Context(Fixture f, string agentId) => new AgentContext
        {
            AgentId = agentId, AgentsPath = f.AgentsPath, ProposalsPath = f.ProposalsPath, ActivityPath = f.ActivityPath,
        };

        private static McpServer Server(Fixture f, string workspaceId, string agentId) =>
            new McpServer(() => ToolStore.Load(f.ToolsPath), workspaceId, () => WorkspaceStore.Load(f.WorkspacesPath), agent: Context(f, agentId));

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)response["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> response) =>
            (bool)((Dictionary<string, object>)response["result"])["isError"];

        [Fact]
        public void UpdateTool_TrongWorkspaceCoNguCanh_ThanhDeXuatVaToolKhongDoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "update_tool", "{\"id\":\"inside\",\"args\":\"--moi\",\"reason\":\"lỗi peer\"}");

                Assert.False(IsError(response));
                Assert.Contains("đề xuất", Text(response));
                Assert.Equal("--mac-dinh", ToolStore.Load(f.ToolsPath).Find("inside").Args);

                var proposal = ProposalStore.Load(f.ProposalsPath).Pending.Single();
                Assert.Equal("inside", proposal.ToolId);
                Assert.Equal("--moi", proposal.Fields["args"]);
                Assert.Equal("lỗi peer", proposal.Reason);
                Assert.Equal(f.Agent.Id, proposal.AgentId);
                Assert.Equal("Claude Code · Repo A", proposal.AgentName);
            }
        }

        [Fact]
        public void UpdateTool_GuiHaiLanGiongNhau_ChiMotDeXuat()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f, f.Workspace.Id, f.Agent.Id);
                Call(server, "update_tool", "{\"id\":\"inside\",\"args\":\"--moi\"}");
                Call(server, "update_tool", "{\"id\":\"inside\",\"args\":\"--moi\"}");
                Assert.Single(ProposalStore.Load(f.ProposalsPath).Pending);
            }
        }

        [Fact]
        public void UpdateTool_KhongTruongNao_Loi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "update_tool", "{\"id\":\"inside\"}");
                Assert.True(IsError(response));
                Assert.Empty(ProposalStore.Load(f.ProposalsPath).Proposals);
            }
        }

        [Fact]
        public void UpdateTool_ToolNgoaiWorkspace_VanTuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "update_tool", "{\"id\":\"outside\",\"args\":\"--x\"}");
                Assert.True(IsError(response));
                Assert.Empty(ProposalStore.Load(f.ProposalsPath).Proposals);
            }
        }

        [Fact]
        public void UpdateTool_AgentToanMay_SuaTrucTiep()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var global = AgentStore.Load(f.AgentsPath);
                var id = global.Add(new AgentRecord { Name = "Toàn máy" }).Id;
                global.Save();

                var response = Call(Server(f, null, id), "update_tool", "{\"id\":\"outside\",\"args\":\"--truc-tiep\"}");
                Assert.False(IsError(response));
                Assert.Equal("--truc-tiep", ToolStore.Load(f.ToolsPath).Find("outside").Args);
                Assert.Empty(ProposalStore.Load(f.ProposalsPath).Proposals);
            }
        }

        [Fact]
        public void RegisterTool_TrungToolCoSan_ChiGanVaoWorkspaceKhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "register_tool",
                    "{\"name\":\"Ten khac hoan toan\",\"path\":\"" + f.OutsidePath.Replace("\\", "\\\\") + "\",\"args\":\"--ghi-de\"}");

                Assert.False(IsError(response));
                Assert.Contains("không sửa tool dùng chung", Text(response));
                var outside = ToolStore.Load(f.ToolsPath).Find("outside");
                Assert.Equal("Outside", outside.Name);
                Assert.Null(outside.Args);
                Assert.NotNull(WorkspaceStore.Load(f.WorkspacesPath).Find(f.Workspace.Id).FindLibrary("outside"));
            }
        }

        [Fact]
        public void RegisterTool_ToolMoi_ThemVaoThuVienVaGhiNhatKy()
        {
            using (var dir = new TempDir())
            using (var source = new TempDir())
            {
                var f = Create(dir);
                f.NewPath = source.WriteFile("brand-new.exe", "z");
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "register_tool",
                    "{\"name\":\"Brand New\",\"path\":\"" + f.NewPath.Replace("\\", "\\\\") + "\"}");

                Assert.False(IsError(response), Text(response));
                Assert.Contains(ToolStore.Load(f.ToolsPath).Tools, t => t.Name == "Brand New");

                var entry = ActivityLog.Read(f.ActivityPath).Single();
                Assert.Equal("register_tool", entry.Action);
                Assert.Equal("Brand New", entry.Target);
                Assert.Equal("applied", entry.Result);
                Assert.Equal("Claude Code · Repo A", entry.AgentName);
            }
        }

        [Fact]
        public void UpdateTool_GhiNhatKyLaChoDuyet()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Call(Server(f, f.Workspace.Id, f.Agent.Id), "update_tool", "{\"id\":\"inside\",\"args\":\"--moi\"}");
                Assert.Equal("pending", ActivityLog.Read(f.ActivityPath).Single().Result);
            }
        }

        [Fact]
        public void AgentBiGo_MoiLenhDeuBaoLoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var agents = AgentStore.Load(f.AgentsPath);
                agents.Remove(f.Agent.Id);
                agents.Save();

                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "list_tools", "{}");
                Assert.True(IsError(response));
                Assert.Contains("đã bị gỡ", Text(response));
            }
        }

        [Fact]
        public void AgentCapChoWorkspaceKhac_BiTuChoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var response = Call(Server(f, f.Workspace.Id, f.Agent.Id), "list_tools", "{}");
                Assert.False(IsError(response));

                AgentStore.Update(f.AgentsPath, s => { s.Find(f.Agent.Id).WorkspaceId = "workspace-khac"; return true; });
                Assert.True(IsError(Call(Server(f, f.Workspace.Id, f.Agent.Id), "list_tools", "{}")));
            }
        }

        [Fact]
        public void CheDoChayNone_ChanRunTool_NhungVanDocDuoc()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir, RunModes.None);
                var server = Server(f, f.Workspace.Id, f.Agent.Id);

                var run = Call(server, "run_tool", "{\"id\":\"inside\"}");
                Assert.True(IsError(run));
                Assert.Contains("không được phép chạy", Text(run));
                Assert.False(IsError(Call(server, "list_tools", "{}")));
            }
        }

        [Fact]
        public void DoiQuyenTrongGui_CoHieuLucNgayKhongCanKetNoiLai()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f, f.Workspace.Id, f.Agent.Id);
                AgentStore.Update(f.AgentsPath, s => { s.Find(f.Agent.Id).RunMode = RunModes.None; return true; });
                Assert.Contains("không được phép chạy", Text(Call(server, "run_tool", "{\"id\":\"inside\"}")));
            }
        }

        [Fact]
        public void CallCongCu_CapNhatLanCuoiHoatDong()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Assert.True(string.IsNullOrEmpty(AgentStore.Load(f.AgentsPath).Find(f.Agent.Id).LastSeenAt));
                Call(Server(f, f.Workspace.Id, f.Agent.Id), "list_tools", "{}");
                Assert.False(string.IsNullOrEmpty(AgentStore.Load(f.AgentsPath).Find(f.Agent.Id).LastSeenAt));
            }
        }

        [Fact]
        public void KhongCoNguCanh_GiuHanhViCu_SuaTrucTiep()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = new McpServer(() => ToolStore.Load(f.ToolsPath), f.Workspace.Id, () => WorkspaceStore.Load(f.WorkspacesPath));
                Call(server, "update_tool", "{\"id\":\"inside\",\"args\":\"--moi\"}");
                Assert.Equal("--moi", ToolStore.Load(f.ToolsPath).Find("inside").Args);
            }
        }

        [Fact]
        public void ProposalApplier_DuyetDeXuatSua_GhiVaoToolDungChung()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                Call(Server(f, f.Workspace.Id, f.Agent.Id), "update_tool", "{\"id\":\"inside\",\"args\":\"--legacy-peer-deps\",\"tags\":[\"a\",\"b\"]}");
                var proposal = ProposalStore.Load(f.ProposalsPath).Pending.Single();

                using (ToolStore.Lock())
                {
                    var store = ToolStore.Load(f.ToolsPath);
                    ProposalApplier.Apply(store, proposal);
                    store.Save();
                }

                var tool = ToolStore.Load(f.ToolsPath).Find("inside");
                Assert.Equal("--legacy-peer-deps", tool.Args);
                Assert.Equal(new[] { "a", "b" }, tool.Tags.ToArray());
            }
        }

        [Fact]
        public void ProposalApplier_DeXuatXoa_BoToolKhoiThuVien()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var proposal = new Proposal { Kind = ProposalKinds.DeleteTool, ToolId = "outside", ToolName = "Outside" };

                using (ToolStore.Lock())
                {
                    var store = ToolStore.Load(f.ToolsPath);
                    ProposalApplier.Apply(store, proposal);
                    store.Save();
                }
                Assert.Null(ToolStore.Load(f.ToolsPath).Find("outside"));
            }
        }

        [Fact]
        public void ProposalApplier_ToolDaMat_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var store = ToolStore.Load(f.ToolsPath);
                Assert.Throws<InvalidDataException>(() => ProposalApplier.Apply(store, new Proposal { Kind = ProposalKinds.UpdateTool, ToolId = "khong-co" }));
            }
        }
    }
}
