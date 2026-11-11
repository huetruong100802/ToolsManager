using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Chia sẻ và Nhận gói một luồng: cùng một câu "người nhận sẽ có gì", và nhận gói chọn workspace đích.</summary>
    public class ShareFlowTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        [Fact]
        public void Counts_BoQuaPhanBang0_VaNoiRoKhiTrong()
        {
            Assert.Equal("1 workflow · 2 profile · 3 tool · 1 lệnh riêng", ShareSummary.Counts(3, 2, 1, 1));
            Assert.Equal("2 tool", ShareSummary.Counts(2, 0, 0, 0));
            Assert.Equal("Chưa có gì để chia sẻ.", ShareSummary.Counts(0, 0, 0, 0));
            Assert.StartsWith("Người nhận sẽ có: ", ShareSummary.Header(1, 0, 0, 0));
        }

        [Theory]
        [InlineData(500L, "500 B")]
        [InlineData(2048L, "2 KB")]
        [InlineData(5L * 1024 * 1024, "5 MB")]
        public void Format_DonViDungChoDungLuong(long bytes, string expected) => Assert.Equal(expected, ShareSummary.Format(bytes));

        [Fact]
        public void ShareWindow_HienDongVaCanhBao()
        {
            Sta(() =>
            {
                var window = new ShareWindow("Người nhận sẽ có: 1 tool", new[] { "• A — 3 file, 1 KB" }, new[] { "A lấy cả thư mục rộng" });
                Assert.Equal("Người nhận sẽ có: 1 tool", ((TextBlock)window.FindName("HeaderText")).Text);
                Assert.Contains("A — 3 file", ((TextBlock)window.FindName("Lines")).Text);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WarningBox")).Visibility);
                var clean = new ShareWindow("x", new string[0], new string[0]);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)clean.FindName("WarningBox")).Visibility);
            });
        }

        private static PackManifest Manifest(bool withWorkspace)
        {
            var manifest = new PackManifest { Tools = new List<ToolEntry> { new ToolEntry { Id = "a", Name = "A", Command = "a" } } };
            if (withWorkspace)
                manifest.Workspace = new WorkspacePackData
                {
                    Profiles = new List<Profile> { new Profile { Id = "p1", Label = "Dev" } },
                    Stacks = new List<StackDef> { new StackDef { Id = "s1", Label = "Cả bộ" } },
                };
            return manifest;
        }

        [Fact]
        public void ImportWindow_GoiCoProfile_HoiWorkspaceDich()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var store = ToolStore.Load(dir.Combine("tools.json"));
                    var workspaces = new[] { new Workspace { Id = "w1", Name = "Một", Path = dir.Path }, new Workspace { Id = "w2", Name = "Hai", Path = dir.Path } };
                    var window = new ImportWindow(Manifest(withWorkspace: true), store, workspaces);
                    Assert.True(window.HasWorkspaceContent);
                    Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("TargetPanel")).Visibility);
                    var combo = (ComboBox)window.FindName("TargetCombo");
                    Assert.Equal(3, combo.Items.Count); // 2 workspace + "Tạo workspace mới…"
                    Assert.Equal(0, combo.SelectedIndex);
                    Assert.Contains("1 workflow · 1 profile", ((TextBlock)window.FindName("TargetHint")).Text);
                }
            });
        }

        [Fact]
        public void ImportWindow_GoiChiCoTool_KhongHoiWorkspace()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var window = new ImportWindow(Manifest(withWorkspace: false), ToolStore.Load(dir.Combine("tools.json")), new Workspace[0]);
                    Assert.False(window.HasWorkspaceContent);
                    Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("TargetPanel")).Visibility);
                }
            });
        }

        [Fact]
        public void ImportWindow_ChuaCoWorkspace_PhaiChonThuMucDeTao()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var window = new ImportWindow(Manifest(withWorkspace: true), ToolStore.Load(dir.Combine("tools.json")), new Workspace[0]);
                    var combo = (ComboBox)window.FindName("TargetCombo");
                    Assert.Single(combo.Items);
                    Assert.Contains("Tạo workspace mới", (string)combo.Items[0]);
                    Assert.Contains("Chưa có workspace nào", ((TextBlock)window.FindName("TargetHint")).Text);
                }
            });
        }
    }
}
