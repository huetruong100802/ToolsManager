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
    /// <summary>
    /// Nhóm biến qua MCP (D3): agent chỉ liệt kê (không thấy giá trị), gắn vào profile, chạy kèm nhóm;
    /// không tạo/sửa/xoá nhóm; nhóm tắt "Cho agent dùng" thì không thấy, không gắn, không chạy.
    /// </summary>
    public class McpVariableGroupTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private const string PlainValue = "gia-tri-thuong-123";
        private const string SecretValue = "s3cr3t-token-xyz";

        private sealed class Fixture
        {
            public string ToolsPath, WorkspacesPath, AgentsPath, ProposalsPath, ActivityPath;
            public string WorkspaceId, AgentId;
        }

        /// <summary>
        /// Nhóm: g1 "Dev-A" (cho agent; FOO thường, PLAIN thường, TOKEN secret đã có giá trị), g2 "Staging-Hidden" (tắt cho agent),
        /// g3 "Needs" (cho agent; secret JWT chưa có giá trị). Lệnh riêng c1 và profile pc chạy lệnh đó.
        /// </summary>
        private static Fixture Create(TempDir dir)
        {
            var f = new Fixture
            {
                ToolsPath = dir.Combine("tools.json"),
                WorkspacesPath = dir.Combine("workspaces.json"),
                AgentsPath = dir.Combine("agents.json"),
                ProposalsPath = dir.Combine("proposals.json"),
                ActivityPath = dir.Combine("activity.jsonl"),
            };
            Directory.CreateDirectory(dir.Combine("repo"));
            ToolStore.Load(f.ToolsPath).Save();

            var token = new GroupVariable { Name = "TOKEN", Secret = true };
            VariableGroups.SetSecret(token, SecretValue);
            var hidden = new GroupVariable { Name = "HIDDEN_KEY", Secret = true };
            VariableGroups.SetSecret(hidden, "an-so-bi-mat-789");

            var workspaces = WorkspaceStore.Load(f.WorkspacesPath);
            var ws = workspaces.Add(new Workspace { Name = "Repo A", Path = dir.Combine("repo") });
            ws.Customs.Add(new CustomCommand { Id = "c1", Label = "Echo", CommandLine = "echo TBX=%TBX_FOO%" });
            ws.Profiles.Add(new Profile { Id = "pc", Label = "Chạy echo", Kind = ProfileKind.Custom, RefId = "c1" });
            ws.VariableGroups.Add(new VariableGroup
            {
                Id = "g1", Label = "Dev-A", Description = "DB local", AllowAgent = true,
                Variables = new List<GroupVariable>
                {
                    new GroupVariable { Name = "TBX_FOO", Value = "bar" },
                    new GroupVariable { Name = "PLAIN", Value = PlainValue },
                    token,
                },
            });
            ws.VariableGroups.Add(new VariableGroup
            {
                Id = "g2", Label = "Staging-Hidden", Description = "chỉ người dùng", AllowAgent = false,
                Variables = new List<GroupVariable> { new GroupVariable { Name = "TBX_FOO", Value = "staging" }, hidden },
            });
            ws.VariableGroups.Add(new VariableGroup
            {
                Id = "g3", Label = "Needs", Description = "thiếu secret", AllowAgent = true,
                Variables = new List<GroupVariable> { new GroupVariable { Name = "JWT", Secret = true } },
            });
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

        private static Workspace LoadWs(Fixture f) => WorkspaceStore.Load(f.WorkspacesPath).Find(f.WorkspaceId);

        private static string J(object value) => Json.Serialize(value);

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> r) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)r["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> r) => (bool)((Dictionary<string, object>)r["result"])["isError"];

        private static Dictionary<string, object> Data(Dictionary<string, object> r) => Json.Deserialize<Dictionary<string, object>>(Text(r));

        private static List<Dictionary<string, object>> Items(Dictionary<string, object> r) =>
            ((ArrayList)Data(r)["items"]).Cast<Dictionary<string, object>>().ToList();

        /// <summary>Chờ bước đầu của lần chạy khởi chạy và kết thúc, trả về các dòng output của nó.</summary>
        private static string RunOutput(McpServer server, Dictionary<string, object> started)
        {
            var runId = (string)started["id"];
            Dictionary<string, object> status = null;
            SpinWait.SpinUntil(() =>
            {
                status = Data(Call(server, "run_status", J(new { id = runId, tail = 50 })));
                return ((ArrayList)status["startedTaskIds"]).Count > 0 || (string)status["state"] == "failed";
            }, TimeSpan.FromSeconds(15));
            var ids = (ArrayList)status["startedTaskIds"];
            Assert.True(ids.Count > 0, "bước không khởi chạy: " + status["message"]);

            Dictionary<string, object> task = null;
            SpinWait.SpinUntil(() =>
            {
                task = Data(Call(server, "run_status", J(new { id = (string)ids[0], tail = 50 })));
                return (string)task["state"] != "running";
            }, TimeSpan.FromSeconds(20));
            return string.Join("\n", ((ArrayList)task["tail"]).Cast<string>());
        }

        // ---- Chỉ đọc: không lộ giá trị ----

        [Fact]
        public void LietKe_KhongLoGiaTriThuongVaSecret_VaChiThayNhomChoAgent()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var outputs = new List<string>
                {
                    Text(Call(server, "overview", "{}")),
                    Text(Call(server, "search", J(new { type = "variablegroup" }))),
                    Text(Call(server, "search", J(new { type = "variablegroup", query = "PLAIN" }))),
                    Text(Call(server, "search", "{}")),
                    Text(Call(server, "get", J(new { type = "variablegroup", id = "g1" }))),
                    Text(Call(server, "get", J(new { type = "variablegroup", id = "Dev-A" }))),
                    Text(Call(server, "get", J(new { type = "profile", id = "pc" }))),
                };
                var encrypted = LoadWs(f).VariableGroups.Find(g => g.Id == "g1").Variables.Find(v => v.Name == "TOKEN").Enc;
                Assert.False(string.IsNullOrEmpty(encrypted));
                foreach (var text in outputs)
                {
                    Assert.DoesNotContain(PlainValue, text);
                    Assert.DoesNotContain(SecretValue, text);
                    Assert.DoesNotContain(encrypted, text);
                    Assert.DoesNotContain("an-so-bi-mat-789", text);
                }

                var groups = Items(Call(server, "search", J(new { type = "variablegroup" })));
                Assert.Equal(new[] { "g1", "g3" }, groups.Select(g => (string)g["id"]).ToArray());
                Assert.DoesNotContain("Staging-Hidden", outputs[1]);

                var dev = Data(Call(server, "get", J(new { type = "variablegroup", id = "Dev-A" })));
                Assert.Equal("DB local", (string)dev["description"]);
                var variables = ((ArrayList)dev["variables"]).Cast<Dictionary<string, object>>().ToDictionary(v => (string)v["name"]);
                Assert.False((bool)variables["PLAIN"]["secret"]);
                Assert.True((bool)variables["PLAIN"]["hasValue"]);
                Assert.True((bool)variables["TOKEN"]["secret"]);
                Assert.True((bool)variables["TOKEN"]["hasValue"]);
                Assert.All(variables.Values, v => Assert.Equal(new[] { "hasValue", "name", "secret" }, v.Keys.OrderBy(k => k).ToArray()));

                var needs = Data(Call(server, "get", J(new { type = "variablegroup", id = "g3" })));
                var jwt = ((ArrayList)needs["variables"]).Cast<Dictionary<string, object>>().Single();
                Assert.True((bool)jwt["secret"]);
                Assert.False((bool)jwt["hasValue"]);

                // Tìm theo tên biến; tìm không type thì không có nhóm biến.
                Assert.Equal(new[] { "g1" }, Items(Call(server, "search", J(new { type = "variablegroup", query = "PLAIN" }))).Select(g => (string)g["id"]).ToArray());
                Assert.DoesNotContain(Items(Call(server, "search", "{}")), row => (string)row["type"] == "variablegroup");

                // Overview chỉ đếm nhóm agent được dùng (g1, g3), nhóm tắt không tính.
                var counts = (Dictionary<string, object>)Data(Call(server, "overview", "{}"))["counts"];
                Assert.Equal(2, Convert.ToInt32(counts["variableGroups"]));
            }
        }

        // ---- Từ chối tạo/sửa/xoá ----

        [Fact]
        public void SaveVaDelete_TypeVariableGroup_BiTuChoiVaWorkspaceKhongDoi()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);
                var before = File.ReadAllText(f.WorkspacesPath);

                var create = Call(server, "save", J(new { type = "variablegroup", label = "Mới" }));
                var edit = Call(server, "save", J(new { type = "variablegroup", id = "g1", label = "Đổi tên" }));
                var delete = Call(server, "delete", J(new { type = "variablegroup", id = "g1" }));

                foreach (var response in new[] { create, edit, delete })
                {
                    Assert.True(IsError(response));
                    Assert.Contains("tab Biến", Text(response));
                }
                Assert.Equal(before, File.ReadAllText(f.WorkspacesPath));
            }
        }

        // ---- Nhóm tắt "Cho agent dùng" ----

        [Fact]
        public void NhomTatChoAgent_KhongThay_KhongGan_KhongChayKem()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                Assert.True(IsError(Call(server, "get", J(new { type = "variablegroup", id = "g2" }))));
                Assert.True(IsError(Call(server, "get", J(new { type = "variablegroup", id = "Staging-Hidden" }))));

                var attach = Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "Staging-Hidden" }));
                Assert.True(IsError(attach));
                Assert.Contains("Không có nhóm biến", Text(attach));
                Assert.Null(LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);

                var attachById = Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "g2" }));
                Assert.True(IsError(attachById));
                Assert.Null(LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);

                var run = Call(server, "run", J(new { type = "custom", id = "c1", variableGroup = "g2" }));
                Assert.True(IsError(run));
                Assert.Contains("Không có nhóm biến", Text(run));

                var action = Call(server, "run", J(new { type = "action", kind = "git", action = "x", variableGroup = "g2" }));
                Assert.True(IsError(action));
                Assert.Contains("Không có nhóm biến", Text(action));
            }
        }

        [Fact]
        public void ProfileNguoiDungDaGanNhomTat_AgentKhongChayDuoc_ThongBaoKhongNeuTenNhom()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var store = WorkspaceStore.Load(f.WorkspacesPath);
                store.Find(f.WorkspaceId).Profiles.Find(p => p.Id == "pc").VariableGroupId = "g2";
                store.Save();
                var server = Server(f);

                var run = Call(server, "run", J(new { type = "profile", id = "pc" }));

                Assert.True(IsError(run));
                Assert.Contains("không cho agent dùng", Text(run));
                Assert.DoesNotContain("Staging-Hidden", Text(run));
                // Agent không thấy tên nhóm tắt ở profile.
                Assert.False(Data(Call(server, "get", J(new { type = "profile", id = "pc" }))).ContainsKey("variableGroup"));
            }
        }

        // ---- Gắn vào profile ----

        [Fact]
        public void Save_GanTheoTenHoacId_BoGanBangChuoiRong_VaGetTraVeTenNhom()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var byName = Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "dev-a" }));
                Assert.False(IsError(byName), Text(byName));
                Assert.Equal("g1", LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);
                Assert.DoesNotContain("Cảnh báo", Text(byName));
                Assert.Equal("Dev-A", (string)Data(Call(server, "get", J(new { type = "profile", id = "pc" })))["variableGroup"]);

                // Sửa trường khác, không nhắc tới variableGroup: giữ nguyên liên kết.
                Assert.False(IsError(Call(server, "save", J(new { type = "profile", id = "pc", label = "Tên mới" }))));
                Assert.Equal("g1", LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);

                var unbind = Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "" }));
                Assert.False(IsError(unbind), Text(unbind));
                Assert.Null(LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);

                var byId = Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "g1" }));
                Assert.False(IsError(byId), Text(byId));
                Assert.Equal("g1", LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);
            }
        }

        [Fact]
        public void Save_TaoProfileMoi_KemNhom_VaNhomThieuGiaTriVanGanDuocKemCanhBao()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var created = Call(server, "save", J(new { type = "profile", label = "Mới có nhóm", kind = "custom", refId = "c1", variableGroup = "Needs" }));

                Assert.False(IsError(created), Text(created));
                Assert.Contains("Cảnh báo", Text(created));
                Assert.Contains("JWT", Text(created));
                var profile = LoadWs(f).Profiles.Single(p => p.Label == "Mới có nhóm");
                Assert.Equal("g3", profile.VariableGroupId);

                var bad = Call(server, "save", J(new { type = "profile", label = "Nhóm không có", kind = "custom", refId = "c1", variableGroup = "khong-co" }));
                Assert.True(IsError(bad));
                Assert.DoesNotContain(LoadWs(f).Profiles, p => p.Label == "Nhóm không có");
            }
        }

        [Fact]
        public void HoanTac_SuaProfileGanNhom_ProfileVeKhongGanNhom()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                Assert.False(IsError(Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "Dev-A" }))));
                Assert.Equal("g1", LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);

                var entry = ActivityLog.Read(f.ActivityPath).Single(e => e.Undo != null);
                ActivityUndo.Undo(entry, f.WorkspacesPath, f.ToolsPath, f.ActivityPath);

                Assert.Null(LoadWs(f).Profiles.Find(p => p.Id == "pc").VariableGroupId);
            }
        }

        // ---- Chạy kèm nhóm ----

        [Fact]
        public void AgentGanNhomRoiChayProfile_ProcessCoEnv_VaRunStatusKhongLoEnvHayGiaTri()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);
                Assert.False(IsError(Call(server, "save", J(new { type = "profile", id = "pc", variableGroup = "Dev-A" }))));

                var run = Call(server, "run", J(new { type = "profile", id = "pc" }));
                Assert.False(IsError(run), Text(run));
                var output = RunOutput(server, Data(run));

                Assert.Contains("TBX=bar", output);

                var statuses = new[]
                {
                    Text(run),
                    Text(Call(server, "run_status", J(new { id = (string)Data(run)["id"], tail = 50 }))),
                    Text(Call(server, "logs", J(new { id = (string)Data(run)["id"] }))),
                };
                foreach (var text in statuses)
                {
                    Assert.DoesNotContain(SecretValue, text);
                    Assert.DoesNotContain(PlainValue, text);
                    Assert.DoesNotContain("\"env\"", text.ToLowerInvariant());
                    Assert.DoesNotContain("\"secrets\"", text.ToLowerInvariant());
                }
            }
        }

        [Fact]
        public void RunCustomKemNhom_ProcessCoEnv_KhongKemNhomThiKhong()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var without = Call(server, "run", J(new { type = "custom", id = "c1" }));
                Assert.False(IsError(without), Text(without));
                Assert.Contains("TBX=%TBX_FOO%", RunOutput(server, Data(without)));

                var with = Call(server, "run", J(new { type = "custom", id = "c1", variableGroup = "Dev-A" }));
                Assert.False(IsError(with), Text(with));
                Assert.Contains("TBX=bar", RunOutput(server, Data(with)));
            }
        }

        [Fact]
        public void RunCustomKemNhomThieuSecret_BuocBaoHongDungThongBaoD5()
        {
            using (var dir = new TempDir())
            {
                var f = Create(dir);
                var server = Server(f);

                var run = Call(server, "run", J(new { type = "custom", id = "c1", variableGroup = "Needs" }));

                var text = Text(run);
                if (text.StartsWith("{"))
                {
                    // Kết quả là trạng thái lần chạy: chờ bước được đánh giá rồi đọc lại.
                    var runId = (string)Data(run)["id"];
                    SpinWait.SpinUntil(() => (string)Data(Call(server, "run_status", J(new { id = runId })))["state"] != "running", TimeSpan.FromSeconds(10));
                    text += "\n" + Text(Call(server, "run_status", J(new { id = runId })));
                }
                Assert.Contains("Nhóm biến Needs chưa có giá trị cho JWT, nhập ở tab Biến", text);
            }
        }
    }
}
