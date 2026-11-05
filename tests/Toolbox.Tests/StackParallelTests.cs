using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class StackParallelTests : IDisposable
    {
        private const string LongRunning = "ping -n 60 127.0.0.1 >nul";

        private readonly TempDir _dir = new TempDir();
        private readonly TaskHost _host;

        public StackParallelTests()
        {
            _host = new TaskHost(_dir.Combine("runs"));
        }

        public void Dispose()
        {
            _host.StopAll();
            SpinWait.SpinUntil(() => _host.Running().Count == 0, TimeSpan.FromSeconds(10));
            _dir.Dispose();
        }

        private Workspace Ws(params CustomCommand[] commands)
        {
            var ws = new Workspace { Id = "w1", Name = "Thử", Path = _dir.Path };
            ws.Customs.AddRange(commands);
            return ws;
        }

        private static CustomCommand Cmd(string id, string line) =>
            new CustomCommand { Id = id, Label = "lệnh " + id, CommandLine = line };

        private static StackStep Step(string customId, int group = 0, string wait = null, int port = 0) =>
            new StackStep { Kind = TaskKey.Custom, ActionId = customId, Group = group, Wait = wait, Port = port };

        private static StackDef Stack(params StackStep[] steps) =>
            new StackDef { Id = "s1", Label = "Bộ thử", Steps = steps.ToList() };

        private StackRunner Runner(Func<int, bool> probe = null) =>
            new StackRunner(_host, probe) { PollInterval = TimeSpan.FromMilliseconds(30), PortTimeout = TimeSpan.FromSeconds(20) };

        private static void Finished(StackRun run) =>
            Assert.True(run.Completion.Wait(TimeSpan.FromSeconds(20)), "bộ chạy không kết thúc");

        private static void Eventually(Func<bool> condition, string what) =>
            Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(15)), "không đạt: " + what);

        private static string Show(System.Collections.Generic.List<System.Collections.Generic.List<int>> batches) =>
            string.Join("|", batches.Select(b => string.Join(",", b)));

        // ---- JSON ----

        [Fact]
        public void Json_GroupKhuDoiTrip()
        {
            var stack = Stack(Step("a", 2), Step("b", 2), Step("c"));

            var json = ToJson(stack);
            var back = FromJson(json);

            Assert.Contains("\"group\":2", json);
            Assert.Equal(new[] { 2, 2, 0 }, back.Steps.Select(s => s.Group).ToArray());
        }

        [Fact]
        public void Json_BuocKhongNhom_KhongGhiTruongGroup()
        {
            Assert.DoesNotContain("group", ToJson(Stack(Step("a"))));
        }

        [Fact]
        public void Json_FileCuKhongCoGroup_DocLa0()
        {
            const string old = "{\"id\":\"s1\",\"label\":\"x\",\"steps\":[{\"kind\":\"custom\",\"action\":\"a\"},{\"kind\":\"custom\",\"action\":\"b\",\"wait\":\"exit\"}]}";

            var back = FromJson(old);

            Assert.All(back.Steps, s => Assert.Equal(0, s.Group));
        }

        private static string ToJson(StackDef stack)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(StackDef)).WriteObject(ms, stack);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static StackDef FromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (StackDef)new DataContractJsonSerializer(typeof(StackDef)).ReadObject(ms);
        }

        // ---- Batches / Validate ----

        [Fact]
        public void Batches_KhongNhom_MoiBuocMotDot()
        {
            Assert.Equal("0|1|2", Show(StackPlan.Batches(new[] { Step("a"), Step("b"), Step("c") })));
        }

        [Fact]
        public void Batches_MotNhomOGiua()
        {
            var steps = new[] { Step("a"), Step("b", 1), Step("c", 1), Step("d") };

            Assert.Equal("0|1,2|3", Show(StackPlan.Batches(steps)));
        }

        [Fact]
        public void Batches_HaiNhomLienTiepKhacSo()
        {
            var steps = new[] { Step("a", 1), Step("b", 1), Step("c", 2), Step("d", 2), Step("e", 2) };

            Assert.Equal("0,1|2,3,4", Show(StackPlan.Batches(steps)));
        }

        [Fact]
        public void Batches_NhomMotBuoc_LaTuanTu()
        {
            Assert.Equal("0|1", Show(StackPlan.Batches(new[] { Step("a", 3), Step("b") })));
        }

        [Fact]
        public void Batches_NhomBiTach_Loi()
        {
            var steps = new[] { Step("a", 1), Step("b"), Step("c", 1) };

            var ex = Assert.Throws<ArgumentException>(() => StackPlan.Batches(steps));

            Assert.Contains("nhóm 1 bị tách", ex.Message);
        }

        [Fact]
        public void Batches_RongKhongLoi()
        {
            Assert.Empty(StackPlan.Batches(new StackStep[0]));
        }

        [Fact]
        public void Validate_GroupAm_Loi()
        {
            var problem = StackPlan.Validate(new[] { Step("a"), Step("b", -1) });

            Assert.Contains(">= 0", problem);
            Assert.Contains("Bước 2", problem);
        }

        [Fact]
        public void Validate_HopLe_LaNull()
        {
            Assert.Null(StackPlan.Validate(new[] { Step("a", 1), Step("b", 1), Step("c"), Step("d", 2) }));
        }

        [Fact]
        public void Resolve_MangGroup()
        {
            var r = StackResolver.Resolve(Ws(Cmd("a", "echo a")), Step("a", 4), 0);

            Assert.Equal(4, r.Group);
        }

        // ---- WorkspaceEditor ----

        [Fact]
        public void CreateStack_NhomBiTach_BiTuChoi()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));

            var problem = WorkspaceEditor.CreateStack(ws, Stack(Step("a", 1), Step("b"), Step("a", 1)));

            Assert.Contains("nhóm 1 bị tách", problem);
            Assert.Empty(ws.Stacks);
        }

        [Fact]
        public void CreateStack_GroupAm_BiTuChoi()
        {
            var ws = Ws(Cmd("a", "echo a"));

            var problem = WorkspaceEditor.CreateStack(ws, Stack(Step("a", -2)));

            Assert.Contains(">= 0", problem);
        }

        [Fact]
        public void CreateStack_NhomHopLe_LuuDuoc()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));

            var problem = WorkspaceEditor.CreateStack(ws, Stack(Step("a", 1), Step("b", 1)));

            Assert.Null(problem);
            Assert.Single(ws.Stacks);
        }

        [Fact]
        public void UpdateStack_NhomBiTach_BiTuChoi_VaGiuNguyenBuocCu()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));
            Assert.Null(WorkspaceEditor.CreateStack(ws, Stack(Step("a"))));

            var problem = WorkspaceEditor.UpdateStack(ws, "s1", null, Stack(Step("a", 2), Step("b"), Step("a", 2)).Steps);

            Assert.Contains("bị tách", problem);
            Assert.Single(ws.Stacks[0].Steps);
        }

        // ---- Chạy thật ----

        [Fact]
        public void Run_HaiBuocCungNhom_CungChayVaBuocSauChoCaHai()
        {
            var gate = new ManualResetEventSlim(false);
            var bothRunningAtProbe = false;
            var ws = Ws(Cmd("a", LongRunning), Cmd("b", LongRunning), Cmd("c", "echo c"));
            // a: không chờ; b: chờ cổng, chỉ "mở" khi test cho phép => bước c phải chờ b.
            var runner = Runner(port =>
            {
                if (!gate.IsSet)
                    return false;
                bothRunningAtProbe = _host.Running().Count >= 2;
                return true;
            });

            var run = runner.Start(ws, Stack(Step("a", 1), Step("b", 1, WaitKind.Port, 4331), Step("c")));

            Eventually(() => run.Steps[0].Status == StepStatus.Done || run.Steps[0].Status == StepStatus.Running, "bước a bắt đầu");
            Eventually(() => _host.Running().Count == 2, "hai tác vụ cùng đang chạy");
            Assert.Equal(StepStatus.Running, run.Steps[1].Status);
            Assert.Equal(StepStatus.Pending, run.Steps[2].Status);
            Assert.Equal(RunState.Running, run.State);

            gate.Set();
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            Assert.True(bothRunningAtProbe);
            Assert.All(run.Steps, s => Assert.Equal(StepStatus.Done, s.Status));
            Assert.Equal(3, _host.Snapshot().Count);
        }

        [Fact]
        public void Run_BuocTrongNhomLoi_DungOBienNhom_KhongKillBuocCungNhom()
        {
            var ws = Ws(Cmd("a", LongRunning), Cmd("b", "exit /b 5"), Cmd("c", "echo c"));

            var run = Runner().Start(ws, Stack(Step("a", 1), Step("b", 1, WaitKind.Exit), Step("c")));
            Finished(run);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("bước 2", run.Message);
            Assert.Equal(StepStatus.Failed, run.Steps[1].Status);
            Assert.Equal(StepStatus.Pending, run.Steps[2].Status);
            Assert.Equal(2, _host.Snapshot().Count);
            Assert.Single(_host.Running());
        }

        [Fact]
        public void Run_DungKhiNhomDangCho_DungCaNhom()
        {
            var ws = Ws(Cmd("a", LongRunning), Cmd("b", LongRunning), Cmd("c", "echo c"));

            var run = Runner(port => false).Start(ws, Stack(Step("a", 1, WaitKind.Port, 4332), Step("b", 1, WaitKind.Port, 4333), Step("c")));
            Eventually(() => _host.Running().Count == 2, "hai tác vụ cùng đang chạy");

            run.Stop();
            Finished(run);

            Assert.Equal(RunState.Cancelled, run.State);
            Eventually(() => _host.Running().Count == 0, "cả nhóm đã dừng");
            Assert.Equal(StepStatus.Pending, run.Steps[2].Status);
        }

        [Fact]
        public void Run_NhomBiTach_BaoLoiTruocKhiBatGi()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));
            var stack = Stack(Step("a", 1), Step("b"), Step("a", 1));

            var run = Runner().Start(ws, stack);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("bị tách", run.Message);
            Assert.Empty(_host.Snapshot());
        }
    }
}
