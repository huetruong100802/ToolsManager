using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    public class RunDockTests
    {
        private static TaskInfo Task(string id, string key, TaskState state, int? port = null)
        {
            var task = new TaskInfo(id, new TaskSpec { WorkspaceId = "w", Label = "Tác vụ " + id, CommandLine = "x", Port = port, Key = key }, null);
            if (state != TaskState.Running)
                task.Finish(state, state == TaskState.Failed ? 2 : 0);
            return task;
        }

        [Fact]
        public void Build_DangChayRoiLoiChuaXem_BoLoiDaXemVaLoiDaChayLaiThanhCong()
        {
            var seen = new SeenFailures();
            var running = Task("r", "k1", TaskState.Running, 4200);
            var failed = Task("f", "k2", TaskState.Failed);
            var seenFailed = Task("s", "k3", TaskState.Failed);
            seen.Mark("s");
            var failedOld = Task("o1", "k4", TaskState.Failed);
            System.Threading.Thread.Sleep(15);
            var recovered = Task("o2", "k4", TaskState.Done);

            var chips = DockChips.Build(new[] { running, failed, seenFailed, failedOld, recovered }, seen, DateTime.UtcNow);
            Assert.Equal(new[] { "r", "f" }, chips.Select(c => c.Task.Id));
            Assert.False(chips[0].Failed);
            Assert.True(chips[1].Failed);
        }

        [Fact]
        public void Update_GomChipThua_GiuChipLoiDoDenKhiXem()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var dock = new RunDock();
                var tasks = Enumerable.Range(0, 6).Select(i => Task("t" + i, "k" + i, TaskState.Running, 4000 + i)).ToList();
                dock.Update(tasks);
                Assert.Equal(DockChips.MaxShown, dock.Rows.Count);
                var summary = (System.Windows.Controls.TextBlock)dock.FindName("Summary");
                Assert.Contains("+2 nữa", summary.Text);
                Assert.Contains("6 đang chạy", summary.Text);

                var failed = Task("bad", "kx", TaskState.Failed);
                dock.Update(new[] { failed });
                Assert.Single(dock.Rows);
                Assert.True(dock.Rows[0].Failed);
                Assert.Equal("Đóng", dock.Rows[0].LastActionText);

                dock.MarkSeen("bad");
                Assert.Empty(dock.Rows);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)dock.FindName("Idle")).Visibility);
            });
        }

        [Fact]
        public void Chip_CoCongThiCoNutMo_KhongCongThiKhong()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var dock = new RunDock();
                dock.Update(new[] { Task("a", "k1", TaskState.Running, 4200), Task("b", "k2", TaskState.Running) });
                Assert.Equal(Visibility.Visible, dock.Rows[0].OpenVisibility);
                Assert.Equal(":4200", dock.Rows[0].PortText);
                Assert.Equal(Visibility.Collapsed, dock.Rows[1].OpenVisibility);
                Assert.Equal("Dừng", dock.Rows[1].LastActionText);
            });
        }
    }
}
