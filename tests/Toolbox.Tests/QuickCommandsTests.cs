using System.Collections.Generic;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class QuickCommandsTests
    {
        private static Workspace Make(string path, params string[] toolIds) => new Workspace
        {
            Id = "w1", Name = "Demo", Path = path,
            Bindings = toolIds.Select(t => new ToolBinding { ToolId = t }).ToList(),
        };

        [Fact]
        public void WorkspaceKhongGanTool_TraRong_KhongNem()
        {
            using (var dir = new TempDir())
            {
                Assert.Empty(QuickCommands.For(Make(dir.Path)));
                Assert.Empty(QuickCommands.For(null));
            }
        }

        [Fact]
        public void Id_OnDinhQuaHaiLanGoi_VaKhongTrung()
        {
            using (var dir = new TempDir())
            {
                var workspace = Make(dir.Path, "git", "dotnet");
                workspace.Customs.Add(new CustomCommand { Id = "c1", Label = "Dọn", CommandLine = "git clean -fdx" });
                workspace.Profiles.Add(new Profile { Id = "p1", Label = "Trạng thái", Kind = "git", ActionId = "status", VariantId = "" });

                var first = QuickCommands.For(workspace).Select(q => q.Id).ToList();
                var second = QuickCommands.For(workspace).Select(q => q.Id).ToList();

                Assert.NotEmpty(first);
                Assert.Equal(first, second);
                Assert.Equal(first.Count, first.Distinct().Count());
                Assert.All(first, id => Assert.StartsWith("w1:", id));
            }
        }

        [Fact]
        public void LenhGitVaDotnet_RaDungChuoiLenh()
        {
            using (var dir = new TempDir())
            {
                var all = QuickCommands.For(Make(dir.Path, "git", "dotnet"));

                var status = all.Single(q => q.ToolId == "git" && q.ActionId == "status");
                Assert.Equal("git status", status.CommandLine);
                Assert.Equal(QuickSource.Action, status.Source);
                Assert.Equal("git", status.Binding.ToolId);
                Assert.True(status.IsRunnable);

                var build = all.Single(q => q.ToolId == "dotnet" && q.ActionId == "build");
                Assert.Equal("dotnet build", build.CommandLine);
                Assert.Equal("build", build.Title);
                Assert.Equal(all.First(q => q.ToolId == "dotnet").Group, build.Group);
                Assert.Equal(dir.Path, build.Cwd);
            }
        }

        [Fact]
        public void ScriptNpm_RaNpmRun()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("package.json", "{ \"scripts\": { \"build\": \"tsc\", \"start\": \"node .\" } }");

                var scripts = QuickCommands.For(Make(dir.Path, "npm")).Where(q => q.Source == QuickSource.Script).ToList();

                Assert.Equal(new[] { "npm run build", "npm run start" }, scripts.Select(q => q.CommandLine).OrderBy(x => x).ToArray());
                Assert.All(scripts, q => Assert.Equal("npm", q.ToolId));
                Assert.Equal("build", scripts.Single(q => q.CommandLine == "npm run build").ActionId);
            }
        }

        [Fact]
        public void BindingCwd_TroToThuMucChuaPackageJson()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("web/package.json", "{ \"scripts\": { \"lint\": \"eslint .\" } }");
                var workspace = Make(dir.Path, "npm");
                workspace.Bindings[0].Cwd = "web";

                var lint = QuickCommands.For(workspace).Single(q => q.Source == QuickSource.Script);

                Assert.Equal("npm run lint", lint.CommandLine);
                Assert.Equal(dir.Combine("web"), lint.Cwd);
            }
        }

        [Fact]
        public void LenhRiengVaProfile_DungNguonVaChuoiLenh()
        {
            using (var dir = new TempDir())
            {
                var workspace = Make(dir.Path, "git");
                workspace.Customs.Add(new CustomCommand { Id = "c1", Label = "Dọn", CommandLine = "git clean -fdx" });
                workspace.Profiles.Add(new Profile { Id = "p1", Label = "Trạng thái", Kind = "git", ActionId = "status", VariantId = "" });
                workspace.Profiles.Add(new Profile
                {
                    Id = "p2", Label = "Dọn kèm cờ", Kind = ProfileKind.Custom, RefId = "c1",
                    Vals = new Dictionary<string, string> { [ProfileKind.ArgsKey] = "-n" },
                });
                workspace.Profiles.Add(new Profile { Id = "p3", Label = "Hỏng", Kind = "git", ActionId = "khong-co", VariantId = "" });

                var all = QuickCommands.For(workspace);

                var custom = all.Single(q => q.Source == QuickSource.Custom);
                Assert.Equal("Lệnh riêng", custom.Group);
                Assert.Equal("git clean -fdx", custom.CommandLine);
                Assert.Equal("c1", custom.RefId);

                var profiles = all.Where(q => q.Source == QuickSource.Profile).ToDictionary(q => q.RefId);
                Assert.Equal("Profile", profiles["p1"].Group);
                Assert.Equal("git status", profiles["p1"].CommandLine);
                Assert.Equal("git clean -fdx -n", profiles["p2"].CommandLine);
                Assert.False(profiles["p3"].IsRunnable);
            }
        }

        [Fact]
        public void QuickKey_DungKhoaQuickVaId()
        {
            Assert.Equal("quick:w1:action:git:status:", UsageStore.QuickKey("w1:action:git:status:"));
        }
    }
}
