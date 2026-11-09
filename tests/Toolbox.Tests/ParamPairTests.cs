using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class ParamPairTests
    {
        [Fact]
        public void Render_DotnetRun_FileLapBaLan_DauTachVaDuongDanCoDauCach()
        {
            var pairs = new List<ParamPair>
            {
                new ParamPair("--project", @"Tools\InventoryControlExcelImport\InventoryControlExcelImport.csproj"),
                new ParamPair("--"),
                new ParamPair("--file", @"Tools\Kho TP 1.xlsx"),
                new ParamPair("--file", @"Tools\Kho TP 2.1.xlsx"),
                new ParamPair("--file", @"Tools\Kho TP 2.2.xlsx"),
                new ParamPair("--warehouse", "3"),
                new ParamPair("--dry-run"),
            };

            Assert.Null(PairLine.Check(pairs));
            Assert.Equal(
                @"--project Tools\InventoryControlExcelImport\InventoryControlExcelImport.csproj -- "
                + "--file \"Tools\\Kho TP 1.xlsx\" --file \"Tools\\Kho TP 2.1.xlsx\" --file \"Tools\\Kho TP 2.2.xlsx\" --warehouse 3 --dry-run",
                PairLine.Render(pairs));
        }

        [Fact]
        public void Render_DotnetNuget_DoiSoViTriVaFlag()
        {
            var pairs = new List<ParamPair> { new ParamPair("", "all"), new ParamPair("--clear") };
            Assert.Equal("all --clear", PairLine.Render(pairs));
        }

        [Fact]
        public void Render_Ef_TenMigrationHoiLucChay_BoQuaKhiChuaCoGiaTri()
        {
            var pairs = new List<ParamPair>
            {
                new ParamPair("", "", ask: true),
                new ParamPair("--project", "Presentation/Nois.Api"),
                new ParamPair("--context", "NoisObjectContext"),
            };

            Assert.Single(PairLine.Pending(pairs));
            Assert.Equal("--project Presentation/Nois.Api --context NoisObjectContext", PairLine.Render(pairs));

            var resolved = PairLine.Resolve(pairs, new[] { "AddIdentityKey" });
            Assert.Empty(PairLine.Pending(resolved));
            Assert.Equal("AddIdentityKey --project Presentation/Nois.Api --context NoisObjectContext", PairLine.Render(resolved));
            // Bản gốc không bị ghi đè.
            Assert.Single(PairLine.Pending(pairs));
        }

        [Fact]
        public void Check_KeySaiVaGiaTriCoKyTuCam_BiTuChoi()
        {
            Assert.NotNull(PairLine.Check(new[] { new ParamPair("--ok;calc", "x") }));
            Assert.NotNull(PairLine.Check(new[] { new ParamPair("--ok", "a\"b") }));
            Assert.NotNull(PairLine.Check(new[] { new ParamPair("--ok", "%PATH%") }));
            Assert.Null(PairLine.Check(new[] { new ParamPair("--ok", "a & b") }));
        }

        [Fact]
        public void Parse_DanCaDong_DungTheoTakesValue()
        {
            var known = new HashSet<string> { "--project", "--file", "--warehouse" };
            var pairs = PairLine.Parse(
                "--project \"A B\\x.csproj\" -- --file \"Kho 1.xlsx\" --file k2.xlsx --warehouse 3 --dry-run",
                known.Contains);

            Assert.Equal(
                new[] { "--project=A B\\x.csproj", "--=", "--file=Kho 1.xlsx", "--file=k2.xlsx", "--warehouse=3", "--dry-run=" },
                pairs.Select(p => p.Key + "=" + p.Value).ToArray());
            // Quay vòng: dựng lại rồi tách lại cho cùng kết quả.
            Assert.Equal(PairLine.Render(pairs), PairLine.Render(PairLine.Parse(PairLine.Render(pairs), known.Contains)));
        }

        [Fact]
        public void Parse_KhongBietTakesValue_CoiLaFlagVaGiaTriLaDoiSoViTri()
        {
            var pairs = PairLine.Parse("--flag value");
            Assert.Equal(2, pairs.Count);
            Assert.Equal("--flag", pairs[0].Key);
            Assert.Equal("", pairs[1].Key);
            Assert.Equal("value", pairs[1].Value);
        }

        [Fact]
        public void ScopeOf_GomCacDoiSoViTriDauDong_DungONhanCoKey()
        {
            var pairs = new[] { new ParamPair("", "migrations"), new ParamPair("", "add"), new ParamPair("", "Init"), new ParamPair("--project", "x") };
            // "Init" cũng giống tên lệnh con nên bị gom; người dùng đổi thành đường dẫn/số thì dừng. Test chặn cờ và giá trị không hợp lệ.
            Assert.Equal("migrations add Init", Toolbox.Views.ActionRow.ScopeOf("", pairs));
            Assert.Equal("ef migrations add", Toolbox.Views.ActionRow.ScopeOf("ef", pairs.Take(2)));
            Assert.Equal("", Toolbox.Views.ActionRow.ScopeOf("", new[] { new ParamPair("--no-build"), new ParamPair("", "add") }));
            Assert.Equal("migrations", Toolbox.Views.ActionRow.ScopeOf("", new[] { new ParamPair("", "migrations"), new ParamPair("", "./a.csproj") }));
        }

        [Fact]
        public void GoiY_LenhDaQuet_GhiDeCoChung_ChuaQuetThiLuiVeCoChung()
        {
            var data = new ToolCapabilities { Kind = "override-demo" };
            data.Entries.Add(new CapabilityEntry { Action = "", Name = "--info", Source = "scan" });
            data.Entries.Add(new CapabilityEntry { Action = "run", Name = "--project", TakesValue = true, Source = "scan" });
            data.Entries.Add(new CapabilityEntry { Action = "build", Name = "--output", TakesValue = true, Source = "scan" });
            CapabilityStore.Save(CapabilityStore.DefaultDirectory, data);

            var build = Toolbox.Views.PairEditor.SuggestionsFor("override-demo", "build", out var buildOwn);
            Assert.True(buildOwn);
            Assert.Equal(new[] { "--output" }, build.Select(e => e.Name).ToArray());   // không lẫn --info, không lẫn --project của run

            var test = Toolbox.Views.PairEditor.SuggestionsFor("override-demo", "test", out var testOwn);
            Assert.False(testOwn);
            Assert.Equal(new[] { "--info" }, test.Select(e => e.Name).ToArray());       // chưa quét: dùng cờ chung
        }

        [Fact]
        public void Parse_CoNgoaiBangQuet_TokenKeTiepLaGiaTri()
        {
            // --project, --startup-project, --context không có trong help của lệnh: vẫn phải tách thành cặp.
            var known = new Dictionary<string, bool> { ["--no-build"] = false };
            var pairs = PairLine.Parse(
                "--project Presentation/Nois.DxCMMS.Api --startup-project Presentation/Nois.DxCMMS.Api --context CmmsObjectContext --no-build",
                key => !known.TryGetValue(key, out var takes) || takes);

            Assert.Equal(
                new[] { "--project=Presentation/Nois.DxCMMS.Api", "--startup-project=Presentation/Nois.DxCMMS.Api", "--context=CmmsObjectContext", "--no-build=" },
                pairs.Select(p => p.Key + "=" + p.Value).ToArray());
        }

        [Fact]
        public void Parse_KeyCoDauBang_NoiThang()
        {
            var pairs = PairLine.Parse("--port=4200");
            Assert.Single(pairs);
            Assert.Equal("--port=", pairs[0].Key);
            Assert.Equal("--port=4200", PairLine.Render(pairs));
        }

        [Fact]
        public void Profile_CoCap_ExtraArgsDungCap_ProfileCuVanChay()
        {
            var old = new Profile { Vals = new Dictionary<string, string> { ["args"] = "--old x" } };
            Assert.Equal("--old x", old.ExtraArgs);
            Assert.False(old.HasPairs);

            var fresh = new Profile { Pairs = new List<ParamPair> { new ParamPair("--file", "A B.xlsx") } };
            Assert.Equal("--file \"A B.xlsx\"", fresh.ExtraArgs);
            Assert.Null(fresh.ArgsError);

            var ask = new Profile { Pairs = new List<ParamPair> { new ParamPair("", "", ask: true) } };
            Assert.Single(ask.PendingAsks);
            Assert.Equal("Name", ask.WithAnswers(new[] { "Name" }).ExtraArgs);
            Assert.Single(ask.PendingAsks);
        }

        // ----- Định dạng help thật (chạy dotnet 10 / dotnet-ef) -----

        private const string SystemCommandLineHelp = @"Options:
  -c, --clear             Clear the selected local resources or cache location(s).
  -l, --list              List the selected local resources or cache location(s).
  -?, -h, --help          Show help and usage information
  --project <PROJECT_PATH>                Defines the path of the project file to run.
  -lp, --launch-profile <LAUNCH_PROFILE>  The name of the launch profile.
  --no-build                              Do not build the project before running. [default: False]
";

        private const string McMasterHelp = @"Options:
  -o|--output-dir <PATH>                 The directory to put files in.
  -c|--context <DBCONTEXT>               The DbContext to use.
  -p|--project <PROJECT>                 The project to use.
  -s|--startup-project <PROJECT>         The startup project to use.
  --no-build                             Don't build the project.
  -v|--verbose                           Show verbose output.
";

        [Fact]
        public void HelpParser_SystemCommandLine_CoVaKhongCoGiaTri()
        {
            var entries = HelpParser.Parse(SystemCommandLineHelp).ToDictionary(e => e.Name);
            Assert.False(entries["--clear"].TakesValue);
            Assert.False(entries["--list"].TakesValue);
            Assert.True(entries["--project"].TakesValue);
            Assert.True(entries["--launch-profile"].TakesValue);
            Assert.False(entries["--no-build"].TakesValue);
        }

        [Fact]
        public void HelpParser_McMaster_DocDuocKeyDangPipe()
        {
            var entries = HelpParser.Parse(McMasterHelp).ToDictionary(e => e.Name);
            Assert.True(entries["--context"].TakesValue);
            Assert.True(entries["--startup-project"].TakesValue);
            Assert.True(entries["--output-dir"].TakesValue);
            Assert.False(entries["--no-build"].TakesValue);
            Assert.False(entries["--verbose"].TakesValue);
        }

        [Fact]
        public void HauTo_NhieuToken_HopLe()
        {
            Assert.True(CapabilityStore.IsSuffix("ef migrations add"));
            Assert.True(CapabilityStore.IsSuffix("nuget locals"));
            Assert.False(CapabilityStore.IsSuffix("ef  migrations"));
            Assert.False(CapabilityStore.IsSuffix("ef;calc"));
        }

        [Fact]
        public void HelpCommand_MauMacDinhVaMauTuChon()
        {
            var plain = new ToolDef("dotnet", "dotnet", "", null, null, "dotnet");
            Assert.Equal("dotnet ef migrations add --help", plain.HelpCommand("ef migrations add"));
            Assert.Equal("dotnet --help", plain.HelpCommand(""));

            var custom = new ToolDef("ng", "ng", "", null, null, "npx ng", "{base} help {tokens}");
            Assert.Equal("npx ng help build", custom.HelpCommand("build"));
        }

        [Fact]
        public void NhapTuNguonKhac_GiuQuaLanQuetLai_VaBoQuaMucTrung()
        {
            var data = new ToolCapabilities { Kind = "npm-like" };
            Assert.True(CapabilityStore.AddImported(data, "", "run build", false, "script build"));
            Assert.True(CapabilityStore.AddImported(data, "run build", "--watch", false, null));
            Assert.False(CapabilityStore.AddImported(data, "", "run build", false, null));   // trùng
            Assert.False(CapabilityStore.AddImported(data, "", "run;calc", false, null));    // tên sai

            // Quét lại lệnh gốc thay các mục quét cũ nhưng không đụng mục nhập vào.
            CapabilityStore.ApplyScan(data, "", new ScanOutcome { Command = "x", Fingerprint = "f", Entries = new List<CapabilityEntry> { new CapabilityEntry { Name = "--help" } } }, System.DateTime.Now);
            Assert.Contains(data.Entries, e => e.Name == "run build" && e.Source == "import");
            Assert.Contains(data.Entries, e => e.Name == "--watch" && e.Action == "run build");
            Assert.Equal("run build", CapabilityStore.Subcommands(data).Single().Name);
        }
    }
}
