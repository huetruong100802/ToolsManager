using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class AgentPromptsTests
    {
        private static Workspace Ws() => new Workspace
        {
            Id = "w1", Name = "DxFactory", Path = @"C:\Users\ai-do\repo\dx",
            Profiles = new List<Profile> { new Profile { Id = "p1", Label = "Dev API :4200" } },
            Stacks = new List<StackDef> { new StackDef { Id = "s1", Label = "Dev toàn bộ" } },
        };

        [Fact]
        public void SetupWorkspace_NhacDungCongCuMcpVaTenWorkspace()
        {
            var text = AgentPrompts.SetupWorkspace(Ws(), new[] { ".NET", "git" });
            Assert.Contains("DxFactory", text);
            Assert.Contains("w1", text);
            Assert.Contains(".NET, git", text);
            foreach (var tool in new[] { "`overview`", "`search`", "`save`", "`attach`" })
                Assert.Contains(tool, text);
            Assert.DoesNotContain("ai-do", text); // không lộ đường dẫn máy
        }

        [Fact]
        public void ReadToolCommands_CoLenhGocVaThuMucTuongDoi()
        {
            var text = AgentPrompts.ReadToolCommands(Ws(), "Angular CLI", "ng", "web/app");
            Assert.Contains("Angular CLI", text);
            Assert.Contains("Lệnh gốc: ng", text);
            Assert.Contains("web/app", text);
            Assert.Contains("--help", text);
            Assert.Contains(".", AgentPrompts.ReadToolCommands(Ws(), "Git", null, null)); // thiếu thông tin vẫn dựng được
        }

        [Fact]
        public void BuildWorkflow_GomMoTaVaProfileDangCo()
        {
            var text = AgentPrompts.BuildWorkflow(Ws(), "bật API rồi chờ cổng 5000 rồi bật web");
            Assert.Contains("bật API rồi chờ cổng 5000", text);
            Assert.Contains("Dev API :4200", text);
            Assert.Contains("Dev toàn bộ", text);
            Assert.Contains("type=stack", text);
            Assert.Contains("hỏi lại", AgentPrompts.BuildWorkflow(Ws(), "  ")); // chưa mô tả thì bảo agent hỏi lại
        }

        [Fact]
        public void DiagnoseFailure_Cuoi40DongKhongDuongDanTuyetDoi()
        {
            var log = Enumerable.Range(1, 80).Select(i => "dòng " + i).ToList();
            var text = AgentPrompts.DiagnoseFailure(Ws(), "Dev API", "dotnet run", @"C:\Users\ai-do\repo\dx\api", 1, log);
            Assert.Contains("dotnet run", text);
            Assert.Contains("api", text);
            Assert.Contains("dòng 80", text);
            Assert.DoesNotContain("dòng 40\r", text);
            Assert.DoesNotContain("ai-do", text);
            Assert.Contains("`logs`", text);
        }

        [Fact]
        public void AgentAsk_ChuaCoAgent_KhongSaoChep_VaDanToiKetNoi()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var host = new Toolbox.Views.ToastHost();
                var previous = Toolbox.Views.Toasts.Host;
                Toolbox.Views.Toasts.Host = host;
                try
                {
                    var connected = 0;
                    var copied = Toolbox.Views.AgentAsk.Send("prompt", () => connected++, () => false);
                    Assert.False(copied);
                    Assert.Equal(1, host.Count);
                    Assert.Equal(0, connected); // chỉ hiện nút "Kết nối agent", chưa tự mở
                }
                finally
                {
                    Toolbox.Views.Toasts.Host = previous;
                }
            });
        }
    }
}
