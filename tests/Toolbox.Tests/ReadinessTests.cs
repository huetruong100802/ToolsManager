using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class ReadinessTests
    {
        private static ToolStore Library(TempDir dir, params ToolEntry[] tools)
        {
            var store = ToolStore.Load(dir.Combine("tools.json"));
            foreach (var tool in tools)
                store.Add(tool);
            return store;
        }

        private static Workspace Ws(TempDir dir)
        {
            var path = dir.Combine("repo");
            Directory.CreateDirectory(path);
            return new Workspace { Id = "w1", Name = "W", Path = path };
        }

        [Fact]
        public void WorkspaceDayDu_KhongCoVanDe()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("tool.exe", "x");
                var ws = Ws(dir);
                var library = Library(dir, new ToolEntry { Name = "Có file", Path = file }, new ToolEntry { Name = "Git", Command = "git" });
                ws.Bindings.Add(new ToolBinding { ToolId = "co-file" });
                ws.Bindings.Add(new ToolBinding { ToolId = "git" });
                Assert.Empty(Readiness.Check(ws, library, commandExists: p => true));
            }
        }

        [Fact]
        public void ThuMucMat_ToolKhongCoTrongThuVien_ToolMatDuongDan_LenhKhongCoTrenPath()
        {
            using (var dir = new TempDir())
            {
                var ws = new Workspace { Id = "w1", Name = "W", Path = dir.Combine("khong-co") };
                var library = Library(dir,
                    new ToolEntry { Name = "Mất", Path = dir.Combine("mat.exe") },
                    new ToolEntry { Name = "Lệnh lạ", Command = "chuong-trinh-la --x" });
                ws.Bindings.Add(new ToolBinding { ToolId = "mat" });
                ws.Bindings.Add(new ToolBinding { ToolId = "lenh-la" });
                ws.Bindings.Add(new ToolBinding { ToolId = "chua-co" });

                var issues = Readiness.Check(ws, library, commandExists: p => false);

                Assert.Contains(issues, i => i.Fix == ReadinessFix.RelocateWorkspace);
                Assert.Contains(issues, i => i.Fix == ReadinessFix.EditTool && i.TargetId == "mat" && i.Title.Contains("mất đường dẫn"));
                Assert.Contains(issues, i => i.Fix == ReadinessFix.EditTool && i.TargetId == "lenh-la" && i.Title.Contains("chuong-trinh-la"));
                var missing = issues.Single(i => i.Fix == ReadinessFix.UnbindTool);
                Assert.Equal("chua-co", missing.TargetId);
                Assert.All(issues.Where(i => i.Fix != ReadinessFix.OpenProfile), i => Assert.Equal(ReadinessLevel.Error, i.Level));
            }
        }

        [Fact]
        public void ToolCanCai_NoiCanCaiDat()
        {
            using (var dir = new TempDir())
            {
                var setup = dir.WriteFile("setup.exe", "x");
                var ws = Ws(dir);
                var library = Library(dir, new ToolEntry { Name = "Cần cài", Path = dir.Combine("chua-cai.exe"), Pack = PackMode.Setup, Setup = setup });
                ws.Bindings.Add(new ToolBinding { ToolId = "can-cai" });
                var issue = Readiness.Check(ws, library, commandExists: p => true).Single();
                Assert.Equal(ReadinessFix.InstallTool, issue.Fix);
                Assert.Equal("Cài đặt", issue.FixText);
            }
        }

        [Fact]
        public void ProfileVaWorkflowTroVaoThuKhongCon_DuocBao()
        {
            using (var dir = new TempDir())
            {
                var ws = Ws(dir);
                var library = Library(dir);
                ws.Profiles.Add(new Profile { Id = "p1", Label = "Profile hỏng", Kind = "tool-khong-co", ActionId = "", VariantId = "" });
                ws.Stacks.Add(new StackDef
                {
                    Id = "s1", Label = "Workflow hỏng",
                    Steps = new List<StackStep> { new StackStep { Kind = TaskKey.Custom, ActionId = "khong-co", Wait = WaitKind.Exit } },
                });

                var issues = Readiness.Check(ws, library, commandExists: p => true);

                Assert.Contains(issues, i => i.Fix == ReadinessFix.OpenProfile && i.TargetId == "p1");
                Assert.Contains(issues, i => i.Fix == ReadinessFix.OpenWorkflow && i.TargetId == "s1" && i.Title.Contains("1 bước hỏng"));
            }
        }

        [Theory]
        [InlineData("npm run dev", "npm")]
        [InlineData("\"C:/Program Files/x/tool.exe\" --a", "C:/Program Files/x/tool.exe")]
        [InlineData("git", "git")]
        [InlineData("   ", null)]
        public void FirstToken_LayChuongTrinhDauTien(string line, string expected) => Assert.Equal(expected, Readiness.FirstToken(line));

        [Fact]
        public void OnPath_TimTrenPathVaDuongDanTuyetDoi()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("chay-thu.cmd", "@echo off");
                var original = Environment.GetEnvironmentVariable("PATH");
                try
                {
                    Environment.SetEnvironmentVariable("PATH", dir.Path + ";" + original);
                    Assert.True(Readiness.OnPath("chay-thu"));      // thử các đuôi chạy được
                    Assert.True(Readiness.OnPath("chay-thu.cmd"));
                    Assert.True(Readiness.OnPath(file));            // đường dẫn tuyệt đối
                    Assert.True(Readiness.OnPath("cmd"));           // lệnh dựng sẵn của shell
                    Assert.False(Readiness.OnPath("chuong-trinh-khong-ton-tai-123"));
                    Assert.False(Readiness.OnPath(""));
                }
                finally
                {
                    Environment.SetEnvironmentVariable("PATH", original);
                }
            }
        }
    }
}
