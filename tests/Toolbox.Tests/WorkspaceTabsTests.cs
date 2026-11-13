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
    /// <summary>Trang workspace ba tab cố định: Chạy (mọi lệnh, gồm lệnh của tool), Workflow, Chia sẻ.</summary>
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
        public void BonTabCoDinh_LenhCuaToolNamTrongTabChay()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 8);
                    Assert.Equal(new[] { "Chạy", "Workflow", "Chia sẻ", "Biến" }, Titles(view, "TabStrip"));
                }
            });
        }

        [Fact]
        public void CycleTab_QuayVongBonTab()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 3);
                    var tabs = (ItemsControl)view.FindName("TabStrip");
                    Func<TabRow[]> rows = () => ((IEnumerable)tabs.ItemsSource).Cast<TabRow>().ToArray();
                    Assert.True(rows().First().IsSelected);

                    view.CycleTab(1); // → Workflow
                    Assert.True(rows()[1].IsSelected);
                    view.CycleTab(1); // → Chia sẻ
                    view.CycleTab(1); // → Biến
                    Assert.True(rows().Last().IsSelected);
                    view.CycleTab(1); // quay vòng về Chạy
                    Assert.True(rows().First().IsSelected);

                    view.CycleTab(-1); // lùi từ Chạy → Biến
                    Assert.True(rows().Last().IsSelected);
                }
            });
        }

        [Fact]
        public void WorkspaceKhongCoTool_VanDungDuocTrang()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = Make(dir, 0);
                    Assert.Equal(4, Titles(view, "TabStrip").Length);
                    view.CycleTab(1);
                    view.CycleTab(-1);
                }
            });
        }
    }
}
