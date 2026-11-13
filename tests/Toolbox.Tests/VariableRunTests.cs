using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Chạy với nhóm biến: biến vào môi trường process, secret bị che trong output và log, thiếu secret thì bước hỏng (D5).</summary>
    [Collection("ChayTienTrinh")] // các test bật tiến trình thật chạy lần lượt, không tranh CPU nhau
    public class VariableRunTests : IDisposable
    {
        private const string Secret = "hunter2-Bi-Mat";

        private readonly TempDir _dir = new TempDir();
        private readonly TaskHost _host;

        public VariableRunTests()
        {
            _host = new TaskHost(_dir.Combine("runs"));
        }

        public void Dispose()
        {
            _host.StopAll();
            SpinWait.SpinUntil(() => _host.Running().Count == 0, TimeSpan.FromSeconds(10));
            _dir.Dispose();
        }

        private static void WaitFinished(TaskInfo task) =>
            Assert.True(SpinWait.SpinUntil(() => !task.IsRunning, TimeSpan.FromSeconds(15)), "tác vụ không kết thúc");

        /// <summary>Workspace có lệnh riêng c1 và profile p1 (loại custom) trỏ tới c1, gắn nhóm <paramref name="groupId"/>.</summary>
        private Workspace Ws(string commandLine, string groupId, VariableGroup group = null)
        {
            var ws = new Workspace { Id = "w1", Name = "Thử", Path = _dir.Path };
            ws.Customs.Add(new CustomCommand { Id = "c1", Label = "lệnh c1", CommandLine = commandLine });
            ws.Profiles.Add(new Profile { Id = "p1", Label = "FE", Kind = ProfileKind.Custom, RefId = "c1", VariableGroupId = groupId });
            if (group != null)
                ws.VariableGroups.Add(group);
            return ws;
        }

        private static VariableGroup Group(params GroupVariable[] variables)
        {
            var group = new VariableGroup { Id = "g1", Label = "Dev-A" };
            group.Variables.AddRange(variables);
            return group;
        }

        private static GroupVariable SecretVar(string name, string plain)
        {
            var v = new GroupVariable { Name = name };
            VariableGroups.SetSecret(v, plain);
            return v;
        }

        private static ResolvedStep ResolveProfile(Workspace ws) =>
            StackResolver.Resolve(ws, new StackStep { Kind = ProfileKind.Custom, ProfileId = "p1" }, 0);

        [Fact]
        public void Profile_GanNhom_EchoRaGiaTriBien()
        {
            var ws = Ws("echo %FOO%", "g1", Group(new GroupVariable { Name = "FOO", Value = "bar" }));

            var resolved = ResolveProfile(ws);
            Assert.False(resolved.IsBroken, resolved.Problem);
            Assert.Equal("bar", resolved.Spec.Env["foo"]);

            var task = _host.Start(resolved.Spec);
            WaitFinished(task);

            Assert.Equal(TaskState.Done, task.State);
            Assert.Contains("bar", task.Lines());
        }

        [Fact]
        public void Profile_BienNhomGhiDeBienKeThua()
        {
            // PATHEXT luôn có trong môi trường Toolbox; nhóm ghi đè thì process thấy giá trị của nhóm.
            var ws = Ws("echo [%PATHEXT%]", "g1", Group(new GroupVariable { Name = "PATHEXT", Value = "ghi-de" }));

            var task = _host.Start(ResolveProfile(ws).Spec);
            WaitFinished(task);

            Assert.Contains("[ghi-de]", task.Lines());
        }

        [Fact]
        public void Secret_TrongOutput_CheOLinesVaFileLog()
        {
            var ws = Ws("(echo token=%TOK%)& (echo %TOK%)1>&2& <nul set /p=cuoi-%TOK%", "g1", Group(SecretVar("TOK", Secret)));

            var resolved = ResolveProfile(ws);
            Assert.False(resolved.IsBroken, resolved.Problem);
            var task = _host.Start(resolved.Spec);
            WaitFinished(task);

            var lines = task.Lines();
            Assert.Contains("token=***", lines);
            Assert.DoesNotContain(lines, l => l.Contains(Secret));
            // stderr và dòng cuối không có newline cũng bị che
            Assert.Contains(lines, l => l.Trim() == VariableGroups.Mask);
            Assert.Contains(lines, l => l.Contains("cuoi-***"));

            var log = File.ReadAllText(task.LogPath);
            Assert.Contains("token=***", log);
            Assert.DoesNotContain(Secret, log);
        }

        [Fact]
        public void Secret_ChuaCoGiaTri_BuocHongDungThongBaoD5()
        {
            var ws = Ws("echo %TOK%", "g1", Group(new GroupVariable { Name = "TOK", Secret = true }));

            var resolved = ResolveProfile(ws);

            Assert.True(resolved.IsBroken);
            Assert.Null(resolved.Spec);
            Assert.Equal("Nhóm biến Dev-A chưa có giá trị cho TOK, nhập ở tab Biến", resolved.Problem);
        }

        [Fact]
        public void Secret_ChuaCoGiaTri_BoChayBaoBuocHong()
        {
            var ws = Ws("echo %TOK%", "g1", Group(new GroupVariable { Name = "TOK", Secret = true }));
            var stack = new StackDef { Id = "s1", Label = "Bộ thử", Steps = { new StackStep { Kind = ProfileKind.Custom, ProfileId = "p1" } } };

            var plan = StackResolver.ResolveAll(ws, stack);

            Assert.True(plan.Single().IsBroken);
            Assert.Contains("chưa có giá trị cho TOK", plan.Single().Problem);
        }

        [Fact]
        public void NhomKhongCon_BuocHongCoThongBaoRo()
        {
            var ws = Ws("echo x", "khong-co");

            var resolved = ResolveProfile(ws);

            Assert.True(resolved.IsBroken);
            Assert.Contains("khong-co", resolved.Problem);
        }

        [Fact]
        public void Profile_KhongGanNhom_ChayNhuTruoc()
        {
            var ws = Ws("echo xin chao", null);

            var resolved = ResolveProfile(ws);
            Assert.False(resolved.IsBroken, resolved.Problem);
            Assert.Null(resolved.Spec.Env);
            Assert.Null(resolved.Spec.Secrets);

            var task = _host.Start(resolved.Spec);
            WaitFinished(task);

            Assert.Equal(TaskState.Done, task.State);
            Assert.Contains("xin chao", task.Lines());
        }

        [Fact]
        public void ProfileTool_GanNhom_CoEnv()
        {
            var ws = Ws("echo x", null, Group(new GroupVariable { Name = "FOO", Value = "bar" }));
            ws.Bindings.Add(new ToolBinding { ToolId = ToolCatalog.DotNet });
            ws.Profiles.Add(new Profile { Id = "p2", Label = "Test", Kind = ToolCatalog.DotNet, ActionId = "test", VariantId = "default", VariableGroupId = "g1" });

            var r = StackResolver.Resolve(ws, new StackStep { Kind = ToolCatalog.DotNet, ActionId = "test", ProfileId = "p2" }, 0);

            Assert.False(r.IsBroken, r.Problem);
            Assert.Equal("bar", r.Spec.Env["FOO"]);
        }

        [Fact]
        public void ResolveCustom_NhanGroupId()
        {
            var ws = Ws("echo %FOO%", null, Group(new GroupVariable { Name = "FOO", Value = "bar" }));

            var with = StackResolver.ResolveCustom(ws, new StackStep { Kind = TaskKey.Custom, ActionId = "c1" }, "g1");
            var without = StackResolver.ResolveCustom(ws, new StackStep { Kind = TaskKey.Custom, ActionId = "c1" });

            Assert.False(with.IsBroken, with.Problem);
            Assert.Equal("bar", with.Spec.Env["FOO"]);
            Assert.Null(without.Spec.Env);
        }

        [Fact]
        public void ResolveAdHoc_GroupIdKhongCon_LaHong()
        {
            var ws = Ws("echo x", null);
            ws.Bindings.Add(new ToolBinding { ToolId = ToolCatalog.DotNet });

            var vals = new Dictionary<string, string> { ["args"] = "--no-build" };
            var ok = StackResolver.ResolveAdHoc(ws, ToolCatalog.DotNet, "test", "default", vals);
            var broken = StackResolver.ResolveAdHoc(ws, ToolCatalog.DotNet, "test", "default", vals, "khong-co");

            Assert.False(ok.IsBroken, ok.Problem);
            Assert.Null(ok.Spec.Env);
            Assert.True(broken.IsBroken);
            Assert.Contains("khong-co", broken.Problem);
            Assert.Null(broken.Spec);
        }

        [Fact]
        public void Clone_ChepRiengEnvVaSecrets()
        {
            var spec = new TaskSpec
            {
                CommandLine = "echo x",
                Env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FOO"] = "bar" },
                Secrets = new List<string> { Secret },
            };

            var copy = spec.Clone();
            spec.Env["FOO"] = "doi";
            spec.Secrets.Clear();

            Assert.Equal("bar", copy.Env["FOO"]);
            Assert.Equal(new[] { Secret }, copy.Secrets);
        }

        [Fact]
        public void Restart_GiuBienVaVanChe()
        {
            var ws = Ws("echo %TOK%", "g1", Group(SecretVar("TOK", Secret)));
            var first = _host.Start(ResolveProfile(ws).Spec);
            WaitFinished(first);

            var again = _host.Restart(first.Id);
            WaitFinished(again);

            Assert.Contains(VariableGroups.Mask, again.Lines());
            Assert.DoesNotContain(again.Lines(), l => l.Contains(Secret));
        }
    }
}
