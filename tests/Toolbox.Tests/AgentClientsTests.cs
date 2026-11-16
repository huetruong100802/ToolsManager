using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.AgentClients;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Kết nối Toolbox vào nhiều app agent: ghi đúng file và định dạng của từng app, giữ nguyên phần còn lại của file.</summary>
    public class AgentClientsTests
    {
        private static JsonFileClient Cursor(string userPath) => new JsonFileClient(
            "cursor", "Cursor", "", "Cu", () => userPath, @".cursor\mcp.json", "mcpServers", stdioType: false, detect: () => true, restartHint: "");

        private static JsonFileClient VsCode(string userPath) => new JsonFileClient(
            "vscode", "VS Code", "", "VS", () => userPath, @".vscode\mcp.json", "servers", stdioType: true, detect: () => true, restartHint: "");

        /// <summary>App chỉ có cấu hình toàn máy (như Claude Desktop, Windsurf).</summary>
        private static JsonFileClient GlobalOnly(string userPath) => new JsonFileClient(
            "windsurf", "Windsurf", "", "Ws", () => userPath, null, "mcpServers", stdioType: false, detect: () => true, restartHint: "");

        private static Dictionary<string, object> ReadJson(string path) =>
            (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));

        private static Dictionary<string, object> Server(string path, string key, string name) =>
            (Dictionary<string, object>)((Dictionary<string, object>)ReadJson(path)[key])[name];

        [Fact]
        public void JsonClient_KetNoiToanMay_GiuMucKhacVaServerKhac()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"app\Toolbox.exe", "");
                var config = dir.WriteFile(@"user\.cursor\mcp.json",
                    "{ \"theme\": \"dark\", \"mcpServers\": { \"github\": { \"command\": \"npx\", \"args\": [\"-y\", \"gh-mcp\"], \"env\": { \"T\": \"1\" } } } }");
                var client = Cursor(config);

                AgentConnections.Connect(client, ConnectTarget.Global, exe, "a1");

                var root = ReadJson(config);
                Assert.Equal("dark", root["theme"]);
                Assert.Equal("npx", Server(config, "mcpServers", "github")["command"]);
                var toolbox = Server(config, "mcpServers", "toolbox");
                Assert.Equal(exe, toolbox["command"]);
                Assert.Equal(new object[] { "--mcp", "--agent", "a1" }, (object[])toolbox["args"]);
                Assert.True(File.Exists(config + ".toolbox.bak"));
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, ConnectTarget.Global, exe).Link);
                Assert.Equal(AgentLink.Stale, AgentConnections.State(client, ConnectTarget.Global, dir.WriteFile(@"khac\Toolbox.exe", "")).Link);

                AgentConnections.Disconnect(client, ConnectTarget.Global);

                Assert.False(((Dictionary<string, object>)ReadJson(config)["mcpServers"]).ContainsKey("toolbox"));
                Assert.Equal("npx", Server(config, "mcpServers", "github")["command"]);
            }
        }

        [Fact]
        public void JsonClient_KetNoiLai_GiuTruongNguoiDungTuThem()
        {
            using (var dir = new TempDir())
            {
                var config = dir.WriteFile("mcp.json",
                    "{ \"mcpServers\": { \"toolbox\": { \"command\": \"C:\\\\cu\\\\Toolbox.exe\", \"args\": [\"--mcp\"], \"disabled\": false } } }");

                AgentConnections.Connect(Cursor(config), ConnectTarget.Global, @"C:\moi\Toolbox.exe", null);

                var toolbox = Server(config, "mcpServers", "toolbox");
                Assert.Equal(@"C:\moi\Toolbox.exe", toolbox["command"]);
                Assert.Equal(false, toolbox["disabled"]);
            }
        }

        [Fact]
        public void VsCode_DungKhoaServersVaTypeStdio()
        {
            using (var dir = new TempDir())
            {
                var config = dir.Combine(@"Code\User\mcp.json");

                AgentConnections.Connect(VsCode(config), ConnectTarget.Global, @"C:\T\Toolbox.exe", null);

                var toolbox = Server(config, "servers", "toolbox");
                Assert.Equal("stdio", toolbox["type"]);
                Assert.Equal(@"C:\T\Toolbox.exe", toolbox["command"]);
            }
        }

        [Fact]
        public void Jsonc_DocDuocNhungKhongGhiDeChuThich()
        {
            using (var dir = new TempDir())
            {
                var text = "{\n  // server của tôi\n  \"servers\": { \"toolbox\": { \"command\": \"C:\\\\a // b.exe\", \"args\": [\"--mcp\",], }, },\n}";
                var config = dir.WriteFile("mcp.json", text);
                var client = VsCode(config);

                var entry = client.ReadServers(null)["toolbox"];
                Assert.Equal(@"C:\a // b.exe", entry.Command); // "//" trong chuỗi không phải chú thích

                Assert.Throws<InvalidDataException>(() => AgentConnections.Connect(client, ConnectTarget.Global, @"C:\T\Toolbox.exe", null));
                Assert.Equal(text, File.ReadAllText(config));
            }
        }

        [Fact]
        public void FileHong_TrangThaiCoLoiThayViNem()
        {
            using (var dir = new TempDir())
            {
                var state = AgentConnections.State(Cursor(dir.WriteFile("mcp.json", "{ hỏng")), ConnectTarget.Global);

                Assert.NotNull(state.Error);
            }
        }

        [Fact]
        public void KetNoiWorkspace_GhiFileDuAnVaBoQuaTrongGit()
        {
            using (var dir = new TempDir())
            {
                var repo = dir.Combine("repo");
                Directory.CreateDirectory(Path.Combine(repo, ".git"));
                var client = Cursor(dir.Combine("user-mcp.json"));
                var target = new ConnectTarget { Path = repo, WorkspaceId = "ws1", Name = "Repo" };

                var note = AgentConnections.Connect(client, target, @"C:\T\Toolbox.exe", "a1");

                var file = Path.Combine(repo, ".cursor", "mcp.json");
                Assert.Equal(new object[] { "--mcp", "--workspace", "ws1", "--agent", "a1" }, (object[])Server(file, "mcpServers", "toolbox")["args"]);
                Assert.False(File.Exists(dir.Combine("user-mcp.json")));
                Assert.Contains("/.cursor/mcp.json", File.ReadAllLines(Path.Combine(repo, ".git", "info", "exclude")));
                Assert.NotNull(note);
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, target).Link);
                // Khai báo của workspace khác trong file này thì là kết nối cũ.
                Assert.Equal(AgentLink.Stale, AgentConnections.State(client, new ConnectTarget { Path = repo, WorkspaceId = "ws2", Name = "Repo" }).Link);

                // Kết nối lại không ghi thêm dòng exclude.
                AgentConnections.Connect(client, target, @"C:\T\Toolbox.exe", "a1");
                Assert.Single(File.ReadAllLines(Path.Combine(repo, ".git", "info", "exclude")), l => l == "/.cursor/mcp.json");
            }
        }

        [Fact]
        public void AppChiCoCauHinhToanMay_MoiWorkspaceMotServerRieng()
        {
            using (var dir = new TempDir())
            {
                var config = dir.Combine("mcp_config.json");
                var client = GlobalOnly(config);
                var a = new ConnectTarget { Path = dir.Combine("a"), WorkspaceId = "id-a", Name = "Dự án API" };
                var b = new ConnectTarget { Path = dir.Combine("b"), WorkspaceId = "id-b", Name = "Dự án API" };

                AgentConnections.Connect(client, ConnectTarget.Global, @"C:\T\Toolbox.exe", "g");
                AgentConnections.Connect(client, a, @"C:\T\Toolbox.exe", "x");
                AgentConnections.Connect(client, b, @"C:\T\Toolbox.exe", "y");

                var servers = (Dictionary<string, object>)ReadJson(config)["mcpServers"];
                Assert.Equal(new[] { "toolbox", "toolbox-du-an-api", "toolbox-du-an-api-id-b" }, servers.Keys.ToArray());
                Assert.Equal("toolbox-du-an-api-id-b", AgentConnections.State(client, b).Server);
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, a).Link);
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, ConnectTarget.Global).Link);

                AgentConnections.Disconnect(client, a);

                servers = (Dictionary<string, object>)ReadJson(config)["mcpServers"];
                Assert.Equal(new[] { "toolbox", "toolbox-du-an-api-id-b" }, servers.Keys.ToArray());
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, b).Link);
            }
        }

        [Fact]
        public void ToanMay_KhaiBaoCoWorkspace_LaKetNoiCu()
        {
            using (var dir = new TempDir())
            {
                var config = dir.WriteFile("mcp.json", "{ \"mcpServers\": { \"toolbox\": { \"command\": \"x\", \"args\": [\"--mcp\", \"--workspace\", \"w\"] } } }");

                var state = AgentConnections.State(Cursor(config), ConnectTarget.Global);

                Assert.Equal(AgentLink.Stale, state.Link);
                Assert.Equal("Kết nối cũ, cần kết nối lại", state.Reason);
            }
        }

        [Fact]
        public void Codex_ThayDungBangCuaToolboxGiuPhanConLai()
        {
            using (var dir = new TempDir())
            {
                var config = dir.WriteFile("config.toml",
                    "model = \"gpt-5\"\n\n[mcp_servers.toolbox]\ncommand = 'C:\\cu\\Toolbox.exe'\nargs = [\n  \"--mcp\",\n]\n\n[mcp_servers.toolbox.env]\nA = \"1\"\n\n"
                    + "[mcp_servers.github]\ncommand = \"npx\"\nargs = [\"-y\", \"gh\"]\n\n[profiles.fast]\nmodel = \"o4\"\n");
                var client = new CodexClient(() => config);

                Assert.Equal(@"C:\cu\Toolbox.exe", client.ReadServers(null)["toolbox"].Command);
                Assert.Equal(new[] { "--mcp" }, client.ReadServers(null)["toolbox"].Args);

                AgentConnections.Connect(client, ConnectTarget.Global, @"C:\Program Files\T\Toolbox.exe", "a1");
                AgentConnections.Connect(client, new ConnectTarget { Path = @"C:\r", WorkspaceId = "w1", Name = "Repo" }, @"C:\Program Files\T\Toolbox.exe", "a2");

                var text = File.ReadAllText(config);
                Assert.Contains("model = \"gpt-5\"", text);
                Assert.Contains("[mcp_servers.github]", text);
                Assert.Contains("[profiles.fast]\nmodel = \"o4\"", text);
                Assert.DoesNotContain("[mcp_servers.toolbox.env]", text);
                Assert.Equal(1, text.Split('\n').Count(l => l == "[mcp_servers.toolbox]"));
                var servers = client.ReadServers(null);
                Assert.Equal(@"C:\Program Files\T\Toolbox.exe", servers["toolbox"].Command);
                Assert.Equal(new[] { "--mcp", "--agent", "a1" }, servers["toolbox"].Args);
                Assert.Equal(new[] { "--mcp", "--workspace", "w1", "--agent", "a2" }, servers["toolbox-repo"].Args);
                Assert.Equal(new[] { "-y", "gh" }, servers["github"].Args);

                AgentConnections.Disconnect(client, ConnectTarget.Global);

                Assert.False(client.ReadServers(null).ContainsKey("toolbox"));
                Assert.True(client.ReadServers(null).ContainsKey("toolbox-repo"));
                Assert.Contains("[profiles.fast]", File.ReadAllText(config));
            }
        }

        [Fact]
        public void ClaudeCode_DocTrangThaiTuClaudeJsonVaBaoLenhKhiThieuCli()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"app\Toolbox.exe", "");
                var repo = dir.Combine("repo");
                Directory.CreateDirectory(repo);
                var config = dir.WriteFile(".claude.json", "{ \"projects\": { \"" + repo.Replace("\\", "/") + "\": { \"mcpServers\": { \"toolbox\": { \"command\": \""
                    + exe.Replace("\\", "\\\\") + "\", \"args\": [\"--mcp\", \"--workspace\", \"w1\"] } } } } }");
                var client = new ClaudeCodeClient(config, findCli: () => null);
                var target = new ConnectTarget { Path = repo, WorkspaceId = "w1", Name = "repo" };

                Assert.Equal(new[] { repo }, client.KnownWorkspaces().ToArray());
                Assert.Equal(AgentLink.Connected, AgentConnections.State(client, target, exe).Link);
                Assert.Equal(AgentLink.NotConnected, AgentConnections.State(client, ConnectTarget.Global, exe).Link);

                var error = Assert.Throws<ClientUnavailableException>(() => AgentConnections.Connect(client, ConnectTarget.Global, exe, "a1"));
                Assert.Contains("claude mcp add --scope user toolbox -- ", error.ManualStep);
                Assert.Contains("--agent a1", error.ManualStep);
            }
        }

        [Fact]
        public void AgentKhac_CopyCauHinhMcpServers()
        {
            var snippet = AgentConnections.Snippet(new ManualClient(), new ConnectTarget { Path = @"C:\r", WorkspaceId = "w1", Name = "Repo" }, @"C:\T\Toolbox.exe", "a1");

            Assert.Contains("\"toolbox-repo\": { \"command\": \"C:\\\\T\\\\Toolbox.exe\", \"args\": [\"--mcp\", \"--workspace\", \"w1\", \"--agent\", \"a1\"] }", snippet);
        }

        [Theory]
        [InlineData("Dự án API (v2)", "du-an-api-v2")]
        [InlineData("  ", "workspace")]
        [InlineData("Đồ_án", "do-an")]
        public void Slug_ChuThuongKhongDau(string name, string expected) => Assert.Equal(expected, AgentConnections.Slug(name));

        [Fact]
        public void DisplayName_TheoIdHoacTenAppTuBao()
        {
            Assert.Equal("Cursor", AgentClientCatalog.DisplayName("cursor", "cursor-vscode"));
            Assert.Equal("Zed", AgentClientCatalog.DisplayName("other", "Zed"));
            Assert.Equal("Agent khác", AgentClientCatalog.DisplayName("other"));
            Assert.Equal("Agent", AgentClientCatalog.DisplayName(null));
        }

        [Fact]
        public void Initialize_GhiTenVaPhienBanAppAgent()
        {
            using (var dir = new TempDir())
            {
                var agentsPath = dir.Combine("agents.json");
                var store = AgentStore.Load(agentsPath);
                var agent = store.Add(new AgentRecord { Name = "Cursor · toàn máy", Client = "cursor" });
                store.Save();
                var server = new McpServer(() => ToolStore.Load(dir.Combine("tools.json")), null, () => WorkspaceStore.Load(dir.Combine("workspaces.json")),
                    agent: new AgentContext { AgentId = agent.Id, AgentsPath = agentsPath, ProposalsPath = dir.Combine("p.json"), ActivityPath = dir.Combine("a.jsonl") });

                server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"clientInfo\":{\"name\":\"cursor-vscode\",\"version\":\"1.7.2\"}}}");

                var saved = AgentStore.Load(agentsPath).Find(agent.Id);
                Assert.Equal("cursor-vscode", saved.ClientName);
                Assert.Equal("1.7.2", saved.ClientVersion);
                Assert.False(string.IsNullOrEmpty(saved.LastSeenAt));
            }
        }
    }
}
