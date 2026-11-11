using System;
using System.Diagnostics;
using System.Threading;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    [Collection("ChayTienTrinh")] // các test bật tiến trình thật chạy lần lượt, không tranh CPU nhau
    public class RunTrackerTests
    {
        private static readonly ToolEntry Tool = new ToolEntry { Id = "t", Name = "t", Path = @"C:\Windows\System32\cmd.exe" };

        // Chạy cmd ẩn: test không được bật cửa sổ console như Runner.CreateStartInfo.
        private static RunTracker Tracker() => new RunTracker((tool, args) =>
            Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = false, CreateNoWindow = true }));

        private static void WaitUntilStopped(RunTracker tracker) =>
            Assert.True(SpinWait.SpinUntil(() => tracker.Current("t") == null, TimeSpan.FromSeconds(10)), "tiến trình không kết thúc");

        [Fact]
        public void Start_ThoatMaLoi_GhiNhoMaThoat()
        {
            var tracker = Tracker();

            Assert.NotNull(tracker.Start(Tool, "/c exit 3"));
            WaitUntilStopped(tracker);

            var last = tracker.Last("t");
            Assert.Equal(3, last.ExitCode);
            Assert.False(last.Stopped);
        }

        [Fact]
        public void Start_ThoatSachNgay_LaBoKhoiDongNenKhongGhiKetQua()
        {
            var tracker = Tracker();

            tracker.Start(Tool, "/c exit 0");
            WaitUntilStopped(tracker);

            Assert.Null(tracker.Last("t"));
        }

        [Fact]
        public void Start_ThoatSachNhungSauKhiHetCuaSoBoKhoiDong_VanGhiKetQua()
        {
            var tracker = Tracker();
            tracker.HandoffWindow = TimeSpan.Zero;

            tracker.Start(Tool, "/c exit 0");
            WaitUntilStopped(tracker);

            Assert.Equal(0, tracker.Last("t").ExitCode);
        }

        [Fact]
        public void Stop_DungTienTrinhDangChay_DanhDauDaDung()
        {
            var tracker = Tracker();
            var changed = 0;
            tracker.Changed += () => Interlocked.Increment(ref changed);

            var run = tracker.Start(Tool, "/c ping -n 60 127.0.0.1 >nul");
            Assert.True(run.IsRunning);
            Assert.Equal(1, tracker.RunningCount);

            tracker.Stop("t");
            WaitUntilStopped(tracker);

            Assert.True(tracker.Last("t").Stopped);
            Assert.Equal(0, tracker.RunningCount);
            // Changed bắn sau khi run đã bị gỡ khỏi danh sách nên WaitUntilStopped có thể về trước; chờ đủ 2 lần.
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref changed) >= 2, TimeSpan.FromSeconds(5)),
                "thiếu sự kiện Changed khi kết thúc"); // lúc bắt đầu và lúc kết thúc
        }

        [Fact]
        public void Start_ThuMucChiMoExplorer_KhongTheoDoi()
        {
            var folder = new ToolEntry { Id = "f", Name = "f", Path = Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            var tracker = new RunTracker((tool, args) => Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true }));

            Assert.Null(tracker.Start(folder));
            Assert.Equal(0, tracker.RunningCount);
        }

        [Theory]
        [InlineData(0, "0 giây")]
        [InlineData(59, "59 giây")]
        [InlineData(60, "1 phút")]
        [InlineData(3599, "59 phút")]
        [InlineData(3600, "1 giờ 0 phút")]
        [InlineData(7500, "2 giờ 5 phút")]
        public void Duration_DinhDangTiengViet(int seconds, string expected) =>
            Assert.Equal(expected, Text.Duration(TimeSpan.FromSeconds(seconds)));

        [Theory]
        [InlineData(3, "vừa xong")]
        [InlineData(30, "30 giây trước")]
        [InlineData(300, "5 phút trước")]
        [InlineData(7200, "2 giờ trước")]
        [InlineData(172800, "2 ngày trước")]
        public void Ago_DinhDangTiengViet(int seconds, string expected) =>
            Assert.Equal(expected, Text.Ago(TimeSpan.FromSeconds(seconds)));
    }
}
