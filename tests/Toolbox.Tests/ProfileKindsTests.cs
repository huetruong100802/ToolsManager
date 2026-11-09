using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Profile loại library và custom: lưu/đọc, dòng lệnh có args nối sau, kiểm khi lưu, bộ chạy và MCP chạy được.</summary>
    public class ProfileKindsTests : IDisposable
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private readonly TempDir _dir = new TempDir();
        private readonly string _toolsPath;
        private readonly string _workspacesPath;
        private readonly ToolStore _tools;
        private readonly string _cmdPath;

        public ProfileKindsTests()
        {
            _toolsPath = _dir.Combine("tools.json");
            _workspacesPath = _dir.Combine("workspaces.json");
            Directory.CreateDirectory(_dir.Combine("repo"));
            _cmdPath = _dir.WriteFile("hello.cmd", "@echo off\r\necho hello %*\r\n");
            _tools = ToolStore.Load(_toolsPath);
            _tools.Add(new ToolEntry { Id = "hello", Name = "Hello Tool", Path = _cmdPath, Args = "base" });
            _tools.Add(new ToolEntry { Id = "free", Name = "Free Tool", Path = _dir.WriteFile("free.cmd", "@echo free") });
            _tools.Save();
        }

        public void Dispose() => _dir.Dispose();

        private Workspace NewWorkspace(bool bindHello = true)
        {
            var ws = new Workspace { Id = "w1", Name = "Thử", Path = _dir.Combine("repo") };
            if (bindHello)
                ws.BindLibrary(_tools.Find("hello"));
            ws.Customs.Add(new CustomCommand { Id = "c1", Label = "Lệnh một", CommandLine = "echo custom", Cwd = "." });
            return ws;
        }

        private static Profile LibProfile(string args = null) => new Profile
        {
            Id = "pl", Label = "Chạy hello", Kind = "hello",
            Vals = args == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["args"] = args },
        };

        private static Profile CustomProfile(string args = null) => new Profile
        {
            Id = "pc", Label = "Chạy lệnh một", Kind = ProfileKind.Custom, RefId = "c1",
            Vals = args == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["args"] = args },
        };

        // ---- JSON ----

        [Fact]
        public void Json_RoundTrip_GiuRefIdVaArgs()
        {
            var store = WorkspaceStore.Load(_workspacesPath);
            var ws = store.Add(NewWorkspace());
            ws.Profiles.Add(LibProfile("--x 1"));
            ws.Profiles.Add(CustomProfile("-y"));
            store.Save();

            var loaded = WorkspaceStore.Load(_workspacesPath).Workspaces.Single();
            var lib = loaded.Profiles.Single(p => p.Id == "pl");
            Assert.Equal("hello", lib.Kind);   // profile của tool: Kind là toolId, không còn refId
            Assert.Null(lib.RefId);
            Assert.Equal("--x 1", lib.ExtraArgs);
            Assert.Equal("c1", loaded.Profiles.Single(p => p.Id == "pc").RefId);
        }

        [Fact]
        public void Json_ProfileCu_KhongCoRefId_DocDuocVaKhongGhiRefId()
        {
            var store = WorkspaceStore.Load(_workspacesPath);
            var ws = store.Add(NewWorkspace());
            ws.Profiles.Add(new Profile { Id = "g", Label = "Git", Kind = "git", ActionId = "status" });
            store.Save();

            Assert.DoesNotContain("refId", File.ReadAllText(_workspacesPath));
            var loaded = WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles.Single();
            Assert.Equal("git", loaded.Kind);
            Assert.Null(loaded.RefId);
            Assert.Equal("", loaded.ExtraArgs);
        }

        // ---- Dòng lệnh ----

        [Fact]
        public void Resolve_Library_ArgsNoiSauThamSoHieuLuc()
        {
            var ws = NewWorkspace();
            ws.Profiles.Add(LibProfile("extra"));
            var r = StackResolver.Resolve(ws, new StackStep { Kind = "hello", ProfileId = "pl" }, 0, _tools);

            Assert.False(r.IsBroken, r.Problem);
            Assert.Equal("Chạy hello", r.Label);
            Assert.Equal("\"" + _cmdPath + "\" base extra", r.Spec.CommandLine);
            Assert.Equal(Path.GetDirectoryName(_cmdPath), r.Spec.Cwd);
        }

        [Fact]
        public void Resolve_Library_ThamSoRiengCuaBindingThayThamSoThuVien()
        {
            var ws = NewWorkspace();
            ws.Binding("hello").Args = "ws-args";
            ws.Profiles.Add(LibProfile("more"));
            var r = StackResolver.Resolve(ws, new StackStep { Kind = "hello", ProfileId = "pl" }, 0, _tools);
            Assert.EndsWith(" ws-args more", r.Spec.CommandLine);
        }

        [Fact]
        public void Resolve_Library_KhongProfile_DungActionIdLaToolId()
        {
            var r = StackResolver.Resolve(NewWorkspace(), new StackStep { Kind = "hello", ActionId = "" }, 0, _tools);
            Assert.False(r.IsBroken, r.Problem);
            Assert.Equal("Hello Tool", r.Label);
            Assert.EndsWith(" base", r.Spec.CommandLine);
        }

        [Fact]
        public void Resolve_Library_ChuaGanHoacMatKhoiThuVien_LaHongKhongNem()
        {
            var unbound = StackResolver.Resolve(NewWorkspace(bindHello: false), new StackStep { Kind = "hello", ActionId = "" }, 0, _tools);
            Assert.True(unbound.IsBroken);

            var ws = NewWorkspace();
            ws.Bindings.Add(new ToolBinding { ToolId = "da-xoa" });
            var gone = StackResolver.Resolve(ws, new StackStep { Kind = "da-xoa", ActionId = "" }, 0, _tools);
            Assert.True(gone.IsBroken);
            Assert.Contains("da-xoa", gone.Problem);
        }

        [Fact]
        public void Resolve_Custom_ArgsNoiSauCommandLine()
        {
            var ws = NewWorkspace();
            ws.Profiles.Add(CustomProfile("them"));
            var r = StackResolver.Resolve(ws, new StackStep { Kind = ProfileKind.Custom, ProfileId = "pc" }, 0);

            Assert.False(r.IsBroken, r.Problem);
            Assert.Equal("echo custom them", r.Spec.CommandLine);
            Assert.Equal("Chạy lệnh một", r.Label);
        }

        [Fact]
        public void Resolve_BuocCustomKieuCu_KhongProfile_VanLaLenhRieng()
        {
            var r = StackResolver.Resolve(NewWorkspace(), new StackStep { Kind = TaskKey.Custom, ActionId = "c1" }, 0);
            Assert.False(r.IsBroken, r.Problem);
            Assert.Equal("echo custom", r.Spec.CommandLine);
        }

        [Fact]
        public void Resolve_ArgsCoKyTuNguyHiem_LaHong()
        {
            var ws = NewWorkspace();
            ws.Profiles.Add(CustomProfile("a & del x"));
            var r = StackResolver.Resolve(ws, new StackStep { Kind = ProfileKind.Custom, ProfileId = "pc" }, 0);
            Assert.True(r.IsBroken);
        }

        // ---- Cặp tham số (pairs) ----

        [Fact]
        public void Pairs_Json_RoundTripVaDuyetDongLenh()
        {
            var store = WorkspaceStore.Load(_workspacesPath);
            var ws = store.Add(NewWorkspace());
            var profile = LibProfile();
            profile.Pairs = new List<ParamPair> { new ParamPair("--file", "A B.xlsx"), new ParamPair("--file", "C.xlsx"), new ParamPair("--dry-run") };
            ws.Profiles.Add(profile);
            store.Save();

            var loaded = WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles.Single(p => p.Id == "pl");
            Assert.Equal(3, loaded.Pairs.Count);
            var r = StackResolver.Resolve(WorkspaceStore.Load(_workspacesPath).Workspaces.Single(), new StackStep { Kind = "hello", ProfileId = "pl" }, 0, _tools);
            Assert.False(r.IsBroken, r.Problem);
            Assert.EndsWith(" base --file \"A B.xlsx\" --file C.xlsx --dry-run", r.Spec.CommandLine);
        }

        [Fact]
        public void Pairs_HoiLucChay_ChuaCoGiaTriThiHong_CoCauTraLoiThiChay()
        {
            var ws = NewWorkspace();
            var profile = CustomProfile();
            profile.Pairs = new List<ParamPair> { new ParamPair("", "", ask: true), new ParamPair("--x", "1") };
            ws.Profiles.Add(profile);
            var step = new StackStep { Kind = ProfileKind.Custom, ProfileId = "pc" };

            var pending = StackResolver.Resolve(ws, step, 0);
            Assert.True(pending.IsBroken);
            Assert.Contains("Cần nhập giá trị", pending.Problem);

            step.Answers = new[] { "ten-moi" };
            var ok = StackResolver.Resolve(ws, step, 0);
            Assert.False(ok.IsBroken, ok.Problem);
            Assert.Equal("echo custom ten-moi --x 1", ok.Spec.CommandLine);
            // Profile gốc vẫn còn cặp hỏi, không bị ghi đè bởi câu trả lời.
            Assert.Single(profile.PendingAsks);
        }

        [Fact]
        public void Pairs_Editor_TaoSuaXoaVaKiemKeySai()
        {
            var ws = NewWorkspace();
            using (StackResolver.UseLibrary(_tools))
            {
                Assert.NotNull(WorkspaceEditor.CreateProfile(ws, new Profile
                {
                    Label = "Sai", Kind = "hello", Pairs = new List<ParamPair> { new ParamPair("--ok;calc", "x") },
                }));

                var good = new Profile { Label = "Tốt", Kind = "hello", Pairs = new List<ParamPair> { new ParamPair("--a", "1") } };
                Assert.Null(WorkspaceEditor.CreateProfile(ws, good));

                Assert.Null(WorkspaceEditor.UpdateProfile(ws, good.Id, null, null, null, new[] { new ParamPair("--b") }));
                Assert.Equal("--b", ws.Profiles.Single(p => p.Id == good.Id).ExtraArgs);

                // Không truyền cặp: giữ nguyên. Truyền rỗng: xoá.
                Assert.Null(WorkspaceEditor.UpdateProfile(ws, good.Id, "Đổi tên", null, null));
                Assert.Equal("--b", ws.Profiles.Single(p => p.Id == good.Id).ExtraArgs);
                Assert.Null(WorkspaceEditor.UpdateProfile(ws, good.Id, null, null, null, new ParamPair[0]));
                Assert.False(ws.Profiles.Single(p => p.Id == good.Id).HasPairs);
            }
        }

        [Fact]
        public void Pairs_GoiVaNhanBan_GiuCap_VaChiaSeKhongDungChung()
        {
            var src = LibProfile();
            src.Pairs = new List<ParamPair> { new ParamPair("--p", "1") };
            var copy = WorkspacePack.CloneProfile(src);
            Assert.Equal("--p 1", copy.ExtraArgs);
            copy.Pairs[0].Value = "2";
            Assert.Equal("--p 1", src.ExtraArgs);
        }

        [Fact]
        public void Pairs_ActivityUndo_ThayDoiCapLamDoiBam()
        {
            var a = LibProfile();
            a.Pairs = new List<ParamPair> { new ParamPair("--p", "1") };
            var b = LibProfile();
            b.Pairs = new List<ParamPair> { new ParamPair("--p", "2") };
            var ws1 = NewWorkspace(); ws1.Profiles.Add(a);
            var ws2 = NewWorkspace(); ws2.Profiles.Add(b);
            var data = ActivityUndo.Build(ActivityUndo.Capture(ws1, null), ActivityUndo.Capture(ws2, null));
            Assert.NotNull(data);   // chỉ cặp đổi mà vẫn nhận ra thay đổi
            Assert.Equal(ActivityUndo.TypeProfile, data.Type);
        }

        // ---- Kiểm khi lưu ----

        [Fact]
        public void Editor_LuuProfileTool_ChuanHoaVaKiemTool()
        {
            var ws = NewWorkspace();
            using (StackResolver.UseLibrary(_tools))
            {
                // Profile chạy lệnh gốc của tool (hậu tố rỗng): chuẩn hoá bỏ biến thể và refId lạc.
                var ok = new Profile { Label = "A", Kind = "hello", RefId = "lac", ActionId = "", VariantId = "y" };
                Assert.Null(WorkspaceEditor.CreateProfile(ws, ok));
                Assert.Equal("hello", ok.Kind);
                Assert.True(string.IsNullOrEmpty(ok.ActionId));
                Assert.Null(ok.VariantId);
                Assert.Null(ok.RefId);

                // Có trong thư viện nhưng workspace chưa gắn: tool phải được gắn vào workspace trước khi tạo profile.
                Assert.NotNull(WorkspaceEditor.CreateProfile(ws, new Profile { Label = "B", Kind = "free" }));

                Assert.Contains("khong-co", WorkspaceEditor.CreateProfile(ws, new Profile { Label = "D", Kind = "khong-co" }));
            }
        }

        [Fact]
        public void Editor_LuuProfileCustom_RefIdPhaiLaLenhRiengCoTrongWorkspace()
        {
            var ws = NewWorkspace();
            Assert.Null(WorkspaceEditor.CreateProfile(ws, CustomProfile("ok")));
            Assert.NotNull(WorkspaceEditor.CreateProfile(ws, new Profile { Label = "X", Kind = ProfileKind.Custom, RefId = "khong-co" }));
            Assert.NotNull(WorkspaceEditor.CreateProfile(ws, new Profile { Label = "Y", Kind = ProfileKind.Custom }));
            Assert.Contains("& | <", WorkspaceEditor.CreateProfile(ws, new Profile { Label = "Z", Kind = ProfileKind.Custom, RefId = "c1", Vals = new Dictionary<string, string> { ["args"] = "a|b" } }));
        }

        [Fact]
        public void Editor_XoaLenhRiengDangDuocProfileDung_BiTuChoi()
        {
            var ws = NewWorkspace();
            Assert.Null(WorkspaceEditor.CreateProfile(ws, CustomProfile()));
            Assert.Contains("Chạy lệnh một", WorkspaceEditor.DeleteCustom(ws, "c1"));
        }

        [Fact]
        public void Editor_SuaProfileLibrary_GiuRefIdVaDoiArgs()
        {
            var ws = NewWorkspace();
            using (StackResolver.UseLibrary(_tools))
            {
                Assert.Null(WorkspaceEditor.CreateProfile(ws, LibProfile("a")));
                Assert.Null(WorkspaceEditor.UpdateProfile(ws, "pl", "Mới", null, new Dictionary<string, string> { ["args"] = "b" }));
            }
            var p = ws.Profiles.Single();
            Assert.Equal("Mới", p.Label);
            Assert.Equal("hello", p.Kind);
            Assert.Equal("b", p.ExtraArgs);
        }

        [Fact]
        public void Editor_BoChayCoBuocProfileMoi_KiemDuocVaBuocHongBiTuChoi()
        {
            var ws = NewWorkspace();
            ws.Profiles.Add(LibProfile());
            ws.Profiles.Add(CustomProfile());
            using (StackResolver.UseLibrary(_tools))
            {
                var good = new StackDef { Label = "ok", Steps = { new StackStep { Kind = "hello", ProfileId = "pl" }, new StackStep { Kind = ProfileKind.Custom, ProfileId = "pc" } } };
                Assert.Null(WorkspaceEditor.CreateStack(ws, good));

                var bad = new StackDef { Label = "hong", Steps = { new StackStep { Kind = "hello", ProfileId = "khong-co" } } };
                Assert.StartsWith("Bước 1", WorkspaceEditor.CreateStack(ws, bad));
            }
        }

        // ---- StackRunner ----

        [Fact]
        public void StackRunner_ChayProfileLibraryVaCustom()
        {
            var ws = NewWorkspace();
            ws.Profiles.Add(LibProfile("extra"));
            ws.Profiles.Add(CustomProfile("them"));
            var stack = new StackDef { Id = "s1", Label = "Bộ", Steps = { new StackStep { Kind = "hello", ProfileId = "pl" }, new StackStep { Kind = ProfileKind.Custom, ProfileId = "pc", Wait = WaitKind.Exit } } };

            var host = new TaskHost(_dir.Combine("runs"));
            try
            {
                StackRun run;
                using (StackResolver.UseLibrary(_tools))
                    run = new StackRunner(host) { PollInterval = TimeSpan.FromMilliseconds(30) }.Start(ws, stack);
                Assert.True(run.Completion.Wait(TimeSpan.FromSeconds(30)), "bộ chạy không kết thúc");
                Assert.Equal(RunState.Succeeded, run.State);

                var output = string.Join("\n", run.StartedTaskIds.SelectMany(id => host.Find(id).Lines()));
                Assert.Contains("hello base extra", output);
            }
            finally
            {
                host.StopAll();
                SpinWait.SpinUntil(() => host.Running().Count == 0, TimeSpan.FromSeconds(10));
            }
        }

        // ---- MCP ----

        private McpServer Server(string workspaceId, out string agentsPath)
        {
            agentsPath = _dir.Combine("agents.json");
            var agents = AgentStore.Load(agentsPath);
            var agentId = agents.Add(new AgentRecord { Name = "Claude", WorkspaceId = workspaceId }).Id;
            agents.Save();
            return new McpServer(
                () => ToolStore.Load(_toolsPath), workspaceId, () => WorkspaceStore.Load(_workspacesPath), runsDir: _dir.Combine("runs"),
                agent: new AgentContext { AgentId = agentId, AgentsPath = agentsPath, ProposalsPath = _dir.Combine("proposals.json"), ActivityPath = _dir.Combine("activity.jsonl") });
        }

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> r) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)r["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> r) => (bool)((Dictionary<string, object>)r["result"])["isError"];

        private static Dictionary<string, object> Data(Dictionary<string, object> r) => Json.Deserialize<Dictionary<string, object>>(Text(r));

        private McpServer SeededServer()
        {
            var store = WorkspaceStore.Load(_workspacesPath);
            var ws = store.Add(NewWorkspace());
            store.Save();
            return Server(ws.Id, out _);
        }

        [Fact]
        public void Mcp_SaveGetProfileLibrary_TraRefIdVaArgs()
        {
            var server = SeededServer();
            var saved = Call(server, "save", "{\"type\":\"profile\",\"label\":\"Hello\",\"kind\":\"hello\",\"args\":\"--a\"}");
            Assert.False(IsError(saved), Text(saved));

            var id = WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles.Single().Id;
            var view = Data(Call(server, "get", "{\"type\":\"profile\",\"id\":\"" + id + "\"}"));
            Assert.Equal("hello", view["kind"]);
            Assert.Equal("--a", view["args"]);

            var updated = Call(server, "save", "{\"type\":\"profile\",\"id\":\"" + id + "\",\"args\":\"--b\"}");
            Assert.False(IsError(updated), Text(updated));
            Assert.Equal("--b", WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles.Single().ExtraArgs);
        }

        [Fact]
        public void Mcp_SaveProfileSai_BaoLoiRoKhongGhi()
        {
            var server = SeededServer();
            var noTool = Call(server, "save", "{\"type\":\"profile\",\"label\":\"A\",\"kind\":\"khong-co\"}");
            Assert.True(IsError(noTool));
            Assert.Contains("khong-co", Text(noTool));

            Assert.True(IsError(Call(server, "save", "{\"type\":\"profile\",\"label\":\"B\",\"kind\":\"custom\",\"refId\":\"khong-co\"}")));
            Assert.True(IsError(Call(server, "save", "{\"type\":\"profile\",\"label\":\"C\",\"kind\":\"hello\",\"args\":\"x && y\"}")));
            Assert.Empty(WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles);
        }

        [Fact]
        public void Mcp_RunProfileLibraryVaCustom_ChayVaCoOutput()
        {
            var server = SeededServer();
            Assert.False(IsError(Call(server, "save", "{\"type\":\"profile\",\"label\":\"Hello\",\"kind\":\"hello\",\"args\":\"extra\"}")));
            Assert.False(IsError(Call(server, "save", "{\"type\":\"profile\",\"label\":\"Cus\",\"kind\":\"custom\",\"refId\":\"c1\",\"args\":\"them\"}")));
            var profiles = WorkspaceStore.Load(_workspacesPath).Workspaces.Single().Profiles;

            try
            {
                var lib = profiles.Single(p => p.Kind == "hello");
                Assert.Contains("hello base extra", RunAndRead(server, lib.Id));
                var custom = profiles.Single(p => p.Kind == ProfileKind.Custom);
                Assert.Contains("custom them", RunAndRead(server, custom.Id));
            }
            finally
            {
                server.Close();
            }
        }

        private static string RunAndRead(McpServer server, string profileId)
        {
            var started = Call(server, "run", "{\"type\":\"profile\",\"id\":\"" + profileId + "\"}");
            Assert.False(IsError(started), Text(started));
            var runId = (string)Data(started)["id"];

            var deadline = DateTime.UtcNow.AddSeconds(30);
            Dictionary<string, object> status;
            do
            {
                Thread.Sleep(100);
                status = Data(Call(server, "run_status", "{\"id\":\"" + runId + "\"}"));
            } while ((string)status["state"] == "running" && DateTime.UtcNow < deadline);
            Assert.Equal("succeeded", status["state"]);

            var taskId = (string)((ArrayList)status["startedTaskIds"])[0];
            // Output của tác vụ ngắn có thể về chậm hơn trạng thái một nhịp.
            for (var i = 0; i < 50; i++)
            {
                var tail = Text(Call(server, "logs", "{\"id\":\"" + taskId + "\"}"));
                if (tail.Contains("hello") || tail.Contains("custom"))
                    return tail;
                Thread.Sleep(100);
            }
            return Text(Call(server, "logs", "{\"id\":\"" + taskId + "\"}"));
        }
    }
}
