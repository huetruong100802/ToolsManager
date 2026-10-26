using System.IO;
using System.Linq;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class ClaudeCodeTests
    {
        private static string Json(string path) => path.Replace("\\", "\\\\");

        [Fact]
        public void ReadConfig_DocKhaiBaoToanMayVaTheoWorkspace_GopCachVietCuaCungThuMuc()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"app\Toolbox.exe", "");
                var repo = dir.Combine("repo");
                Directory.CreateDirectory(repo);
                var slashRepo = repo.Replace('\\', '/');
                var lowerRepo = char.ToLowerInvariant(slashRepo[0]) + slashRepo.Substring(1);
                var config = dir.WriteFile(".claude.json", $@"{{
  ""mcpServers"": {{ ""toolbox"": {{ ""type"": ""stdio"", ""command"": ""{Json(exe)}"", ""args"": [""--mcp""] }} }},
  ""projects"": {{
    ""{slashRepo}"": {{ ""mcpServers"": {{}} }},
    ""{lowerRepo}/"": {{ ""mcpServers"": {{ ""toolbox"": {{ ""command"": ""C:\\old\\Toolbox.exe"" }} }} }},
    ""{Json(dir.Combine("da-xoa"))}"": {{ ""mcpServers"": {{}} }}
  }}
}}");

                var result = ClaudeCode.ReadConfig(config);

                Assert.Equal(exe, result.User.Command);
                Assert.Equal(AgentLink.Connected, result.User.LinkTo(exe));
                var workspace = Assert.Single(result.Workspaces); // thư mục đã xoá bị bỏ
                Assert.Equal(repo, workspace.Workspace);
                Assert.Equal(@"C:\old\Toolbox.exe", workspace.Command);
                Assert.Equal(AgentLink.Stale, workspace.LinkTo(exe));
                // Gỡ phải chạy ở đúng cách viết đang có khai báo.
                Assert.Equal(new[] { lowerRepo.Replace('/', '\\') + "\\" }, workspace.ConfiguredKeys);
            }
        }

        [Fact]
        public void ReadConfig_ChuaCoFile_TraVeRong()
        {
            using (var dir = new TempDir())
            {
                var result = ClaudeCode.ReadConfig(dir.Combine(".claude.json"));

                Assert.Null(result.User.Command);
                Assert.Empty(result.Workspaces);
            }
        }

        [Fact]
        public void ReadConfig_FileHong_BaoInvalidData()
        {
            using (var dir = new TempDir())
            {
                var config = dir.WriteFile(".claude.json", "{ hỏng");

                Assert.Throws<InvalidDataException>(() => ClaudeCode.ReadConfig(config));
            }
        }

        [Fact]
        public void LinkTo_ExeKhongConTonTai_LaStale()
        {
            var target = new ClaudeTarget { Command = @"C:\khong-co\Toolbox.exe" };

            Assert.Equal(AgentLink.Stale, target.LinkTo(@"C:\khong-co\Toolbox.exe"));
            Assert.Equal(AgentLink.NotConnected, new ClaudeTarget().LinkTo(@"C:\khong-co\Toolbox.exe"));
        }

        [Fact]
        public void Cli_ToanMayDungScopeUser_WorkspaceDungScopeLocalTaiThuMucDo()
        {
            var add = ClaudeCode.CreateCliStartInfo(@"C:\bin\claude.exe", ClaudeCode.AddArguments(null, @"C:\a b\Toolbox.exe"), null);
            Assert.Equal(@"C:\bin\claude.exe", add.FileName);
            Assert.Equal("mcp add --scope user toolbox -- \"C:\\a b\\Toolbox.exe\" --mcp", add.Arguments);
            Assert.True(add.RedirectStandardInput); // không để claude đọc stdin của Toolbox

            var remove = ClaudeCode.CreateCliStartInfo(@"C:\bin\claude.exe", ClaudeCode.RemoveArguments(@"C:\repo"), @"C:\repo");
            Assert.Equal("mcp remove --scope local toolbox", remove.Arguments);
            Assert.Equal(@"C:\repo", remove.WorkingDirectory);
        }

        [Fact]
        public void Cli_BanNpmLaCmd_ChayQuaCmdExe()
        {
            var info = ClaudeCode.CreateCliStartInfo(@"C:\npm\claude.cmd", ClaudeCode.RemoveArguments(null), null);

            Assert.Equal("cmd.exe", info.FileName);
            Assert.Equal("/d /s /c \"\"C:\\npm\\claude.cmd\" mcp remove --scope user toolbox\"", info.Arguments);
        }
    }
}
