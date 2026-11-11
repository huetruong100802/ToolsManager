using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Giữ app nhẹ: danh sách dài ảo hoá, đồng hồ chỉ chạy khi trang đang hiện.</summary>
    public class LightweightTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        [Fact]
        public void DanhSachTacVu_VaThuVien_AoHoa()
        {
            Sta(() =>
            {
                var tasks = new TasksView();
                var list = (ItemsControl)tasks.FindName("TaskList");
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                var panel = list.ItemsPanel.LoadContent();
                Assert.IsType<VirtualizingStackPanel>(panel);

                var library = new LibraryView();
                var toolList = (ListBox)library.FindName("ToolList");
                Assert.True(VirtualizingPanel.GetIsVirtualizing(toolList));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(toolList));
            });
        }

        [Fact]
        public void TasksView_DongHoChiChayKhiTrangHien()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = new TasksView();
                    view.Initialize(new TaskHost(dir.Combine("runs")), new System.Collections.ObjectModel.ObservableCollection<WorkspaceRow>());
                    var timer = (System.Windows.Threading.DispatcherTimer)typeof(TasksView)
                        .GetField("_timer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
                    Assert.False(timer.IsEnabled); // chưa hiện thì không chạy
                }
            });
        }
    }
}
