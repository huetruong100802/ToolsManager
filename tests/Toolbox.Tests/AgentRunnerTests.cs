using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class AgentRunnerTests
    {
        private static string Fake(TempDir dir, string name, string body) => dir.WriteFile(name + ".cmd", "@echo off\r\n" + body + "\r\n");

        [Fact]
        public void TryParse_TachChuongTrinhVaThamSo_ChapNhanDuongDanCoDauCach()
        {
            Assert.True(AgentRunner.TryParse("claude -p --model x", out var p, out var a));
            Assert.Equal("claude", p);
            Assert.Equal("-p --model x", a);

            Assert.True(AgentRunner.TryParse("\"C:\\Program Files\\a b\\agent.exe\" -p", out p, out a));
            Assert.Equal("C:\\Program Files\\a b\\agent.exe", p);
            Assert.Equal("-p", a);

            Assert.False(AgentRunner.TryParse("   ", out _, out _));
            Assert.False(AgentRunner.TryParse("\"khong dong", out _, out _));
        }

        [Fact]
        public void Run_ThieuCLI_BaoMissingVaCachCai_KhongTreo()
        {
            var result = AgentRunner.Run("khong-co-agent-nao-ca-xyz -p", "hi", null, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Equal(AgentRunStatus.Missing, result.Status);
            Assert.Contains("khong-co-agent-nao-ca-xyz", result.Error);
            Assert.Contains("npm install", result.Error);
        }

        [Fact]
        public void Run_DocPromptQuaStdinVaTraStdout()
        {
            using (var dir = new TempDir())
            {
                var cmd = Fake(dir, "echo-agent", "findstr \"^\"");
                var result = AgentRunner.Run("\"" + cmd + "\"", "xin chao\nhai dong", dir.Path, TimeSpan.FromSeconds(20), CancellationToken.None);
                Assert.Equal(AgentRunStatus.Ok, result.Status);
                Assert.Contains("xin chao", result.Output);
                Assert.Contains("hai dong", result.Output);
            }
        }

        [Fact]
        public void Run_MaThoatKhac0_GiuNguyenVanLoi()
        {
            using (var dir = new TempDir())
            {
                var cmd = Fake(dir, "fail-agent", "echo loi dang nhap 1>&2\r\nexit /b 3");
                var result = AgentRunner.Run("\"" + cmd + "\"", "x", dir.Path, TimeSpan.FromSeconds(20), CancellationToken.None);
                Assert.Equal(AgentRunStatus.Failed, result.Status);
                Assert.Equal(3, result.ExitCode);
                Assert.Contains("loi dang nhap", result.Error);
            }
        }

        [Fact]
        public void Run_QuaGio_DungTienTrinhVaBaoTimedOut()
        {
            using (var dir = new TempDir())
            {
                var cmd = Fake(dir, "slow-agent", "ping -n 30 127.0.0.1 >nul");
                var watch = Stopwatch.StartNew();
                var result = AgentRunner.Run("\"" + cmd + "\"", "x", dir.Path, TimeSpan.FromMilliseconds(800), CancellationToken.None);
                Assert.Equal(AgentRunStatus.TimedOut, result.Status);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
            }
        }

        [Fact]
        public void Run_Huy_DungTienTrinhCon()
        {
            using (var dir = new TempDir())
            using (var cts = new CancellationTokenSource())
            {
                var marker = dir.Combine("alive.txt");
                // Tiến trình con ghi dấu mỗi giây: sau khi huỷ, dấu không được ghi thêm.
                var cmd = Fake(dir, "loop-agent", "for /l %%i in (1,1,60) do (echo x>>\"" + marker + "\" & ping -n 2 127.0.0.1 >nul)");
                var task = AgentRunner.RunAsync("\"" + cmd + "\"", "x", dir.Path, TimeSpan.FromMinutes(1), cts.Token);
                Thread.Sleep(1500);
                cts.Cancel();
                Assert.True(task.Wait(TimeSpan.FromSeconds(15)));
                Assert.Equal(AgentRunStatus.Cancelled, task.Result.Status);

                Thread.Sleep(500);
                var size = File.Exists(marker) ? new FileInfo(marker).Length : 0;
                Thread.Sleep(2500);
                var after = File.Exists(marker) ? new FileInfo(marker).Length : 0;
                Assert.Equal(size, after);
            }
        }
    }

    public class SetupProposalsTests
    {
        private static Workspace Ws() => new Workspace { Id = "w", Name = "Demo", Path = Path.GetTempPath() };

        [Fact]
        public void Parse_ChapNhanRaoCodeVaLoiDan_LayDungMuc()
        {
            var output = "Đây là đề xuất:\n```json\n{\"commands\":[{\"label\":\"Chạy dev\",\"commandLine\":\"npm run start\",\"cwd\":\"web\",\"reason\":\"script start\"}]}\n```";
            var result = SetupProposals.Parse(output, Ws());
            Assert.True(result.Ok);
            var item = Assert.Single(result.Items);
            Assert.Equal("Chạy dev", item.Label);
            Assert.Equal("npm run start", item.CommandLine);
            Assert.Equal("web", item.Cwd);
        }

        [Fact]
        public void Parse_KhongPhaiJson_LoiKemDauRa_KhongMucNao()
        {
            var result = SetupProposals.Parse("Xin lỗi, tôi không làm được", Ws());
            Assert.False(result.Ok);
            Assert.Empty(result.Items);
            Assert.Contains("Xin lỗi", result.Error);
        }

        [Fact]
        public void Parse_BoMucCwdRaNgoaiTrungHoacThieuTruong()
        {
            var ws = Ws();
            ws.Customs.Add(new CustomCommand { Id = "c1", Label = "Cũ", CommandLine = "npm test" });
            var output = "{\"commands\":["
                + "{\"label\":\"A\",\"commandLine\":\"npm ci\",\"cwd\":\"..\\\\x\"},"
                + "{\"label\":\"B\",\"commandLine\":\"npm ci\",\"cwd\":\"C:\\\\Windows\"},"
                + "{\"label\":\"\",\"commandLine\":\"npm ci\"},"
                + "{\"label\":\"D\",\"commandLine\":\"npm test\"},"
                + "{\"label\":\"E\",\"commandLine\":\"npm run build\",\"cwd\":\"web/app\"},"
                + "{\"label\":\"E2\",\"commandLine\":\"npm run build\",\"cwd\":\"web/app\"}]}";
            var result = SetupProposals.Parse(output, ws);
            Assert.True(result.Ok);
            var item = Assert.Single(result.Items);
            Assert.Equal("E", item.Label);
            Assert.Equal("web\\app", item.Cwd);
            Assert.Equal(5, result.Skipped);
        }

        [Fact]
        public void Apply_ChiThemMucDuocDuyet()
        {
            var ws = Ws();
            var added = SetupProposals.Apply(ws, new[] { new SetupItem { Label = "Dev", CommandLine = "npm run start" } });
            Assert.Equal(1, added);
            Assert.Equal("npm run start", Assert.Single(ws.Customs).CommandLine);
        }
    }
}
