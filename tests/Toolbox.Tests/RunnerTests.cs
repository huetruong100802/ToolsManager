using System;
using System.IO;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class RunnerTests
    {
        [Fact]
        public void Ps1_ChayQuaPowerShell_QuaShellExecute()
        {
            using (var dir = new TempDir())
            {
                var script = dir.WriteFile(@"s\run.ps1", "");
                var info = Runner.CreateStartInfo(new ToolEntry { Name = "s", Path = script, Args = "-X 1" });

                Assert.Equal("powershell.exe", info.FileName);
                Assert.EndsWith($"-File \"{script}\" -X 1", info.Arguments);
                Assert.Equal(Path.GetDirectoryName(script), info.WorkingDirectory);
                Assert.True(info.UseShellExecute); // không kế thừa stdin/stdout của Toolbox
            }
        }

        [Fact]
        public void Exe_ChayTrucTiep_ArgsTruyenVaoGhiDeMacDinh()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"e\tool.exe", "");
                var tool = new ToolEntry { Name = "e", Path = exe, Args = "--port 502" };

                Assert.Equal("--port 502", Runner.CreateStartInfo(tool).Arguments);
                Assert.Equal("--port 503", Runner.CreateStartInfo(tool, "--port 503").Arguments);
            }
        }

        [Fact]
        public void PathKhongTonTai_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<FileNotFoundException>(() => Runner.CreateStartInfo(new ToolEntry { Name = "x", Path = dir.Combine("mat.exe") }));
        }

        [Fact]
        public void Capture_RedirectCaStdin()
        {
            using (var dir = new TempDir())
            {
                var info = Runner.CreateCaptureStartInfo(new ToolEntry { Name = "b", Path = dir.WriteFile("b.bat", "") });
                Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
                Assert.False(info.UseShellExecute);
            }
        }

        [Fact]
        public void Capture_DuoiKhongHoTro_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<NotSupportedException>(() => Runner.CreateCaptureStartInfo(new ToolEntry { Name = "t", Path = dir.WriteFile("a.txt", "") }));
        }

        [Fact]
        public void RunAndCapture_QuaThoiGian_DungTienTrinh()
        {
            using (var dir = new TempDir())
            {
                var bat = dir.WriteFile("slow.bat", "@ping -n 30 127.0.0.1 >nul\r\n");
                var started = DateTime.UtcNow;

                var result = Runner.RunAndCapture(new ToolEntry { Name = "slow", Path = bat }, null, TimeSpan.FromSeconds(1));

                Assert.True(result.TimedOut);
                Assert.Null(result.ExitCode);
                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
            }
        }

        [Fact]
        public void SuggestPack_ProgramFiles_ChiThongTin()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            Assert.Equal(PackMode.None, ToolDefaults.SuggestPack(Path.Combine(programFiles, @"Vendor\app.exe")));
        }

        [Fact]
        public void SuggestPack_ThuMucRieng_LayCaThuMuc()
        {
            using (var dir = new TempDir())
                Assert.Equal(PackMode.Folder, ToolDefaults.SuggestPack(dir.WriteFile(@"tool\a.exe", "")));
        }
    }
}
