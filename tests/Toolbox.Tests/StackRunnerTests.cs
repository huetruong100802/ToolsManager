using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class StackRunnerTests : IDisposable
    {
        private readonly TempDir _dir = new TempDir();
        private readonly TaskHost _host;

        public StackRunnerTests()
        {
            _host = new TaskHost(_dir.Combine("runs"));
        }

        public void Dispose()
        {
            _host.StopAll();
            // Chờ tiến trình thoát hẳn để xoá được thư mục.
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

        private static StackStep Step(string customId, string wait = null, int port = 0) =>
            new StackStep { Kind = TaskKey.Custom, ActionId = customId, Wait = wait, Port = port };

        private static StackDef Stack(params StackStep[] steps) =>
            new StackDef { Id = "s1", Label = "Bộ thử", Steps = steps.ToList() };

        private StackRunner Runner(Func<int, bool> probe = null) =>
            new StackRunner(_host, probe) { PollInterval = TimeSpan.FromMilliseconds(30), PortTimeout = TimeSpan.FromSeconds(5) };

        private static void Finished(StackRun run) =>
            Assert.True(run.Completion.Wait(TimeSpan.FromSeconds(20)), "bộ chạy không kết thúc");

        private const string LongRunning = "ping -n 60 127.0.0.1 >nul";

        // ---- Resolve ----

        [Fact]
        public void Resolve_LenhRiengDaXoa_LaHong()
        {
            var r = StackResolver.Resolve(Ws(), Step("khong-co"), 0);

            Assert.True(r.IsBroken);
            Assert.Contains("đã bị xoá", r.Problem);
            Assert.Null(r.Spec);
        }

        [Fact]
        public void Resolve_ToolChuaGan_LaHong()
        {
            var step = new StackStep { Kind = ToolCatalog.DotNet, ActionId = "build" };

            var r = StackResolver.Resolve(Ws(), step, 0);

            Assert.True(r.IsBroken);
            Assert.Contains("đã bị gỡ", r.Problem);
        }

        [Fact]
        public void Resolve_ToolKhongCoTrongDanhMuc_LaHong()
        {
            var r = StackResolver.Resolve(Ws(), new StackStep { Kind = "vb6", ActionId = "x" }, 0);

            Assert.True(r.IsBroken);
            Assert.Contains("danh mục", r.Problem);
        }

        [Fact]
        public void Resolve_ProfileDaXoa_LaHong()
        {
            var ws = Ws();
            ws.Bindings.Add(new ToolBinding { Kind = ToolCatalog.DotNet });
            var action = ToolCatalog.Find(ToolCatalog.DotNet).Actions[0];

            var r = StackResolver.Resolve(ws, new StackStep { Kind = ToolCatalog.DotNet, ActionId = action.Id, ProfileId = "ma" }, 0);

            Assert.True(r.IsBroken);
            Assert.Contains("Profile", r.Problem);
        }

        [Fact]
        public void Resolve_KieuChoLa_LaHong()
        {
            var r = StackResolver.Resolve(Ws(Cmd("a", "echo a")), Step("a", wait: "mai"), 0);

            Assert.True(r.IsBroken);
        }

        [Fact]
        public void Resolve_ChoCongNhungKhongCoCong_LaHong()
        {
            var r = StackResolver.Resolve(Ws(Cmd("a", "echo a")), Step("a", WaitKind.Port), 0);

            Assert.True(r.IsBroken);
            Assert.Contains("cổng", r.Problem);
        }

        [Fact]
        public void Resolve_LenhRieng_KhoaVaCongDungQuyUoc()
        {
            var ws = Ws(Cmd("a", "echo a"));

            var r = StackResolver.Resolve(ws, Step("a", WaitKind.Port, 4300), 0);

            Assert.False(r.IsBroken);
            Assert.Equal(TaskKey.ForCustom("w1", "a"), r.Spec.Key);
            Assert.Equal(4300, r.Spec.Port);
            Assert.Equal(_dir.Path, r.Spec.Cwd);
        }

        [Fact]
        public void Resolve_HanhDong_DungKhoaVaNhan()
        {
            var ws = Ws();
            ws.Bindings.Add(new ToolBinding { Kind = ToolCatalog.DotNet });
            var tool = ToolCatalog.Find(ToolCatalog.DotNet);
            var action = tool.Actions[0];

            var r = StackResolver.Resolve(ws, new StackStep { Kind = tool.Kind, ActionId = action.Id }, 0);

            Assert.False(r.IsBroken);
            Assert.Equal(TaskKey.ForAction("w1", tool.Kind, action.Id, action.Default.Id), r.Spec.Key);
            Assert.StartsWith(tool.Name + " · " + action.Label, r.Spec.Label);
        }

        [Fact]
        public void Resolve_Profile_DungBienTheVaThamSoCuaProfile()
        {
            var ws = Ws();
            ws.Bindings.Add(new ToolBinding { Kind = ToolCatalog.DotNet });
            var profile = new Profile
            {
                Id = "p1", Label = "API 5099", Kind = ToolCatalog.DotNet, ActionId = "run", VariantId = "port",
                Vals = { ["port"] = "5099" },
            };
            ws.Profiles.Add(profile);

            var r = StackResolver.Resolve(ws, new StackStep { Kind = ToolCatalog.DotNet, ActionId = "run", ProfileId = "p1" }, 0);

            Assert.False(r.IsBroken);
            Assert.Equal("dotnet run --urls http://localhost:5099", r.Spec.CommandLine);
            Assert.Equal(5099, r.Spec.Port);
            Assert.Equal("API 5099", r.Spec.Label);
            Assert.Equal(TaskKey.ForProfile("w1", profile), r.Spec.Key);
        }

        // ---- Chạy ----

        [Fact]
        public void Run_KhongCho_BatTatCaCacBuocRoiBao()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));

            var run = Runner().Start(ws, Stack(Step("a"), Step("b")));
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            Assert.All(run.Steps, s => Assert.Equal(StepStatus.Done, s.Status));
            Assert.Equal(2, run.StartedTaskIds.Count);
        }

        [Fact]
        public void Run_ChoThoat_BuocSauChiChayKhiBuocTruocXongMaKhong()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));

            var run = Runner().Start(ws, Stack(Step("a", WaitKind.Exit), Step("b")));
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            var tasks = run.StartedTaskIds.Select(_host.Find).ToList();
            Assert.False(tasks[0].IsRunning);
            Assert.True(tasks[0].EndedAt <= tasks[1].StartedAt, "bước b phải bắt đầu sau khi a kết thúc");
        }

        [Fact]
        public void Run_BuocThoatMaLoi_DungVaBaoBuocNao()
        {
            var ws = Ws(Cmd("a", "exit /b 3"), Cmd("b", "echo b"));

            var run = Runner().Start(ws, Stack(Step("a", WaitKind.Exit), Step("b")));
            Finished(run);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("bước 1", run.Message);
            Assert.Contains("mã 3", run.Message);
            Assert.Equal(StepStatus.Failed, run.Steps[0].Status);
            Assert.Equal(StepStatus.Pending, run.Steps[1].Status);
            Assert.Single(_host.Snapshot());
        }

        [Fact]
        public void Run_ChoCong_DiTiepKhiCongMo()
        {
            var calls = 0;
            var ws = Ws(Cmd("a", LongRunning), Cmd("b", "echo b"));

            var run = Runner(port => Interlocked.Increment(ref calls) >= 3).Start(ws, Stack(Step("a", WaitKind.Port, 4310), Step("b")));
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            Assert.True(calls >= 3);
            Assert.Equal(2, _host.Snapshot().Count);
        }

        [Fact]
        public void Run_ChoCongThatSu_ThayCongDangNghe()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var ws = Ws(Cmd("a", LongRunning));

                var run = Runner().Start(ws, Stack(Step("a", WaitKind.Port, port)));
                Finished(run);

                Assert.Equal(RunState.Succeeded, run.State);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void Run_ChoCong_HetGio_BaoLoi()
        {
            var ws = Ws(Cmd("a", LongRunning));
            var runner = Runner(port => false);
            runner.PortTimeout = TimeSpan.FromMilliseconds(300);

            var run = runner.Start(ws, Stack(Step("a", WaitKind.Port, 4311)));
            Finished(run);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("chờ cổng 4311", run.Message);
        }

        [Fact]
        public void Run_ChoCong_TienTrinhThoatSom_BaoLoi()
        {
            var ws = Ws(Cmd("a", "exit /b 5"));

            var run = Runner(port => false).Start(ws, Stack(Step("a", WaitKind.Port, 4312)));
            Finished(run);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("mã 5", run.Message);
        }

        [Fact]
        public void Run_TacVuCungKhoaDangChay_DungLaiKhongBatLanHai()
        {
            var ws = Ws(Cmd("a", LongRunning));
            var existing = _host.Start(new TaskSpec
            {
                WorkspaceId = "w1", Label = "a", CommandLine = LongRunning, Cwd = _dir.Path, Port = 4313,
                Key = TaskKey.ForCustom("w1", "a"),
            });

            var run = Runner(port => true).Start(ws, Stack(Step("a", WaitKind.Port, 4313)));
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            Assert.Empty(run.StartedTaskIds);
            Assert.True(run.Steps[0].Reused);
            Assert.Single(_host.Snapshot());
            Assert.True(existing.IsRunning);
        }

        [Fact]
        public void Run_HaiBuocCungKhoa_BuocSauDungLaiBuocTruoc()
        {
            var ws = Ws(Cmd("a", LongRunning));

            var run = Runner().Start(ws, Stack(Step("a", port: 4314), Step("a", port: 4314)));
            Finished(run);

            Assert.Equal(RunState.Succeeded, run.State);
            Assert.Single(run.StartedTaskIds);
        }

        // ---- Kiểm trước ----

        [Fact]
        public void Run_CoBuocHong_KhongBatTienTrinhNaoVaBaoRo()
        {
            var ws = Ws(Cmd("a", "echo a"));

            var run = Runner().Start(ws, Stack(Step("a"), Step("da-xoa")));
            Finished(run);

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("Bước 2", run.Message);
            Assert.Contains("hỏng", run.Message);
            Assert.Equal(StepStatus.Failed, run.Steps[1].Status);
            Assert.Empty(_host.Snapshot());
        }

        [Fact]
        public void Run_BoChayRong_BaoLoi()
        {
            var run = Runner().Start(Ws(), Stack());

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("chưa có bước", run.Message);
        }

        [Fact]
        public void Run_ThuMucWorkspaceMat_BaoLoiTruocKhiChay()
        {
            var ws = Ws(Cmd("a", "echo a"));
            ws.Path = _dir.Combine("khong-ton-tai");

            var run = Runner().Start(ws, Stack(Step("a")));

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("thư mục workspace", run.Message);
            Assert.Empty(_host.Snapshot());
        }

        [Fact]
        public void Run_ThuMucChayMat_BaoLoiTruocKhiChay()
        {
            var command = Cmd("a", "echo a");
            command.Cwd = "khong-co";

            var run = Runner().Start(Ws(command), Stack(Step("a")));

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("thư mục chạy", run.Message);
            Assert.Empty(_host.Snapshot());
        }

        [Fact]
        public void Run_CongDangBiTacVuKhacGiu_BaoLoiTruocKhiChay()
        {
            _host.Start(new TaskSpec
            {
                WorkspaceId = "w1", Label = "khác", CommandLine = LongRunning, Cwd = _dir.Path, Port = 4315, Key = "w1|x|y|z|",
            });
            var ws = Ws(Cmd("a", "echo a"));

            var run = Runner().Start(ws, Stack(Step("a", WaitKind.None, 4315)));

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("4315", run.Message);
            Assert.Single(_host.Snapshot());
        }

        [Fact]
        public void Run_HaiBuocTrungCong_BaoLoiTruocKhiChay()
        {
            var ws = Ws(Cmd("a", "echo a"), Cmd("b", "echo b"));

            var run = Runner().Start(ws, Stack(Step("a", port: 4316), Step("b", port: 4316)));

            Assert.Equal(RunState.Failed, run.State);
            Assert.Contains("trùng cổng 4316", run.Message);
            Assert.Empty(_host.Snapshot());
        }

        // ---- Dừng ----

        [Fact]
        public void Stop_DangChoCong_ThoiChoVaDungTacVuDaBat()
        {
            var ws = Ws(Cmd("a", LongRunning), Cmd("b", "echo b"));
            var run = Runner(port => false).Start(ws, Stack(Step("a", WaitKind.Port, 4317), Step("b")));
            Assert.True(SpinWait.SpinUntil(() => run.StartedTaskIds.Count == 1, TimeSpan.FromSeconds(5)));

            run.Stop();
            Finished(run);

            Assert.Equal(RunState.Cancelled, run.State);
            var task = _host.Find(run.StartedTaskIds[0]);
            Assert.True(SpinWait.SpinUntil(() => !task.IsRunning, TimeSpan.FromSeconds(10)), "tác vụ phải bị dừng");
            Assert.Single(_host.Snapshot());
        }
    }
}
