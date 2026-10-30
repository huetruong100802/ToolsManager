using System.IO;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class DeskMcpTests
    {
        [Fact]
        public void Add_ToanMayVaWorkspace_DungLenhNpxQuaCmd()
        {
            Assert.Equal("mcp add --scope user desk-mcp -- cmd /c npx -y desk-mcp", ClaudeCode.AddArguments(DeskMcp.ServerName, null, DeskMcp.CommandArgs));
            Assert.Equal(@"mcp add --scope local desk-mcp -- cmd /c npx -y desk-mcp", ClaudeCode.AddArguments(DeskMcp.ServerName, @"C:\repo", DeskMcp.CommandArgs));
        }

        [Fact]
        public void Remove_ToanMayVaWorkspace_ChiGoDeskMcp()
        {
            Assert.Equal("mcp remove --scope user desk-mcp", ClaudeCode.RemoveArguments(DeskMcp.ServerName, null));
            Assert.Equal("mcp remove --scope local desk-mcp", ClaudeCode.RemoveArguments(DeskMcp.ServerName, @"C:\repo"));
        }

        [Fact]
        public void ReadConfig_DeskMcpChiMotWorkspace_KhongLanSangToolboxVaToanMay()
        {
            using (var dir = new TempDir())
            {
                var repo = dir.Combine("repo");
                Directory.CreateDirectory(repo);
                var slashRepo = repo.Replace('\\', '/');
                var config = dir.WriteFile(".claude.json", $@"{{
  ""mcpServers"": {{ ""toolbox"": {{ ""command"": ""C:\\a\\Toolbox.exe"" }} }},
  ""projects"": {{
    ""{slashRepo}"": {{ ""mcpServers"": {{ ""desk-mcp"": {{ ""command"": ""cmd"" }} }} }}
  }}
}}");

                var result = ClaudeCode.ReadConfig(config);

                Assert.Null(result.User.Entry(DeskMcp.ServerName));
                Assert.Equal(AgentLink.NotConnected, result.User.LinkTo(DeskMcp.ServerName));
                var workspace = Assert.Single(result.Workspaces);
                Assert.Equal(AgentLink.Connected, workspace.LinkTo(DeskMcp.ServerName));
                Assert.Null(workspace.Entry("toolbox"));
            }
        }

        [Fact]
        public void LinkTo_DeskMcpKhongCoExe_CoKhaiBaoLaDaKetNoi()
        {
            var target = new ClaudeTarget();
            target.Servers[DeskMcp.ServerName] = new McpEntry { Command = "cmd" };

            Assert.Equal(AgentLink.Connected, target.LinkTo(DeskMcp.ServerName));
        }
    }
}
