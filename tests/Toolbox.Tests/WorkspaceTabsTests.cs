using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Trang workspace bốn tab cố định; tab Tool là danh sách bên trái + chi tiết bên phải, tám tool không cần cuộn ngang.</summary>
    public class WorkspaceTabsTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        private static WorkspaceView Make(TempDir dir, int tools)
        {
            var ws = new Workspace { Id = "w", Name = "W", Path = dir.Path };
            for (var i = 0; i < tools; i++)
                ws.Bindings.Add(new ToolBinding { ToolId = "tool-" + i });
            var view = new WorkspaceView();
            view.Initialize(new TaskHost(dir.Combine("runs")), () => true);
            view.Load(ws);
            return view;
        }

        private static string[] Titles(WorkspaceView view, string list) =>
            ((IEnumerable)((ItemsControl)view.FindName(list)).ItemsSource).Cast<TabRow>().Select(t => t.Title).ToArray();

        [Fact]
        public void BonTabCoDinh_DuTamTool()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 8);
                    Assert.Equal(new[] { "Chạy", "Tool", "Workflow", "Chia sẻ" }, Titles(view, "TabStrip"));
                    // 8 tool + "Lệnh riêng" nằm trong danh sách bên trái, không thành tab.
                    Assert.Equal(9, Titles(view, "ToolNav").Length);
                    Assert.Equal("Lệnh riêng", Titles(view, "ToolNav").Last());
                }
            });
        }

        [Fact]
        public void TabTool_HienDanhSachBenTrai_TabKhacAnDi()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 3);
                    var panel = (FrameworkElement)view.FindName("ToolNavPanel");
                    Assert.Equal(Visibility.Collapsed, panel.Visibility); // mặc định mở tab Chạy

                    view.CycleTab(1); // Chạy → Tool
                    Assert.Equal(Visibility.Visible, panel.Visibility);
                    view.CycleTab(1); // → Workflow
                    Assert.Equal(Visibility.Collapsed, panel.Visibility);
                    view.CycleTab(1); // → Chia sẻ
                    view.CycleTab(1); // quay vòng về Chạy
                    Assert.Equal(Visibility.Collapsed, panel.Visibility);
                    Assert.True(((IEnumerable)((ItemsControl)view.FindName("TabStrip")).ItemsSource).Cast<TabRow>().First().IsSelected);

                    view.CycleTab(-1); // lùi từ Chạy → Chia sẻ
                    Assert.True(((IEnumerable)((ItemsControl)view.FindName("TabStrip")).ItemsSource).Cast<TabRow>().Last().IsSelected);
                }
            });
        }

        [Fact]
        public void WorkspaceKhongCoTool_VanCoLenhRieng()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 0);
                    Assert.Equal(new[] { "Lệnh riêng" }, Titles(view, "ToolNav"));
                    view.CycleTab(1); // vào tab Tool không lỗi dù chưa có tool nào
                    Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("ToolNavPanel")).Visibility);
                }
            });
        }
    }
}
