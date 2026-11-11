using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class WorkspaceDetectorTests
    {
        [Fact]
        public void Detect_NhanDienDotnetGitAngularNpm()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("app.sln", "");
                Directory.CreateDirectory(dir.Combine(".git"));
                dir.WriteFile(@"frontend\angular.json", "{\"projects\":{\"admin\":{},\"customer\":{}}}");
                dir.WriteFile(@"frontend\package.json", "{\"scripts\":{\"start\":\"ng serve\",\"lint\":\"ng lint\",\"test\":\"ng test\"}}");

                var found = WorkspaceDetector.Detect(dir.Path).ToDictionary(d => d.Kind);

                Assert.Equal(new[] { "dotnet", "git", "angular", "npm" }.OrderBy(x => x), found.Keys.OrderBy(x => x));
                Assert.Equal("frontend", found["angular"].Cwd);
                Assert.Contains("angular.json", found["angular"].Reason);
                Assert.Equal("frontend", found["npm"].Cwd);
                Assert.Contains("3 mục", found["npm"].Reason);
                Assert.Contains("app.sln", found["dotnet"].Reason);
            }
        }

        [Fact]
        public void DetectionOption_DemSoLenhVaGhiThuMucChay()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"frontend\package.json", "{\"scripts\":{\"start\":\"ng serve\",\"build\":\"ng build\"}}");
                var npm = WorkspaceDetector.Detect(dir.Path).Single(d => d.Kind == "npm");

                var option = new Toolbox.Views.AddWorkspaceSheet.DetectionOption(npm, dir.Path);

                Assert.True(option.IsChecked);
                Assert.True(option.Commands >= 2);
                Assert.Contains("lệnh", option.Summary);
                Assert.Contains("frontend", option.Summary);
            }
        }

        [Fact]
        public void Detect_BoQuaNodeModules()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"node_modules\x\package.json", "{\"scripts\":{\"a\":\"b\"}}");

                Assert.Empty(WorkspaceDetector.Detect(dir.Path));
            }
        }

        [Fact]
        public void Detect_ThuMucKhongTonTai_RongKhongNemLoi() =>
            Assert.Empty(WorkspaceDetector.Detect(@"C:\khong\ton\tai\toolbox"));

        [Fact]
        public void ReadKeys_BoTenCoKyTuLaVaFileHong()
        {
            using (var dir = new TempDir())
            {
                var good = dir.WriteFile("a.json", "{\"scripts\":{\"build\":\"x\",\"evil&calc\":\"y\",\"test:unit\":\"z\"}}");
                var bad = dir.WriteFile("b.json", "{ hỏng");

                Assert.Equal(new[] { "build", "test:unit" }, ToolCatalog.ReadKeys(good, "scripts").ToArray());
                Assert.Empty(ToolCatalog.ReadKeys(bad, "scripts"));
                Assert.Empty(ToolCatalog.ReadKeys(dir.Combine("khong-co.json"), "scripts"));
            }
        }

        [Fact]
        public void ActionsFor_NpmDocScriptTuPackageJson()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"web\package.json", "{\"scripts\":{\"start\":\"a\",\"lint\":\"b\"}}");
                var ws = new Workspace { Name = "w", Path = dir.Path };
                var binding = new ToolBinding { ToolId = "npm", Cwd = "web" };

                // Bỏ action gốc (Id rỗng): mọi tool đều có, đây chỉ kiểm script đọc từ package.json.
                var actions = ToolCatalog.ActionsFor(ws, binding).Where(a => a.Id.Length > 0).ToList();

                Assert.Equal(new[] { "start", "lint" }, actions.Select(a => a.Id).ToArray());
                Assert.Equal("npm run start", actions[0].Default.Template);
            }
        }
    }
}
