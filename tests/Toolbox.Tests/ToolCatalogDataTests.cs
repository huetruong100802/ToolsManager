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
      ""detect"": { ""patterns"": [ ""pubspec.yaml"" ], ""depth"": 0 },
      ""actions"": [
        { ""id"": ""run"", ""label"": ""Chạy"", ""variants"": [
          { ""id"": ""device"", ""label"": ""Theo thiết bị"", ""template"": ""flutter run -d {device}"",
            ""params"": [ { ""key"": ""device"", ""prompt"": ""Thiết bị"", ""default"": ""chrome"" } ] } ] }
      ]
    }
  ]
}";

        [Fact]
        public void Parse_ToolMoi_DocDuocDetectVaLenh()
        {
            var tool = ToolCatalog.Parse(Flutter).Single();

            Assert.Equal("flutter", tool.Kind);
            Assert.Equal(new[] { "pubspec.yaml" }, tool.Detect.Patterns.ToArray());
            var param = tool.Actions[0].Variants[0].Params[0];
            Assert.Equal("device", param.Key);
            Assert.Equal("chrome", param.Default);
            Assert.Equal("flutter run -d chrome", CommandBuilder.Build(tool.Actions[0].Variants[0], new System.Collections.Generic.Dictionary<string, string> { ["device"] = "chrome" }));
        }

        [Fact]
        public void Parse_TrungKind_BiTuChoi()
        {
            const string json = @"{ ""tools"": [
  { ""kind"": ""flutter"", ""name"": ""A"" },
  { ""kind"": ""FLUTTER"", ""name"": ""B"" } ] }";

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

        private const string Make = @"{ ""tools"": [ {
  ""kind"": ""make"", ""name"": ""Make"",
  ""actions"": [ { ""id"": ""run"", ""label"": ""Chạy"", ""variants"": [
    { ""id"": ""target"", ""label"": ""Target"", ""template"": ""make {target}"",
      ""params"": [ { ""key"": ""target"", ""prompt"": ""Target"", ""options"": { ""file"": ""Makefile"", ""pattern"": ""^([A-Za-z0-9_.-]+):"" } } ] } ] } ] } ] }";

        private static ParamDef MakeParam(string options)
        {
            var json = Make.Replace("{ \"file\": \"Makefile\", \"pattern\": \"^([A-Za-z0-9_.-]+):\" }", options);
            return ToolCatalog.Parse(json).Single().Actions[0].Variants[0].Params[0];
        }

        [Fact]
        public void Options_TuPatternTrongMakefile_LayNhomMotCuaMoiDong()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("Makefile", "build:\n\tdotnet build\ntest:\n\tdotnet test\n# bình luận\n");

                var param = MakeParam("{ \"file\": \"Makefile\", \"pattern\": \"^([A-Za-z0-9_.-]+):\" }");

                Assert.Equal(new[] { "build", "test" }, ToolCatalog.OptionsFor(param, dir.Path).ToArray());
            }
        }

        [Fact]
        public void Options_ValuesGhiSan_KhongCanFile()
        {
            var param = MakeParam("{ \"values\": [ \"admin\", \"customer\" ] }");

            Assert.Equal(new[] { "admin", "customer" }, ToolCatalog.OptionsFor(param, null).ToArray());
        }

        [Fact]
        public void Options_SetupKhongCoAngularJson_KhongCoGoiYNhungVanChay()
        {
            using (var dir = new TempDir())
            {
                // Setup không có angular.json: danh sách project rỗng, ô vẫn gõ tay, không báo lỗi.
                var project = ToolCatalog.Find("angular").Actions.First(a => a.Id == "serve").Default.Params.First(p => p.Key == "project");

                Assert.Empty(ToolCatalog.OptionsFor(project, dir.Path));
                Assert.Empty(ToolCatalog.OptionsFor(project, null));
            }
        }

        [Fact]
        public void Parse_PatternKhongCoNhom_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeParam("{ \"file\": \"Makefile\", \"pattern\": \"^build:\" }"));
        }

        [Fact]
        public void Parse_PatternSai_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeParam("{ \"file\": \"Makefile\", \"pattern\": \"(\" }"));
        }

        [Fact]
        public void Parse_ValuesCoKyTuLa_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeParam("{ \"values\": [ \"a&b\" ] }"));
        }

        [Fact]
        public void Parse_NguonRaNgoaiThuMuc_BiTuChoi()
        {
            Assert.Throws<InvalidDataException>(() => MakeParam("{ \"file\": \"..\\\\Makefile\", \"pattern\": \"^([a-z]+):\" }"));
        }

        [Fact]
        public void Build_ProjectDaChon_ChenVaoLenh()
        {
            var serve = CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "serve", "--port")).Default;

            var line = CommandBuilder.Build(serve, new System.Collections.Generic.Dictionary<string, string> { ["project"] = "dxfac", ["port"] = "4200" });

            Assert.Equal("npx ng serve dxfac --port 4200", line);
        }
    }
}
