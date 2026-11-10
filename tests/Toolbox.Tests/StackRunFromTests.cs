using System;
using System.IO;
using System.Linq;
using System.Threading;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>"Chạy lại từ bước này": bỏ qua các bước đã xong, chỉ kiểm và chạy từ bước chọn; nhóm song song lùi về đầu nhóm.</summary>
    [Collection("ChayTienTrinh")] // các test bật tiến trình thật chạy lần lượt, không tranh CPU nhau
    public class StackRunFromTests : IDisposable
    {
        private readonly TempDir _dir = new TempDir();
        private readonly TaskHost _host;

        public StackRunFromTests()
        {
            _host = new TaskHost(_dir.Combine("runs"));
        }

        public void Dispose()
        {
            _host.StopAll();
            SpinWait.SpinUntil(() => _host.Running().Count == 0, TimeSpan.FromSeconds(10));
            _dir.Dispose();
        }

        private static CustomCommand Cmd(string id, string line) => new CustomCommand { Id = id, Label = "lệnh " + id, CommandLine = line };

        private static StackStep Step(string id, int group = 0) =>
            new StackStep { Kind = TaskKey.Custom, ActionId = id, Wait = WaitKind.Exit, Group = group };

        private StackRunner Runner() =>
            new StackRunner(_host) { PollInterval = TimeSpan.FromMilliseconds(30), PortTimeout = TimeSpan.FromSeconds(20) };

        private static void Finished(StackRun run) => Assert.True(run.Completion.Wait(TimeSpan.FromSeconds(30)), "workflow không kết thúc");

        private Workspace Ws(params CustomCommand[] commands)
        {
            var ws = new Workspace { Id = "w1", Name = "Thử", Path = _dir.Path };
            ws.Customs.AddRange(commands);
            return ws;
        }

        [Fact]
        public void ChayLaiTuBuocLoi_BoQuaBuocDaXong_VaThanhCongSauKhiSua()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "exit 1"), Cmd("c", "echo c"));
            var stack = new StackDef { Id = "s1", Label = "Ba bước", Steps = new[] { Step("a"), Step("b"), Step("c") }.ToList() };

            var first = Runner().Start(ws, stack);
            Finished(first);
            Assert.Equal(RunState.Failed, first.State);
            Assert.Equal(1, first.FirstFailedIndex);
            Assert.Equal(StepStatus.Done, first.Steps[0].Status);
            var startedBefore = _host.Snapshot().Count;

            ws.Customs.Single(c => c.Id == "b").CommandLine = "echo b"; // người dùng sửa bước 2
            var second = Runner().Start(ws, stack, first.FirstFailedIndex);
            Finished(second);

            Assert.Equal(RunState.Succeeded, second.State);
            Assert.Equal(1, second.StartedFrom);
            Assert.Equal(StepStatus.Done, second.Steps[0].Status);
            Assert.Contains("lần chạy trước", second.Steps[0].Detail);
            // Chỉ bước 2 và 3 được bật lại: đúng 2 tác vụ mới, bước 1 không chạy lại.
            Assert.Equal(startedBefore + 2, _host.Snapshot().Count);
            Assert.DoesNotContain(_host.Snapshot().Skip(startedBefore), t => t.Spec.Label.Contains("lệnh a"));
        }

        [Fact]
        public void ChayLaiTuGiuaNhom_LuiVeDauNhom()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"), Cmd("c", "echo c"), Cmd("d", "echo d"));
            var stack = new StackDef { Id = "s1", Label = "Nhóm", Steps = new[] { Step("a"), Step("b", 1), Step("c", 1), Step("d") }.ToList() };

            var run = Runner().Start(ws, stack, 2); // bước 3 nằm trong nhóm bước 2-3
            Finished(run);

            Assert.Equal(1, run.StartedFrom);
            Assert.Equal(RunState.Succeeded, run.State);
            Assert.Equal(StepStatus.Done, run.Steps[0].Status);
        }

        [Fact]
        public void ChayLaiTuBuoc_BoQuaKiemTraBuocTruoc_KeCaBuocHong()
        {
            var ws = Ws(Cmd("b", "echo b"));
            var stack = new StackDef
            {
                Id = "s1", Label = "Bước đầu hỏng",
                Steps = new[] { new StackStep { Kind = TaskKey.Custom, ActionId = "khong-co", Wait = WaitKind.Exit }, Step("b") }.ToList(),
            };

            var full = Runner().Start(ws, stack);
            Assert.Equal(RunState.Failed, full.State); // chạy từ đầu: bước 1 hỏng nên không bật gì

            var from = Runner().Start(ws, stack, 1);
            Finished(from);
            Assert.Equal(RunState.Succeeded, from.State);
        }

        [Fact]
        public void TheWorkflow_HienBuocLoiVaNutChayLai()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "exit 1"));
            var stack = new StackDef { Id = "s1", Label = "Hai bước", Steps = new[] { Step("a"), Step("b") }.ToList() };
            var row = new Toolbox.Views.StackRow(ws, stack);
            Assert.False(row.HasFailedStep);

            var run = Runner().Start(ws, stack);
            Finished(run);
            row.Update(run);

            Assert.True(row.HasFailedStep);
            Assert.Equal("Chạy lại từ bước 2", row.RetryText);
            Assert.Equal(System.Windows.Visibility.Visible, row.FailedVisibility);
            Assert.NotNull(row.FailedTaskId);
            Assert.Equal("Done", row.Steps[0].Kind);
            Assert.Equal("Failed", row.Steps[1].Kind);
            Assert.NotNull(_host.Find(row.FailedTaskId)); // mở log đúng tác vụ của bước lỗi
        }

        [Fact]
        public void Since_DoiKhiBuocDoiTrangThai()
        {
            var ws = Ws(Cmd("a", "echo a"));
            var stack = new StackDef { Id = "s1", Label = "Một bước", Steps = new[] { Step("a") }.ToList() };
            var before = DateTime.UtcNow;
            var run = Runner().Start(ws, stack);
            Finished(run);
            Assert.True(run.Steps[0].Since >= before.AddSeconds(-1));
            Assert.Equal(-1, run.FirstFailedIndex);
        }
    }
}
