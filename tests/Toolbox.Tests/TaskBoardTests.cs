using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class TaskBoardTests
    {
        private static TaskInfo Task(string id, int? port = null) =>
            new TaskInfo(id, new TaskSpec { WorkspaceId = "w", Label = id, CommandLine = "x", Port = port, Key = "w|t|a|v|" }, null);

        [Fact]
        public void Ordered_TacVuDangChayDungTruocDaKetThuc()
        {
            var done = Task("xong");
            done.Finish(TaskState.Done, 0);
            var running = Task("dang");

            var ordered = TaskBoard.Ordered(new[] { done, running });

            Assert.Equal(new[] { "dang", "xong" }, ordered.Select(t => t.Id));
        }

        [Fact]
        public void StateText_PhanBietCacTrangThai()
        {
            var failed = Task("loi");
            failed.Finish(TaskState.Failed, 3);
            var stopped = Task("dung");
            stopped.Finish(TaskState.Stopped, null);
            var done = Task("xong");
            done.Finish(TaskState.Done, 0);

            Assert.Equal("Đang chạy", TaskBoard.StateText(Task("dang")));
            Assert.Equal("Lỗi, mã 3", TaskBoard.StateText(failed));
            Assert.Equal("Đã dừng", TaskBoard.StateText(stopped));
            Assert.Equal("Xong", TaskBoard.StateText(done));
        }

        [Fact]
        public void Elapsed_TacVuDangChayTinhToiMocDuocDua()
        {
            var task = Task("dang");

            var span = TaskBoard.Elapsed(task, task.StartedAt.AddSeconds(30));

            Assert.Equal(TimeSpan.FromSeconds(30), span);
        }

        [Fact]
        public void Elapsed_MocTruocLucBatDauLaKhong()
        {
            var task = Task("dang");

            Assert.Equal(TimeSpan.Zero, TaskBoard.Elapsed(task, task.StartedAt.AddSeconds(-5)));
        }

        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(65, "1:05")]
        [InlineData(3599, "59:59")]
        [InlineData(3725, "1:02:05")]
        public void FormatElapsed_MPhutHoacGio(int seconds, string expected) =>
            Assert.Equal(expected, TaskBoard.FormatElapsed(TimeSpan.FromSeconds(seconds)));

        [Fact]
        public void PortsOf_ChiLayCongDangChayKhongTrung()
        {
            var a = Task("a", 5000);
            var b = Task("b", 4200);
            var c = Task("c", 4200);
            var finished = Task("d", 9000);
            finished.Finish(TaskState.Done, 0);
            var noPort = Task("e");

            var ports = TaskBoard.PortsOf(new[] { a, b, c, finished, noPort });

            Assert.Equal(new[] { 4200, 5000 }, ports);
        }

        [Fact]
        public void StatusLine_KhongCoTacVuVaCoCong()
        {
            Assert.Equal("Không có tác vụ chạy ngầm", TaskBoard.StatusLine(new List<TaskInfo>()));

            var running = new List<TaskInfo> { Task("a", 4200), Task("b") };
            Assert.Equal("Đang chạy 2 tác vụ · cổng 4200", TaskBoard.StatusLine(running));
        }
    }
}
