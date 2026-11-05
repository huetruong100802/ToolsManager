using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Danh mục tool đọc từ JSON: thêm tool mới chỉ cần khai báo, không sửa code.</summary>
    public class ToolCatalogDataTests
    {
        private const string Flutter = @"{
  ""tools"": [
    {
      ""kind"": ""flutter"",
      ""name"": ""Flutter"",
      ""rule"": ""pubspec.yaml"",
      ""command"": ""flutter"",
      ""detect"": { ""patterns"": [ ""pubspec.yaml"" ], ""depth"": 0 }
    }
  ]
}";

        [Fact]
        public void Parse_ToolMoi_DocDuocDetectVaLenhGoc()
        {
            var tool = ToolCatalog.Parse(Flutter).Single();

            Assert.Equal("flutter", tool.Kind);
            Assert.Equal("flutter", tool.Command);
            Assert.Equal(new[] { "pubspec.yaml" }, tool.Detect.Patterns.ToArray());
            Assert.Null(tool.ActionsFrom);
        }

        [Fact]
        public void Parse_ThieuLenhGocVaActionsFrom_BiTuChoi()
        {
            const string json = @"{ ""tools"": [ { ""kind"": ""x"", ""name"": ""X"" } ] }";

            Assert.Throws<InvalidDataException>(() => ToolCatalog.Parse(json));
        }

        [Fact]
        public void Parse_LenhGocCoKyTuNguyHiem_BiTuChoi()
        {
            const string json = @"{ ""tools"": [ { ""kind"": ""x"", ""name"": ""X"", ""command"": ""x & calc"" } ] }";

            Assert.Throws<InvalidDataException>(() => ToolCatalog.Parse(json));
        }

        [Fact]
        public void Parse_TrungKind_BiTuChoi()
        {
            const string json = @"{ ""tools"": [
  { ""kind"": ""flutter"", ""name"": ""A"", ""command"": ""flutter"" },
  { ""kind"": ""FLUTTER"", ""name"": ""B"", ""command"": ""flutter"" } ] }";

            Assert.Throws<InvalidDataException>(() => ToolCatalog.Parse(json));
        }

        [Fact]
        public void Parse_DuongDanRaNgoaiThuMuc_BiTuChoi()
        {
            const string json = @"{ ""tools"": [
  { ""kind"": ""x"", ""name"": ""X"", ""detect"": { ""patterns"": [ ""..\\secret.txt"" ] } } ] }";

            Assert.Throws<InvalidDataException>(() => ToolCatalog.Parse(json));
        }

        [Fact]
        public void Parse_KhongCoMangTools_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => ToolCatalog.Parse("{\"khac\": 1}"));
        }

        [Fact]
        public void ReadKeys_LayProjectTuAngularJson()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("angular.json", "{\"projects\":{\"admin\":{},\"customer\":{}}}");

                var keys = ToolCatalog.ReadKeys(dir.Combine("angular.json"), "projects");

                Assert.Equal(new[] { "admin", "customer" }, keys.ToArray());
            }
        }

        // Nguồn danh sách của tool kiểu npm: mỗi mục một action, nhãn là chính chữ đó.
        private const string Make = @"{ ""tools"": [ {
  ""kind"": ""make"", ""name"": ""Make"",
  ""actionsFrom"": { ""file"": ""Makefile"", ""pattern"": ""^([A-Za-z0-9_.-]+):"", ""template"": ""make {id}"" } } ] }";

        private static ValueSource MakeSource(string source)
        {
            var json = Make.Replace("\"file\": \"Makefile\", \"pattern\": \"^([A-Za-z0-9_.-]+):\"", source);
            return ToolCatalog.Parse(json).Single().ActionsFrom.Source;
        }

        [Fact]
        public void Options_TuPatternTrongMakefile_LayNhomMotCuaMoiDong()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("Makefile", "build:\n\tdotnet build\ntest:\n\tdotnet test\n# bình luận\n");

                var source = MakeSource("\"file\": \"Makefile\", \"pattern\": \"^([A-Za-z0-9_.-]+):\"");

                Assert.Equal(new[] { "build", "test" }, ToolCatalog.ReadSource(dir.Path, source).ToArray());
            }
        }

        [Fact]
        public void Options_ValuesGhiSan_KhongCanFile()
        {
            var source = MakeSource("\"values\": [ \"admin\", \"customer\" ]");

            Assert.Equal(new[] { "admin", "customer" }, ToolCatalog.ReadSource(null, source).ToArray());
        }

        [Fact]
        public void ReadSource_SetupKhongCoFile_KhongCoGoiYNhungKhongLoi()
        {
            using (var dir = new TempDir())
            {
                var source = MakeSource("\"file\": \"Makefile\", \"pattern\": \"^([A-Za-z0-9_.-]+):\"");

                Assert.Empty(ToolCatalog.ReadSource(dir.Path, source));
                Assert.Empty(ToolCatalog.ReadSource(null, source));
            }
        }

        [Fact]
        public void Parse_PatternKhongCoNhom_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeSource("\"file\": \"Makefile\", \"pattern\": \"^build:\""));
        }

        [Fact]
        public void Parse_PatternSai_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeSource("\"file\": \"Makefile\", \"pattern\": \"(\""));
        }

        [Fact]
        public void Parse_ValuesCoKyTuLa_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeSource("\"values\": [ \"a&b\" ]"));
        }

        [Fact]
        public void Parse_NguonRaNgoaiThuMuc_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeSource("\"file\": \"..\\\\Makefile\", \"pattern\": \"^([a-z]+):\""));
        }

        [Fact]
        public void Upgrade_CatalogSchemaCu_SaoLuuRoiGhiBanMacDinh()
        {
            using (var dir = new TempDir())
            {
                const string old = "{ \"schemaVersion\": 2, \"tools\": [ { \"kind\": \"x\", \"name\": \"X\", \"actions\": [] } ] }";
                var path = dir.WriteFile("catalog.json", old);

                var text = ToolCatalog.UpgradeOutdated(path, old);

                Assert.NotEqual(old, text);
                Assert.Equal(text, File.ReadAllText(path));
                Assert.Equal(old, File.ReadAllText(path + ".v2.bak"));
                Assert.Contains(ToolCatalog.Parse(text), t => t.Kind == "dotnet" && t.Command == "dotnet");
            }
        }

        [Fact]
        public void Upgrade_CatalogMoiHoacKhongCoSchema_KhongDungToi()
        {
            using (var dir = new TempDir())
            {
                const string mine = "{ \"tools\": [ { \"kind\": \"x\", \"name\": \"X\", \"command\": \"x\" } ] }";
                var path = dir.WriteFile("catalog.json", mine);

                Assert.Same(mine, ToolCatalog.UpgradeOutdated(path, mine));
                Assert.Equal(mine, File.ReadAllText(path));
                Assert.False(File.Exists(path + ".v0.bak"));
            }
        }

        [Fact]
        public void Build_LenhConCoCongVaHauTo_GhepDungThuTu()
        {
            var serve = CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "serve", "--port")).Default;

            var line = CommandBuilder.Build(serve, new System.Collections.Generic.Dictionary<string, string> { ["port"] = "4200", ["args"] = "dxfac" });

            Assert.Equal("npx ng serve --port 4200 dxfac", line);
        }
    }
}
