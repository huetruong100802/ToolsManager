using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class PositionalOptionsTests
    {
        private const string DotnetBuildHelp = @"Description:
  .NET Builder

Usage:
  dotnet build [<PROJECT | SOLUTION | FILE>...] [options]

Arguments:
  <PROJECT | SOLUTION | FILE>  The project or solution or C# (file-based program) file to operate on. If a file is not specified, the command will search the current directory for a project or solution.

Options:
  -c, --configuration <CONFIGURATION>  The configuration to use for building the project. The default for most projects is 'Debug'.
  -v, -verbosity <LEVEL>               Set the MSBuild verbosity level. Allowed values are q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic].
  --no-restore                         Do not restore the project before building. [default: False]
";

        private const string NugetLocalsHelp = @"Usage:
  dotnet nuget locals <all | global-packages | http-cache | plugins-cache | temp> [options]

Arguments:
  <all | global-packages | http-cache | plugins-cache | temp>  Cache Location(s) to be cleared.

Options:
  -c, --clear  Clear the selected local resources or cache location(s).
";

        private const string ArgparseHelp = @"usage: tool [-h] {start,stop} name

positional arguments:
  {start,stop}   what to do
  name           the target
                 continues here

options:
  -h, --help     show this help message
  --mode {fast,slow}  speed
";

        [Fact]
        public void ParseArguments_DotnetBuild_DoiSoLaDuongDanKhongPhaiLuaChon()
        {
            var args = HelpParser.ParseArguments(DotnetBuildHelp);
            var arg = Assert.Single(args);
            Assert.Equal("PROJECT | SOLUTION | FILE", arg.Name);
            Assert.True(arg.IsPath);
            Assert.False(arg.DirectoryOnly);
            Assert.Null(arg.Choices);   // toàn chữ hoa là kiểu, không phải giá trị cố định
        }

        [Fact]
        public void ParseArguments_NugetLocals_LayDuocLuaChonTuKhoiNhonNhon()
        {
            var arg = Assert.Single(HelpParser.ParseArguments(NugetLocalsHelp));
            Assert.Equal(new[] { "all", "global-packages", "http-cache", "plugins-cache", "temp" }, arg.Choices.ToArray());
            Assert.False(arg.IsPath);
        }

        [Fact]
        public void ParseArguments_Argparse_NoiDongMoTaVaLuaChonTrongNgoacNhon()
        {
            var args = HelpParser.ParseArguments(ArgparseHelp);
            Assert.Equal(2, args.Count);
            Assert.Equal(new[] { "start", "stop" }, args[0].Choices.ToArray());
            Assert.Equal("the target continues here", args[1].Description);
            // Dòng "options:" kết thúc mục: -h không bị đọc nhầm thành đối số.
            Assert.DoesNotContain(args, a => a.Name.StartsWith("-"));
        }

        [Fact]
        public void Parse_CoVerbosity_LayGiaTriChoPhepVaMoRongVietTat()
        {
            var entries = HelpParser.Parse(DotnetBuildHelp).ToDictionary(e => e.Name);
            Assert.Equal(new[] { "quiet", "minimal", "normal", "detailed", "diagnostic" }, entries["-verbosity"].Choices.ToArray());
            Assert.Null(entries["--configuration"].Choices);   // chỉ nhắc mặc định 'Debug', không liệt kê → không đoán
            Assert.Null(entries["--no-restore"].Choices);
        }

        [Fact]
        public void ExtractChoices_CauVanThuong_KhongBiNuot()
        {
            Assert.Null(HelpParser.ExtractChoices("Possible values: see the documentation for details"));
            Assert.Null(HelpParser.ExtractChoices("The path to the project to use."));
            Assert.Equal(new[] { "a", "b" }, HelpParser.ExtractChoices("--mode {a,b}").ToArray());
            Assert.Equal(new[] { "debug", "release" }, HelpParser.ExtractChoices("One of: debug, release. Default is debug").ToArray());
        }

        // Nguyên văn `dotnet nuget locals --help` và `dotnet new --help` (rút gọn): lựa chọn nằm ở dòng nối tiếp; lệnh con kèm <tham số>.
        private const string RealLocalsHelp = @"Arguments:
  <Cache Location(s)>  Specifies the cache location(s) to list or clear.
                       <all | http-cache | global-packages | temp>

Options:
  -c, --clear  Clear the selected local resources or cache location(s).
";

        private const string RealNewHelp = @"Options:
  -v, --verbosity <LEVEL>  Sets the verbosity level. Allowed values are q[uiet], m[inimal], n[ormal], and diag[nostic]. [default: normal]

Commands:
  create <template-short-name> <template-args>  Instantiates a template with given short name. An alias of 'dotnet new <template name>'.
  install <package>                             Installs a template package.
  list <template-name>                          Lists templates containing the specified template name.
";

        [Fact]
        public void HelpThat_NugetLocals_LuaChonOCotMoTaDongNoiTiep()
        {
            var arg = Assert.Single(HelpParser.ParseArguments(RealLocalsHelp));
            Assert.Equal("Cache Location(s)", arg.Name);
            Assert.Equal(new[] { "all", "http-cache", "global-packages", "temp" }, arg.Choices.ToArray());
            Assert.False(arg.IsPath);
        }

        [Fact]
        public void HelpThat_DotnetNew_LenhConKemThamSoVaVerbosityCoDefault()
        {
            Assert.Equal(new[] { "create", "install", "list" }, HelpParser.ParseSubcommands(RealNewHelp).Select(e => e.Name).ToArray());
            var verbosity = HelpParser.Parse(RealNewHelp).Single(e => e.Name == "--verbosity");
            Assert.Equal(new[] { "quiet", "minimal", "normal", "diagnostic" }, verbosity.Choices.ToArray());
        }

        private static ToolCapabilities Sample()
        {
            var data = new ToolCapabilities { Kind = "demo" };
            void Child(string scope, string name) => data.Entries.Add(new CapabilityEntry { Action = scope, Name = name, Source = "scan" });
            Child("ef", "database");
            Child("ef", "migrations");
            Child("ef migrations", "add");
            Child("ef migrations", "list");
            Child("ef", "--help");
            data.Args.Add(new ArgHint { Action = "ef migrations add", Name = "NAME" });
            data.Args.Add(new ArgHint { Action = "build", Name = "PROJECT | SOLUTION | FILE", IsPath = true });
            data.Args.Add(new ArgHint { Action = "nuget locals", Name = "location", Choices = new List<string> { "all", "temp" } });
            data.Args.Add(new ArgHint { Action = "pack", Name = "FILE", IsPath = true, Variadic = true });
            return data;
        }

        [Fact]
        public void Resolve_ChuoiLenh_GoiYLenhConTheoPhamViTiepTheo()
        {
            var data = Sample();
            Assert.Equal(new[] { "database", "migrations" }, PositionalOptions.Resolve(data, "ef", new ParamPair[0]).Values.ToArray());
            Assert.Equal(new[] { "add", "list" },
                PositionalOptions.Resolve(data, "ef", new[] { new ParamPair("", "migrations") }).Values.ToArray());

            // Tới lệnh lá: đối số kế tiếp là tên tự do, không có gợi ý.
            var leaf = PositionalOptions.Resolve(data, "ef", new[] { new ParamPair("", "migrations"), new ParamPair("", "add") });
            Assert.False(leaf.Any);
        }

        [Fact]
        public void Resolve_DoiSoCuaLenh_DuongDanVaLuaChon()
        {
            var data = Sample();
            Assert.True(PositionalOptions.Resolve(data, "build", new ParamPair[0]).IsPath);
            Assert.Equal(new[] { "all", "temp" },
                PositionalOptions.Resolve(data, "nuget", new[] { new ParamPair("", "locals") }).Values.ToArray());

            // Sau cờ có key, đối số kế tiếp vẫn là đối số đầu của phạm vi.
            Assert.True(PositionalOptions.Resolve(data, "build", new[] { new ParamPair("--no-restore") }).IsPath);
            // Đã có một đường dẫn rồi; đối số thứ hai của lệnh chỉ có một đối số không có gợi ý.
            Assert.False(PositionalOptions.Resolve(data, "build", new[] { new ParamPair("", "./a.csproj") }).Any);
            // Đối số lặp (FILE...) vẫn gợi ý đường dẫn ở các dòng sau.
            Assert.True(PositionalOptions.Resolve(data, "pack", new[] { new ParamPair("", "./a.csproj") }).IsPath);
        }

        [Fact]
        public void PathCandidates_BoThuMucSinhRaVaThuMucAn_ViTriGocTruoc()
        {
            var root = Path.Combine(Path.GetTempPath(), "pc-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "all.sln"), "");
            File.WriteAllText(Path.Combine(root, "src", "App", "App.csproj"), "");
            File.WriteAllText(Path.Combine(root, "bin", "x.dll"), "");
            File.WriteAllText(Path.Combine(root, ".git", "config"), "");
            try
            {
                var files = PositionalOptions.PathCandidates(root, false);
                Assert.Equal(new[] { "all.sln", Path.Combine("src", "App", "App.csproj") }, files.ToArray());
                var dirs = PositionalOptions.PathCandidates(root, true);
                Assert.Contains("src", dirs);
                Assert.DoesNotContain("bin", dirs);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ApplyScan_LuuVaThayDoiSoViTriTheoPhamVi_VaBangCuKhongCoArgsVanDoc()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cap-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var data = new ToolCapabilities { Kind = "argdemo" };
                CapabilityStore.ApplyScan(data, "build", new ScanOutcome
                {
                    Command = "x", Fingerprint = "f",
                    Entries = new List<CapabilityEntry> { new CapabilityEntry { Name = "--no-restore" } },
                    Args = HelpParser.ParseArguments(DotnetBuildHelp),
                }, System.DateTime.Now);
                CapabilityStore.ApplyScan(data, "build", new ScanOutcome
                {
                    Command = "x", Fingerprint = "g",
                    Entries = new List<CapabilityEntry> { new CapabilityEntry { Name = "--no-restore" } },
                    Args = new List<ArgHint>(),
                }, System.DateTime.Now);
                Assert.Empty(data.Args);   // quét lại thay hẳn đối số cũ của phạm vi

                CapabilityStore.ApplyScan(data, "build", new ScanOutcome
                {
                    Command = "x", Fingerprint = "h",
                    Entries = new List<CapabilityEntry> { new CapabilityEntry { Name = "-v", Choices = new List<string> { "quiet", "minimal" } } },
                    Args = HelpParser.ParseArguments(DotnetBuildHelp),
                }, System.DateTime.Now);
                CapabilityStore.Save(dir, data);
                var back = CapabilityStore.Load(dir, "argdemo");
                Assert.True(Assert.Single(back.Args).IsPath);
                Assert.Equal(new[] { "quiet", "minimal" }, back.Entries.Single(e => e.Name == "-v").Choices.ToArray());

                // File cũ không có "Args": vẫn đọc được.
                File.WriteAllText(CapabilityStore.PathFor(dir, "old"), "{\"Kind\":\"old\",\"Entries\":[{\"Action\":\"\",\"Name\":\"build\",\"Source\":\"manual\"}]}");
                Assert.Empty(CapabilityStore.Load(dir, "old").Args);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
