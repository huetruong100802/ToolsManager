using System;
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
    /// <summary>Trang chủ dựng thật (STA): nhóm hiện/ẩn đúng, bấm dòng báo đúng mục, dòng không dựng lại khi chỉ đổi thời gian.</summary>
    public class HomeViewTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        private static HomeFeed Feed(TempDir dir, int pending = 0, bool tool = true, bool agent = true, IReadOnlyList<TaskInfo> tasks = null)
        {
            var path = dir.Combine("repo");
            Directory.CreateDirectory(path);
            var ws = new List<Workspace> { new Workspace { Id = "w1", Name = "Repo", Path = path } };
            var tools = tool ? new List<ToolEntry> { new ToolEntry { Id = "a", Name = "A", Command = "a" } } : new List<ToolEntry>();
            return HomeFeed.Build(ws, tools, tasks ?? new List<TaskInfo>(), pending, agent, null, null, Now, p => true);
        }

        [Fact]
        public void Show_NhomChiHienKhiCoDong_VaTrangThaiYenOnKhiKhongCoGi()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = new OverviewView();
                    view.Show(Feed(dir), onboardingDismissed: true);
                    Assert.Equal(0, view.Visible);
                    Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("AllQuiet")).Visibility);
                    Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("AttentionSection")).Visibility);

                    view.Show(Feed(dir, pending: 2), onboardingDismissed: true);
                    Assert.Equal(1, view.Visible);
                    Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("AttentionSection")).Visibility);
                    Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("AllQuiet")).Visibility);
                }
            });
        }

        [Fact]
        public void Show_LamQuenHienKhiChuaXongVaChuaAn_RoiAnKhiXong()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var view = new OverviewView();
                    var card = (FrameworkElement)view.FindName("OnboardingCard");
                    view.Show(Feed(dir, agent: false), onboardingDismissed: false);
                    Assert.Equal(Visibility.Visible, card.Visibility);
                    view.Show(Feed(dir, agent: false), onboardingDismissed: true);
                    Assert.Equal(Visibility.Collapsed, card.Visibility);
                    view.Show(Feed(dir, agent: true), onboardingDismissed: false);
                    Assert.Equal(Visibility.Collapsed, card.Visibility); // đủ ba bước thì tự ẩn
                }
            });
        }

        [Fact]
        public void Show_CungDanhSach_GiuNguyenDongChiDoiChiTiet()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var task = new TaskInfo("t", new TaskSpec { WorkspaceId = "w1", Label = "Dev API", CommandLine = "x", Port = 4200, Key = "k" }, null);
                    var view = new OverviewView();
                    view.Show(Feed(dir, tasks: new[] { task }), onboardingDismissed: true);
                    var list = (System.Windows.Controls.ItemsControl)view.FindName("RunningList");
                    var rows = ((IEnumerable<HomeRow>)list.ItemsSource).ToList();
                    Assert.Single(rows);
                    Assert.Equal("Mở :4200", rows[0].ActionText);

                    view.Show(HomeFeed.Build(new List<Workspace> { new Workspace { Id = "w1", Name = "Repo", Path = dir.Path } }, new List<ToolEntry>(),
                        new[] { task }, 0, true, null, null, Now.AddMinutes(5), p => true), onboardingDismissed: true);
                    var again = ((IEnumerable<HomeRow>)list.ItemsSource).ToList();
                    Assert.Same(rows[0], again[0]); // không dựng lại dòng
                }
            });
        }

        [Fact]
        public void BamNutChinhVaPhu_BaoDungMuc()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var task = new TaskInfo("t", new TaskSpec { WorkspaceId = "w1", Label = "Dev API", CommandLine = "x", Port = 4200, Key = "k" }, null);
                    var view = new OverviewView();
                    var seen = new List<string>();
                    view.ItemActivated += (item, secondary) => seen.Add(item.Target + (secondary ? ":phu" : ":chinh"));
                    view.Show(Feed(dir, tasks: new[] { task }), onboardingDismissed: true);
                    var list = (System.Windows.Controls.ItemsControl)view.FindName("RunningList");
                    var row = ((IEnumerable<HomeRow>)list.ItemsSource).Single();
                    Assert.Equal(Visibility.Visible, row.SecondaryVisibility);
                    Assert.Equal("Xem log", row.SecondaryText);
                }
            });
        }
    }
}
