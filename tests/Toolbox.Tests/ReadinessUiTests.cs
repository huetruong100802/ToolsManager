using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    public class ReadinessUiTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        [Fact]
        public void Panel_AnKhiSanSang_HienKhiCoViec_VaBaoNutSua()
        {
            Sta(() =>
            {
                var panel = new ReadinessPanel();
                panel.Show(new List<ReadinessIssue>());
                Assert.Equal(Visibility.Collapsed, panel.Visibility);

                var issues = new List<ReadinessIssue>
                {
                    new ReadinessIssue { Level = ReadinessLevel.Warning, Title = "Profile hỏng", Fix = ReadinessFix.OpenProfile, FixText = "Mở profile", TargetId = "p1" },
                    new ReadinessIssue { Level = ReadinessLevel.Error, Title = "Mất đường dẫn", Fix = ReadinessFix.EditTool, FixText = "Sửa đường dẫn", TargetId = "t1" },
                };
                panel.Show(issues);
                Assert.Equal(Visibility.Visible, panel.Visibility);
                Assert.Equal(2, panel.Count);
                Assert.Contains("1 chặn", ((System.Windows.Controls.TextBlock)panel.FindName("Heading")).Text);
                var rows = ((IEnumerable)((System.Windows.Controls.ItemsControl)panel.FindName("List")).ItemsSource).Cast<ReadinessRow>().ToList();
                Assert.Equal("Mất đường dẫn", rows[0].Title); // lỗi chặn lên trước
                Assert.Equal(Visibility.Visible, rows[0].FixVisibility);
            });
        }

        [Fact]
        public void WorkspaceView_KhungChuaSanSangHienOTabChay()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var ws = new Workspace { Id = "w", Name = "W", Path = dir.Combine("khong-co") }; // thư mục không có
                    var view = new WorkspaceView();
                    view.Initialize(new TaskHost(dir.Combine("runs")), () => true);
                    view.Load(ws);
                    var panel = (ReadinessPanel)view.FindName("ReadyPanel");
                    Assert.True(panel.Count >= 1);
                    Assert.Equal(Visibility.Visible, panel.Visibility);
                }
            });
        }

        [Fact]
        public void TrangChu_WorkspaceChuaSanSang_ThanhViecCanXuLy()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("repo");
                Directory.CreateDirectory(path);
                var ws = new List<Workspace> { new Workspace { Id = "w1", Name = "DxFactory", Path = path }, new Workspace { Id = "w2", Name = "Ổn", Path = path } };
                var issues = new Dictionary<string, int> { ["w1"] = 3, ["w2"] = 0 };
                var feed = HomeFeed.Build(ws, new List<ToolEntry>(), new List<TaskInfo>(), 0, true, null, null, DateTime.UtcNow, p => true, 0, issues);
                var item = feed.Attention.Single();
                Assert.Equal(HomeTarget.Workspace, item.Target);
                Assert.Equal("w1", item.WorkspaceId);
                Assert.Contains("3 việc", item.Title);
            }
        }
    }
}
