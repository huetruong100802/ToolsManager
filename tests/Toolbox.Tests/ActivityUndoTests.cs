using System;
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
    /// <summary>Hoàn tác từng thay đổi lẻ ở Nhật ký: chỉ khôi phục đúng đối tượng, và từ chối khi đối tượng đã bị sửa sau đó.</summary>
    public class ActivityUndoTests : IDisposable
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private readonly TempDir _dir = new TempDir();
        private readonly string _toolsPath;
        private readonly string _workspacesPath;
        private readonly string _activityPath;
        private string _wsId;

        public ActivityUndoTests()
        {
            _toolsPath = _dir.Combine("tools.json");
            _workspacesPath = _dir.Combine("workspaces.json");
            _activityPath = _dir.Combine("activity.jsonl");
            Directory.CreateDirectory(_dir.Combine("repo"));

            var tools = ToolStore.Load(_toolsPath);
            tools.Add(new ToolEntry { Id = "hello", Name = "Hello Tool", Path = _dir.WriteFile("hello.cmd", "@echo hello"), Args = "base" });
            tools.Save();

            var store = WorkspaceStore.Load(_workspacesPath);
            var ws = store.Add(new Workspace { Name = "Thử", Path = _dir.Combine("repo") });
            ws.BindLibrary(tools.Find("hello"));
            ws.Profiles.Add(new Profile { Id = "p1", Label = "Gốc", Kind = ProfileKind.Library, RefId = "hello" });
            store.Save();
            _wsId = ws.Id;
        }

        public void Dispose() => _dir.Dispose();

        private Workspace LoadWs() => WorkspaceStore.Load(_workspacesPath).Find(_wsId);

        /// <summary>Thực hiện <paramref name="change"/> như một thao tác của agent và trả về dòng nhật ký (đã ghi) kèm dữ liệu hoàn tác.</summary>
        private ActivityEntry AgentDoes(string action, string target, Action<Workspace> change)
        {
            var store = WorkspaceStore.Load(_workspacesPath);
            var tools = ToolStore.Load(_toolsPath);
            var before = ActivityUndo.Capture(store.Find(_wsId), tools);
            change(store.Find(_wsId));
            store.Save();
            var undo = ActivityUndo.Build(before, ActivityUndo.Capture(WorkspaceStore.Load(_workspacesPath).Find(_wsId), ToolStore.Load(_toolsPath)));
            var entry = new ActivityEntry { AgentId = "a", AgentName = "Claude", WorkspaceId = _wsId, Action = action, Target = target, Result = "applied", Undo = undo };
            ActivityLog.Append(_activityPath, entry);
            return entry;
        }

        private string Undo(ActivityEntry entry) =>
            ActivityUndo.Undo(entry, _workspacesPath, _toolsPath, _activityPath);

        private bool Can(ActivityEntry entry, out string reason) =>
            ActivityUndo.CanUndo(entry, LoadWs(), ToolStore.Load(_toolsPath), out reason);

        // ---- Logic thuần ----

        [Fact]
        public void HoanTacTaoProfile_ProfileBienMat()
        {
            var entry = AgentDoes("create_profile", "Mới", ws => ws.Profiles.Add(new Profile { Id = "p2", Label = "Mới", Kind = ProfileKind.Custom, RefId = "c" }));

            Assert.NotNull(entry.Undo);
            Assert.Equal("profile", entry.Undo.Type);
            Assert.Equal("p2", entry.Undo.Id);
            Assert.Null(entry.Undo.Before);
            Assert.True(Can(entry, out var reason), reason);

            Undo(entry);

            var ws = LoadWs();
            Assert.Null(ws.Profiles.Find(p => p.Id == "p2"));
            Assert.NotNull(ws.Profiles.Find(p => p.Id == "p1"));
        }

        [Fact]
        public void HoanTacSuaProfile_KhoiPhucBanTruoc_KhongDungDoiTuongKhac()
        {
            var entry = AgentDoes("update_profile", "p1", ws => ws.Profiles.Find(p => p.Id == "p1").Label = "Agent sửa");
            // Người dùng sửa một profile KHÁC sau đó: không cản hoàn tác.
            var store = WorkspaceStore.Load(_workspacesPath);
            store.Find(_wsId).Customs.Add(new CustomCommand { Id = "u1", Label = "Của tôi", CommandLine = "echo" });
            store.Save();

            Assert.True(Can(entry, out var reason), reason);
            var message = Undo(entry);

            var ws = LoadWs();
            Assert.Equal("Gốc", ws.Profiles.Find(p => p.Id == "p1").Label);
            Assert.NotNull(ws.Customs.Find(c => c.Id == "u1"));
            Assert.Contains("khôi phục", message);
        }

        [Fact]
        public void SuaProfileBoiNguoiDungSauThaoTacAgent_TuChoi_KhongDoiGi()
        {
            var entry = AgentDoes("update_profile", "p1", ws => ws.Profiles.Find(p => p.Id == "p1").Label = "Agent sửa");
            var store = WorkspaceStore.Load(_workspacesPath);
            store.Find(_wsId).Profiles.Find(p => p.Id == "p1").Label = "Người dùng sửa";
            store.Save();
            var fileBefore = File.ReadAllText(_workspacesPath);

            Assert.False(Can(entry, out var reason));
            Assert.Equal(ActivityUndo.Modified, reason);
            var ex = Assert.Throws<InvalidOperationException>(() => Undo(entry));
            Assert.Contains("đã bị sửa sau đó", ex.Message);

            Assert.Equal(fileBefore, File.ReadAllText(_workspacesPath));
            Assert.DoesNotContain(ActivityLog.Read(_activityPath), e => e.Action == "undo");
        }

        [Fact]
        public void HoanTacXoa_TaoLaiBanTruoc_VaTuChoiNeuIdDaDungLai()
        {
            var entry = AgentDoes("delete_profile", "p1", ws => ws.Profiles.RemoveAll(p => p.Id == "p1"));
            Assert.Equal("", entry.Undo.AfterHash);
            Assert.NotNull(entry.Undo.Before);

            // Id đã bị dùng lại: từ chối.
            var store = WorkspaceStore.Load(_workspacesPath);
            store.Find(_wsId).Profiles.Add(new Profile { Id = "p1", Label = "Dùng lại", Kind = ProfileKind.Library, RefId = "hello" });
            store.Save();
            Assert.False(Can(entry, out _));

            store = WorkspaceStore.Load(_workspacesPath);
            store.Find(_wsId).Profiles.RemoveAll(p => p.Id == "p1");
            store.Save();
            Assert.True(Can(entry, out var reason), reason);

            Undo(entry);
            var restored = LoadWs().Profiles.Find(p => p.Id == "p1");
            Assert.NotNull(restored);
            Assert.Equal("Gốc", restored.Label);
            Assert.Equal("hello", restored.RefId);
        }

        [Fact]
        public void HoanTacHaiLan_LanHaiBaoDaHoanTac()
        {
            var entry = AgentDoes("create_custom", "c", ws => ws.Customs.Add(new CustomCommand { Id = "c9", Label = "C", CommandLine = "echo" }));
            Undo(entry);

            var log = ActivityLog.Read(_activityPath);
            Assert.True(ActivityUndo.IsUndone(entry, log));
            var undoLine = log.First(e => e.Action == "undo");
            Assert.Equal(entry.Undo.Token, undoLine.UndoOf);
            var ex = Assert.Throws<InvalidOperationException>(() => Undo(entry));
            Assert.Equal(ActivityUndo.AlreadyUndone, ex.Message);
        }

        [Fact]
        public void BoChayVaLenhRieng_HoanTacDuoc()
        {
            var stack = AgentDoes("create_stack", "S", ws => ws.Stacks.Add(new StackDef { Id = "s1", Label = "S", Steps = { new StackStep { Kind = ProfileKind.Library, ProfileId = "p1" } } }));
            Assert.Equal("stack", stack.Undo.Type);
            var edit = AgentDoes("update_stack", "s1", ws => ws.Stacks.Find(s => s.Id == "s1").Label = "S2");
            Undo(edit);
            Assert.Equal("S", LoadWs().Stacks.Find(s => s.Id == "s1").Label);

            // Bộ chạy mới sửa lại rồi => dòng tạo (băm bản sau cũ) không còn khớp.
            Assert.True(Can(stack, out var reason), reason);
            Undo(stack);
            Assert.Empty(LoadWs().Stacks);
        }

        [Fact]
        public void ToolGanVaToolThuVien_HoanTacDuoc_KhongBiLechBoiSoLanChay()
        {
            // Đăng ký tool mới trong workspace: ToolEntry + chỗ gắn.
            var store = WorkspaceStore.Load(_workspacesPath);
            var tools = ToolStore.Load(_toolsPath);
            var before = ActivityUndo.Capture(store.Find(_wsId), tools);
            var added = tools.Add(new ToolEntry { Id = "zed", Name = "Zed", Path = _dir.WriteFile("zed.cmd", "@echo zed") });
            tools.Save();
            store.Find(_wsId).BindLibrary(added);
            store.Save();
            var undo = ActivityUndo.Build(before, ActivityUndo.Capture(WorkspaceStore.Load(_workspacesPath).Find(_wsId), ToolStore.Load(_toolsPath)));
            var entry = new ActivityEntry { WorkspaceId = _wsId, Action = "register_tool", Target = "Zed", Result = "applied", Undo = undo };
            ActivityLog.Append(_activityPath, entry);

            Assert.Single(undo.Also);
            Assert.Equal(new[] { "binding", "tool" }, new[] { undo.Type }.Concat(undo.Also.Select(a => a.Type)).OrderBy(x => x).ToArray());

            // Chạy tool qua MCP chỉ tăng RunCount/LastUsedAt: không được làm lệch băm.
            var t = ToolStore.Load(_toolsPath);
            t.MarkUsed("zed");
            t.Save();
            Assert.True(Can(entry, out var reason), reason);

            Undo(entry);
            Assert.Null(ToolStore.Load(_toolsPath).Find("zed"));
            Assert.Null(LoadWs().FindLibrary("zed"));
            Assert.NotNull(ToolStore.Load(_toolsPath).Find("hello"));
        }

        [Fact]
        public void SuaToolThuVien_HoanTacKhoiPhucBanTruoc_GiuSoLanChay()
        {
            var tools = ToolStore.Load(_toolsPath);
            var before = ActivityUndo.Capture(LoadWs(), tools);
            var hello = tools.Find("hello").Clone();
            hello.Notes = "agent ghi";
            tools.Update(hello);
            tools.Save();
            var undo = ActivityUndo.Build(before, ActivityUndo.Capture(LoadWs(), ToolStore.Load(_toolsPath)));
            var entry = new ActivityEntry { WorkspaceId = _wsId, Action = "update_tool", Target = "hello", Result = "applied", Undo = undo };
            ActivityLog.Append(_activityPath, entry);

            var used = ToolStore.Load(_toolsPath);
            used.MarkUsed("hello");
            used.Save();

            Undo(entry);
            var after = ToolStore.Load(_toolsPath).Find("hello");
            Assert.Null(after.Notes);
            Assert.Equal(1, after.RunCount);
        }

        [Fact]
        public void DongCuKhongCoUndo_KhongHoanTacDuoc()
        {
            var old = new ActivityEntry { WorkspaceId = _wsId, Action = "create_profile", Target = "x", Result = "applied" };
            Assert.False(Can(old, out var reason));
            Assert.Equal(ActivityUndo.NoData, reason);
            Assert.Throws<InvalidOperationException>(() => Undo(old));
        }

        [Fact]
        public void KhongDoiGi_KhongSinhUndo()
        {
            var entry = AgentDoes("update_profile", "p1", ws => { });
            Assert.Null(entry.Undo);
        }

        // ---- JSON ----

        [Fact]
        public void ActivityEntry_JsonRoundTrip_CoVaKhongCoUndo()
        {
            var entry = new ActivityEntry
            {
                AgentName = "A", Action = "save", Target = "t", Result = "applied",
                Undo = new ActivityUndoData
                {
                    Token = "tk", Type = "profile", Id = "p1", Before = "{\"id\":\"p1\"}", AfterHash = "abc",
                    Also = new List<ActivityUndoData> { new ActivityUndoData { Type = "tool", Id = "z", AfterHash = "" } },
                },
            };
            var plain = new ActivityEntry { AgentName = "B", Action = "run_tool", Target = "x", Result = "applied" };
            ActivityLog.Append(_activityPath, entry);
            ActivityLog.Append(_activityPath, plain);

            var lines = File.ReadAllLines(_activityPath);
            Assert.Contains("\"undo\"", lines[0]);
            Assert.DoesNotContain("\"undo\"", lines[1]);
            Assert.DoesNotContain("undoOf", lines[1]);

            var read = ActivityLog.Read(_activityPath);
            var back = read.Single(e => e.AgentName == "A").Undo;
            Assert.Equal("tk", back.Token);
            Assert.Equal("profile", back.Type);
            Assert.Equal("{\"id\":\"p1\"}", back.Before);
            Assert.Equal("abc", back.AfterHash);
            Assert.Equal("z", back.Also.Single().Id);
            Assert.Null(read.Single(e => e.AgentName == "B").Undo);
        }

        [Fact]
        public void DongNhatKyCu_KhongCoTruongUndo_VanDocDuoc()
        {
            File.WriteAllText(_activityPath,
                "{\"time\":\"2026-01-01T00:00:00Z\",\"agentId\":\"a\",\"agentName\":\"Cũ\",\"workspaceId\":\"w\",\"action\":\"create_profile\",\"target\":\"x\",\"result\":\"applied\"}\n");
            var entry = ActivityLog.Read(_activityPath).Single();
            Assert.Equal("Cũ", entry.AgentName);
            Assert.Null(entry.Undo);
            Assert.Null(entry.UndoOf);
        }

        // ---- Qua McpServer thật ----

        private McpServer Server()
        {
            var agentsPath = _dir.Combine("agents.json");
            var agents = AgentStore.Load(agentsPath);
            var agentId = agents.Add(new AgentRecord { Name = "Claude", WorkspaceId = _wsId }).Id;
            agents.Save();
            return new McpServer(
                () => ToolStore.Load(_toolsPath), _wsId, () => WorkspaceStore.Load(_workspacesPath), runsDir: _dir.Combine("runs"),
                agent: new AgentContext { AgentId = agentId, AgentsPath = agentsPath, ProposalsPath = _dir.Combine("proposals.json"), ActivityPath = _activityPath });
        }

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> r) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)r["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> r) => (bool)((Dictionary<string, object>)r["result"])["isError"];

        [Fact]
        public void Mcp_SaveProfile_GhiUndo_VaHoanTacXoaProfile()
        {
            var server = Server();
            var saved = Call(server, "save", "{\"type\":\"profile\",\"label\":\"Hello\",\"kind\":\"library\",\"refId\":\"hello\",\"args\":\"--a\"}");
            Assert.False(IsError(saved), Text(saved));

            var entry = ActivityLog.Read(_activityPath).Single(e => e.Action == "save");
            Assert.NotNull(entry.Undo);
            Assert.Equal("profile", entry.Undo.Type);
            Assert.Null(entry.Undo.Before);
            Assert.NotEmpty(entry.Undo.AfterHash);
            var id = entry.Undo.Id;
            Assert.NotNull(LoadWs().Profiles.Find(p => p.Id == id));

            Undo(entry);

            Assert.Null(LoadWs().Profiles.Find(p => p.Id == id));
            Assert.NotNull(LoadWs().Profiles.Find(p => p.Id == "p1"));
            Assert.Contains(ActivityLog.Read(_activityPath), e => e.Action == "undo" && e.UndoOf == entry.Undo.Token);
        }

        [Fact]
        public void Mcp_SuaProfile_HoanTacKhoiPhuc_NguoiDungSuaSauThiTuChoi()
        {
            var server = Server();
            var edited = Call(server, "save", "{\"type\":\"profile\",\"id\":\"p1\",\"label\":\"Agent đổi\"}");
            Assert.False(IsError(edited), Text(edited));
            var first = ActivityLog.Read(_activityPath).Single(e => e.Action == "save");
            Assert.NotNull(first.Undo);
            Assert.Contains("Gốc", first.Undo.Before);

            Undo(first);
            Assert.Equal("Gốc", LoadWs().Profiles.Find(p => p.Id == "p1").Label);

            // Lần hai: agent sửa, rồi người dùng sửa tiếp => từ chối.
            Call(server, "save", "{\"type\":\"profile\",\"id\":\"p1\",\"label\":\"Agent đổi lần hai\"}");
            var second = ActivityLog.Read(_activityPath).First(e => e.Action == "save");
            var store = WorkspaceStore.Load(_workspacesPath);
            store.Find(_wsId).Profiles.Find(p => p.Id == "p1").Label = "Người dùng";
            store.Save();

            Assert.False(Can(second, out var reason));
            Assert.Equal(ActivityUndo.Modified, reason);
            Assert.Equal("Người dùng", LoadWs().Profiles.Find(p => p.Id == "p1").Label);
        }

        [Fact]
        public void Mcp_ThaoTacLoi_KhongGhiUndo()
        {
            var server = Server();
            var failed = Call(server, "delete", "{\"type\":\"profile\",\"id\":\"khong-co\"}");
            Assert.True(IsError(failed));
            var entry = ActivityLog.Read(_activityPath).Single();
            Assert.Equal("failed", entry.Result);
            Assert.Null(entry.Undo);
        }
    }
}
