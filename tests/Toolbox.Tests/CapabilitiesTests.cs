using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Bảng khả năng giả cho test: một cờ có vai trò cổng cho một action, không đọc file thật.</summary>
    public static class CapabilityFixtures
    {
        public static ToolCapabilities Port(string kind, string action, string flag) => new ToolCapabilities
        {
            Kind = kind,
            Entries = { new CapabilityEntry { Action = action, Name = flag, TakesValue = true, Source = "scan", Role = "port" } },
        };

        /// <summary>Action của một lệnh con, dựng từ bảng khả năng cho trước (không đọc đĩa).</summary>
        public static ActionDef Sub(string kind, string subcommand, ToolCapabilities caps = null) =>
            ToolCatalog.FromSubcommand(ToolCatalog.Find(kind), caps ?? new ToolCapabilities { Kind = kind }, subcommand);

        public static ActionDef Angular(string subcommand, ToolCapabilities caps) => Sub("angular", subcommand, caps);
    }

    public class CapabilitiesTests
    {
        private const string Help = @"Usage: ng serve [options]

Options:
  --port number       Port to listen on. (Default: 4200)
  --open              Opens the url in default browser.
  -h, --help          Shows a help message for this command in the console.
  --host
        Host to listen on.
";

        [Fact]
        public void HelpParser_DocCoBaCo_TenMoTaVaGiaTri()
        {
            var entries = HelpParser.Parse(Help);

            Assert.Equal(new[] { "--port", "--open", "--help", "--host" }, entries.Select(e => e.Name).ToArray());
            Assert.True(entries[0].TakesValue);
            Assert.Equal("Port to listen on. (Default: 4200)", entries[0].Description);
            Assert.False(entries[1].TakesValue);
            Assert.Equal("Opens the url in default browser.", entries[1].Description);
            Assert.Equal("Host to listen on.", entries[3].Description);
            Assert.Contains("--port number", entries[0].Excerpt);
        }

        [Fact]
        public void HelpParser_KhongCoCo_RongKhongBiaRa()
        {
            Assert.Empty(HelpParser.Parse("Usage: tool <lệnh>\nKhông có tuỳ chọn nào."));
        }

        // git --help: cờ nằm trong dòng "usage:" với ngoặc vuông, không đứng đầu dòng.
        private const string GitUsage = @"usage: git [-v | --version] [-h | --help] [-C <path>] [-c <name>=<value>]
           [--exec-path[=<path>]] [--html-path] [--man-path] [--info-path]
           [-p | --paginate | -P | --no-pager] [--no-replace-objects] [--bare]
           [--git-dir=<path>] [--work-tree=<path>]
           <command> [<args>]

These are common Git commands used in various situations:
";

        [Fact]
        public void HelpParser_UsageKieuGit_DocCoCoTrongNgoac()
        {
            var entries = HelpParser.Parse(GitUsage);
            var byName = entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

            Assert.Contains("-v", byName.Keys);
            Assert.Contains("--version", byName.Keys);
            Assert.Contains("--no-pager", byName.Keys);
            Assert.True(byName["-C"].TakesValue);
            Assert.True(byName["-c"].TakesValue);
            Assert.True(byName["--exec-path"].TakesValue);
            Assert.True(byName["--git-dir"].TakesValue);
            Assert.False(byName["--bare"].TakesValue);
            Assert.False(byName["--no-pager"].TakesValue);
            Assert.False(byName["-v"].TakesValue);
            Assert.Null(byName["--bare"].Description);
        }

        [Fact]
        public void HelpParser_UsageArgparse_NhanGiaTriVietHoa()
        {
            var entries = HelpParser.Parse("usage: tool [-h] [--port PORT] [--open]\n\noptions:\n  -h, --help  show help\n");
            var byName = entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

            Assert.True(byName["--port"].TakesValue);
            Assert.False(byName["--open"].TakesValue);
            Assert.Single(entries, e => e.Name == "--help");
        }

        [Fact]
        public void ApplyScan_GiuVaiTroVaMucNhapTay()
        {
            var data = new ToolCapabilities { Kind = "x" };
            CapabilityStore.ApplyScan(data, "serve", Outcome("--port", "--old"), DateTime.Now);
            CapabilityStore.SetRole(data, "serve", "--port", CapabilityStore.PortRole);
            CapabilityStore.AddManual(data, "serve", "--custom", false, "tự nhập");

            CapabilityStore.ApplyScan(data, "serve", Outcome("--port", "--host"), DateTime.Now);

            var names = data.Entries.Where(e => e.Action == "serve").Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "--custom", "--host", "--port" }, names);
            Assert.Equal("port", data.Entries.First(e => e.Name == "--port").Role);
            Assert.Equal("manual", data.Entries.First(e => e.Name == "--custom").Source);
            Assert.Single(data.Scans);
        }

        [Fact]
        public void ApplyScan_LoiThiGiuMucCu()
        {
            var data = new ToolCapabilities { Kind = "x" };
            CapabilityStore.ApplyScan(data, "serve", Outcome("--port"), DateTime.Now);

            CapabilityStore.ApplyScan(data, "serve", new ScanOutcome { Command = "tool --help", Error = "Quá thời gian" }, DateTime.Now);

            Assert.Equal("Quá thời gian", data.Scans.Single().Error);
            Assert.Equal(new[] { "--port" }, data.Entries.Select(e => e.Name).ToArray());
        }

        [Fact]
        public void SetRole_ChiMotCoCHoTungAction()
        {
            var data = new ToolCapabilities { Kind = "x" };
            CapabilityStore.ApplyScan(data, "serve", Outcome("--port", "--p2"), DateTime.Now);

            CapabilityStore.SetRole(data, "serve", "--port", "port");
            CapabilityStore.SetRole(data, "serve", "--p2", "port");

            Assert.Equal("", data.Entries.First(e => e.Name == "--port").Role);
            Assert.Equal("port", data.Entries.First(e => e.Name == "--p2").Role);
        }

        [Theory]
        [InlineData("port; rm")]
        [InlineData("--")]
        public void AddManual_TenCoSai_BiTuChoi(string name)
        {
            var data = new ToolCapabilities { Kind = "x" };
            Assert.Throws<ArgumentException>(() => CapabilityStore.AddManual(data, "serve", name, true, null));
        }

        [Fact]
        public void AddManual_TrungTen_BiTuChoi()
        {
            var data = new ToolCapabilities { Kind = "x" };
            CapabilityStore.AddManual(data, "serve", "--port", true, null);

            Assert.Throws<ArgumentException>(() => CapabilityStore.AddManual(data, "serve", "--port", true, null));
        }

        [Fact]
        public void FromSubcommand_ChuaGanCoCong_KhongCoThamSoCong()
        {
            var action = CapabilityFixtures.Angular("serve", new ToolCapabilities { Kind = "angular" });

            Assert.DoesNotContain(action.Default.Params, p => p.IsPort);
            Assert.Equal("npx ng serve", CommandBuilder.Build(action.Default));
        }

        [Fact]
        public void FromSubcommand_DaGanCo_DungCoDoBang()
        {
            var action = CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "serve", "--port"));

            Assert.Equal("npx ng serve --port 4200 --open",
                CommandBuilder.Build(action.Default, new Dictionary<string, string> { ["port"] = "4200", ["args"] = "--open" }));
        }

        [Fact]
        public void FromSubcommand_CoGanChoLenhConKhac_KhongAnhHuong()
        {
            var action = CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "build", "--port"));

            Assert.DoesNotContain(action.Default.Params, p => p.IsPort);
        }

        [Fact]
        public void FromSubcommand_CongDeTrong_PhaiNhapTruocKhiChay()
        {
            var action = CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "serve", "--port"));

            Assert.Contains("port", CommandBuilder.Validate(action.Default, new Dictionary<string, string>()).Keys);
            Assert.Throws<ArgumentException>(() => CommandBuilder.Build(action.Default));
        }

        [Fact]
        public void Subcommands_ChiLayMucCuaTool_KhongLayCo()
        {
            var data = new ToolCapabilities { Kind = "x" };
            CapabilityStore.AddManual(data, "", "serve", false, null);
            CapabilityStore.AddManual(data, "", "build", false, null);
            CapabilityStore.AddManual(data, "serve", "--port", true, null);
            CapabilityStore.AddManual(data, "", "--version", false, null);

            Assert.Equal(new[] { "build", "serve" }, CapabilityStore.Subcommands(data).Select(e => e.Name).ToArray());
        }

        [Fact]
        public void ActionsFor_LayLenhConTuBangKhaNang()
        {
            var workspace = new Workspace { Name = "w", Path = Path.GetTempPath() };

            var actions = ToolCatalog.ActionsFor(workspace, new ToolBinding { ToolId = "git" });

            Assert.Contains(TestData.GitAction, actions.Select(a => a.Id));
            Assert.Equal(TestData.GitAction, actions.First(a => a.Id == TestData.GitAction).Label);
            Assert.StartsWith("git " + TestData.GitAction, actions.First(a => a.Id == TestData.GitAction).Default.Template);
        }

        [Fact]
        public void ActionsFor_KindKhongCoTrongDanhMuc_KhongCoAction()
        {
            var workspace = new Workspace { Name = "w", Path = Path.GetTempPath() };

            Assert.Empty(ToolCatalog.ActionsFor(workspace, new ToolBinding { ToolId = "chua-co-bang" }));
        }

        [Fact]
        public void Store_LuuVaDocLai()
        {
            using (var dir = new TempDir())
            {
                var data = CapabilityFixtures.Port("angular", "serve", "--port");
                CapabilityStore.AddManual(data, "serve", "--open", false, "Mở trình duyệt");

                CapabilityStore.Save(dir.Path, data);
                var back = CapabilityStore.Load(dir.Path, "angular");

                Assert.Equal(new[] { "--port", "--open" }, back.Entries.Select(e => e.Name).ToArray());
                Assert.Equal("port", back.Entries[0].Role);
                Assert.Equal("Mở trình duyệt", back.Entries[1].Description);
            }
        }

        [Fact]
        public void Store_FileHong_KhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("angular.json", "{ hỏng");

                Assert.Throws<InvalidDataException>(() => CapabilityStore.Load(dir.Path, "angular"));
            }
        }

        [Fact]
        public void Store_ChuaCoFile_TraBangRong()
        {
            using (var dir = new TempDir())
            {
                var data = CapabilityStore.Load(dir.Path, "angular");

                Assert.Empty(data.Entries);
                Assert.Equal("angular", data.Kind);
            }
        }

        [Fact]
        public void Scanner_DocCoTuLenhThat()
        {
            using (var dir = new TempDir())
            {
                var outcome = CapabilityScanner.Run("echo --port 4200", dir.Path);

                Assert.Null(outcome.Error);
                Assert.NotEmpty(outcome.Fingerprint);
                var port = Assert.Single(outcome.Entries);
                Assert.Equal("--port", port.Name);
                Assert.True(port.TakesValue);
            }
        }

        [Fact]
        public void Scanner_ThuMucKhongCo_BaoLoi()
        {
            var outcome = CapabilityScanner.Run("echo x", Path.Combine(Path.GetTempPath(), "khong-co-" + Guid.NewGuid().ToString("N")));

            Assert.NotNull(outcome.Error);
        }

        private static ScanOutcome Outcome(params string[] flags) => new ScanOutcome
        {
            Command = "tool --help",
            Fingerprint = "abc",
            Entries = flags.Select(f => new CapabilityEntry { Name = f, Source = "scan", TakesValue = true, Excerpt = f }).ToList(),
        };
    }
}
