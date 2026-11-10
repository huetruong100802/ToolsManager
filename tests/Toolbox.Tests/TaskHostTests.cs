using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    [Collection("ChayTienTrinh")] // các test bật tiến trình thật chạy lần lượt, không tranh CPU nhau
    public class TaskHostTests
    {
        private static TaskSpec Spec(string line, int? port = null, string cwd = null) =>
            new TaskSpec { WorkspaceId = "w", Label = "thử", CommandLine = line, Cwd = cwd ?? Path.GetTempPath(), Port = port, Key = "w|t|a|v|" };

        private static void WaitFinished(TaskInfo task) =>
            Assert.True(SpinWait.SpinUntil(() => !task.IsRunning, TimeSpan.FromSeconds(15)), "tác vụ không kết thúc");

        [Fact]
        public void Start_GhiOutputVaoBoDemVaFileLog()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);

                var task = host.Start(Spec("echo xin chao"));
                WaitFinished(task);

                Assert.Equal(TaskState.Done, task.State);
                Assert.Equal(0, task.ExitCode);
                Assert.Contains("xin chao", task.Lines());
                var log = File.ReadAllText(task.LogPath);
                Assert.Contains("xin chao", log);
                Assert.Contains("# kết thúc: Done", log);
            }
        }

        [Fact]
        public void Start_ThoatMaLoi_LaFailed()
        {
            using (var dir = new TempDir())
            {
                var task = new TaskHost(dir.Path).Start(Spec("exit /b 3"));
                WaitFinished(task);

                Assert.Equal(TaskState.Failed, task.State);
                Assert.Equal(3, task.ExitCode);
            }
        }

        [Fact]
        public void Start_BatCongTuOutput()
        {
            using (var dir = new TempDir())
            {
                var task = new TaskHost(dir.Path).Start(Spec("echo Local: http://localhost:4321/"));
                WaitFinished(task);

                Assert.Equal(4321, task.Port);
            }
        }

        [Fact]
        public void Start_CongKhaiBaoThangCongBatDuoc()
        {
            using (var dir = new TempDir())
            {
                var task = new TaskHost(dir.Path).Start(Spec("echo http://localhost:4321/", port: 5000));
                WaitFinished(task);

                Assert.Equal(5000, task.Port);
            }
        }

        [Fact]
        public void Stop_DungTienTrinhVaGiaiPhongCong()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);
                var task = host.Start(Spec("ping -n 60 127.0.0.1 >nul", port: 5555));

                Assert.Equal("thử", host.UsedPorts()[5555]);
                host.Stop(task.Id);
                WaitFinished(task);

                Assert.Equal(TaskState.Stopped, task.State);
                Assert.Empty(host.UsedPorts());
                Assert.Empty(host.Running());
            }
        }

        [Fact]
        public void Restart_ChayLaiCungLenhVoiIdMoi()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);
                var first = host.Start(Spec("echo lan mot"));
                WaitFinished(first);

                var second = host.Restart(first.Id);
                WaitFinished(second);

                Assert.NotEqual(first.Id, second.Id);
                Assert.Equal(first.Spec.CommandLine, second.Spec.CommandLine);
                Assert.Equal(2, host.Snapshot().Count);
            }
        }

        [Fact]
        public void Restart_DangChay_DungBanCuTruoc()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);
                var first = host.Start(Spec("ping -n 60 127.0.0.1 >nul"));

                var second = host.Restart(first.Id);
                try
                {
                    Assert.Equal(TaskState.Stopped, first.State);
                    Assert.True(second.IsRunning);
                }
                finally { host.StopAll(); }
            }
        }

        [Fact]
        public void Start_ThuMucKhongTonTai_NemLoiVaKhongGhiNhan()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);

                Assert.Throws<Win32Exception>(() => host.Start(Spec("echo x", cwd: @"C:\khong\ton\tai\toolbox")));
                Assert.Empty(host.Snapshot());
            }
        }

        [Fact]
        public void Start_LenhTrong_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<ArgumentException>(() => new TaskHost(dir.Path).Start(Spec("  ")));
        }

        [Fact]
        public void Changed_BanKhiBatDauVaKhiKetThuc()
        {
            using (var dir = new TempDir())
            {
                var host = new TaskHost(dir.Path);
                var changed = 0;
                host.Changed += () => Interlocked.Increment(ref changed);

                WaitFinished(host.Start(Spec("echo a")));
                SpinWait.SpinUntil(() => changed >= 2, TimeSpan.FromSeconds(5));

                Assert.True(changed >= 2);
            }
        }
    }
}
