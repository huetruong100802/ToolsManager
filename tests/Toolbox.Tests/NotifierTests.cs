using System;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class NotifierTests
    {
        /// <summary>Tác vụ đã kết thúc sau <paramref name="seconds"/> giây chạy.</summary>
        private static TaskInfo Finished(string id, TaskState state, double seconds)
        {
            var task = new TaskInfo(id, new TaskSpec { WorkspaceId = "w", Label = "Tác vụ " + id, CommandLine = "x", Key = "k" + id }, null);
            task.StartedAt = DateTime.UtcNow.AddSeconds(-seconds);
            task.Finish(state, state == TaskState.Failed ? 1 : 0);
            return task;
        }

        [Theory]
        [InlineData(TaskState.Done, 45, true, false, true)]
        [InlineData(TaskState.Failed, 31, true, false, true)]
        [InlineData(TaskState.Done, 10, true, false, false)]   // ngắn quá
        [InlineData(TaskState.Done, 45, true, true, false)]    // cửa sổ đang ở trước
        [InlineData(TaskState.Done, 45, false, false, false)]  // đã tắt thông báo
        [InlineData(TaskState.Stopped, 45, true, false, false)] // tự dừng thì không báo
        public void ShouldNotify_TheoNguongThoiGianCuaSoVaCongTac(TaskState state, double seconds, bool enabled, bool windowActive, bool expected) =>
            Assert.Equal(expected, Notifier.ShouldNotify(Finished("a", state, seconds), enabled, windowActive));

        [Fact]
        public void ShouldNotify_TacVuDangChay_KhongBao()
        {
            var running = new TaskInfo("r", new TaskSpec { WorkspaceId = "w", Label = "x", CommandLine = "x" }, null);
            running.StartedAt = DateTime.UtcNow.AddMinutes(-5);
            Assert.False(Notifier.ShouldNotify(running, true, false));
        }

        [Fact]
        public void MarkExisting_VaCheck_MoiTacVuChiXetMotLan()
        {
            using (var notifier = new Notifier())
            {
                var old = Finished("old", TaskState.Failed, 120);
                notifier.MarkExisting(new[] { old });
                // Cửa sổ đang ở trước (windowActive) nên không hiện thông báo thật trong test.
                var first = notifier.Check(new[] { old, Finished("new", TaskState.Done, 60) }, enabled: true, windowActive: true);
                Assert.Empty(first);
                // Lần sau cửa sổ ra sau: "new" đã được xét rồi nên không bị báo bù.
                var second = notifier.Check(new[] { old, Finished("new", TaskState.Done, 60) }, enabled: true, windowActive: false);
                Assert.Empty(second);
            }
        }

        [Fact]
        public void Title_VaText_NoiTenVaThoiGian()
        {
            var failed = Finished("f", TaskState.Failed, 90);
            Assert.Equal("Tác vụ bị lỗi", Notifier.TitleFor(failed));
            Assert.Contains("Tác vụ f", Notifier.TextFor(failed));
            Assert.Contains("mã thoát 1", Notifier.TextFor(failed));
            Assert.Equal("Tác vụ đã xong", Notifier.TitleFor(Finished("d", TaskState.Done, 40)));
        }

        [Fact]
        public void Settings_Update_KhongDeCaiDatDoNoiKhacVuaGhi()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("settings.json");
                Settings.Update(s => s.Theme = "dark", path);
                Settings.Update(s => s.NotifyOff = true, path);
                var loaded = Settings.Load(path);
                Assert.Equal("dark", loaded.Theme);
                Assert.True(loaded.NotifyOff);
                Assert.False(Settings.Load(dir.Combine("khong-co.json")).NotifyOff); // mặc định bật thông báo
            }
        }
    }
}
