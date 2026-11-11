using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Form thêm tool hai mức: phần nâng cao gập lại, tự mở khi cần, lỗi hiện cạnh ô sai.</summary>
    public class ToolFormTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        private static void Save(ToolEditWindow window) =>
            typeof(ToolEditWindow).GetMethod("OnSave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { window, new RoutedEventArgs() });

        private static T Find<T>(ToolEditWindow w, string name) where T : class => (T)w.FindName(name);

        [Fact]
        public void ToolMoi_NangCaoGapLai_ToolCoGiaTriNangCao_TuMo()
        {
            Sta(() =>
            {
                var fresh = new ToolEditWindow(new ToolEntry(), isNew: true);
                Assert.False(Find<Expander>(fresh, "Advanced").IsExpanded);

                var withArgs = new ToolEditWindow(new ToolEntry { Name = "A", Path = @"C:\a.exe", Args = "--x" }, isNew: false);
                Assert.True(Find<Expander>(withArgs, "Advanced").IsExpanded);

                var withHelp = new ToolEditWindow(new ToolEntry { Name = "A", Command = "git", HelpTemplate = "{base} help" }, isNew: false);
                Assert.True(Find<Expander>(withHelp, "Advanced").IsExpanded);
            });
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        public void HasAdvancedValues_ToolFileMacDinhKhongCan(bool command, bool expected)
        {
            var entry = command ? new ToolEntry { Name = "A", Command = "git", Pack = PackMode.None } : new ToolEntry { Name = "A", Path = @"C:\a.exe", Pack = PackMode.Folder };
            Assert.Equal(expected, ToolEditWindow.HasAdvancedValues(entry));
            Assert.True(ToolEditWindow.HasAdvancedValues(new ToolEntry { Name = "A", Path = @"C:\a.exe", Pack = PackMode.File }));
        }

        [Fact]
        public void LuuThieuTenVaDuongDan_LoiHienCanhO_KhongBatHopThoai()
        {
            Sta(() =>
            {
                var window = new ToolEditWindow(new ToolEntry(), isNew: true);
                Save(window);
                Assert.Equal(Visibility.Visible, Find<TextBlock>(window, "NameError").Visibility);
                Assert.Equal(Visibility.Visible, Find<TextBlock>(window, "PathError").Visibility);

                Find<TextBox>(window, "NameBox").Text = "Tool thử"; // gõ vào ô thì lỗi tự tắt
                Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "NameError").Visibility);
            });
        }

        [Fact]
        public void LenhVoiDuongDanLaFile_BaoLoiCanhODuongDan()
        {
            Sta(() =>
            {
                var file = Path.GetTempFileName();
                try
                {
                    var window = new ToolEditWindow(new ToolEntry(), isNew: true);
                    Find<TextBox>(window, "NameBox").Text = "Tool";
                    Find<TextBox>(window, "PathBox").Text = file;
                    Find<TextBox>(window, "CommandBox").Text = "git status";
                    Save(window);
                    var error = Find<TextBlock>(window, "PathError");
                    Assert.Equal(Visibility.Visible, error.Visibility);
                    Assert.Contains("thư mục", error.Text);
                }
                finally
                {
                    File.Delete(file);
                }
            });
        }

        [Fact]
        public void DuongDanKhongCo_NhacMotLan_KhongChanLuu()
        {
            Sta(() =>
            {
                var window = new ToolEditWindow(new ToolEntry(), isNew: true);
                Find<TextBox>(window, "NameBox").Text = "Tool";
                Find<TextBox>(window, "PathBox").Text = @"Z:\khong\co\tool.exe";
                Save(window); // lần 1: nhắc, chưa lưu
                var error = Find<TextBlock>(window, "PathError");
                Assert.Equal(Visibility.Visible, error.Visibility);
                Assert.Contains("Bấm Lưu lần nữa", error.Text);
            });
        }

        [Fact]
        public void DescribePath_NoiRoThuMucHayFile()
        {
            var dir = Path.GetTempPath();
            Assert.Contains("thư mục", ToolEditWindow.DescribePath(dir, hasCommand: false));
            Assert.Contains("lệnh ở dưới sẽ chạy", ToolEditWindow.DescribePath(dir, hasCommand: true));
            var file = Path.GetTempFileName();
            try
            {
                Assert.Contains("file chạy", ToolEditWindow.DescribePath(file, hasCommand: false));
            }
            finally
            {
                File.Delete(file);
            }
            Assert.Contains("Chưa thấy", ToolEditWindow.DescribePath(@"Z:\khong\co", false));
        }
    }
}
