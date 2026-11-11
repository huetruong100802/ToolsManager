using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Sheet bên phải thay cho hộp thoại modal: mở, xác nhận, huỷ, bị thay; nội dung dựng thật với style của app.</summary>
    public class SideSheetTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        /// <summary>Sheet hoàn tất bằng BeginInvoke: chạy hàng đợi của dispatcher cho tới khi xong.</summary>
        private static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        [Fact]
        public void Open_XacNhan_GoiBenGoiVoiTrue_VaDongSheet()
        {
            Sta(() =>
            {
                var host = new SideSheet();
                var sheet = new PairsSheet("Dev API", new List<ParamPair> { new ParamPair { Key = "--port", Value = "4200" } }, new CapabilityEntry[0], null);
                bool? result = null;
                host.Open(sheet, ok => result = ok);
                Assert.True(host.IsOpen);
                Assert.Equal(Visibility.Visible, host.Visibility);

                ((System.Windows.Controls.Button)sheet.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump();
                Assert.True(result);
                Assert.False(host.IsOpen);
                Assert.Equal(Visibility.Collapsed, host.Visibility);
                Assert.Equal("--port", sheet.Result.Single().Key);
            });
        }

        [Fact]
        public void Close_VaMoSheetKhac_HuyCaiCu()
        {
            Sta(() =>
            {
                var host = new SideSheet();
                var first = new PairsSheet("A", new List<ParamPair>(), new CapabilityEntry[0], null);
                var second = new PairsSheet("B", new List<ParamPair>(), new CapabilityEntry[0], null);
                var log = new List<string>();
                host.Open(first, ok => log.Add("a:" + ok));
                host.Open(second, ok => log.Add("b:" + ok)); // thay: a bị huỷ ngay
                Assert.Equal(new[] { "a:False" }, log);
                Assert.Same(second, host.Current);
                Assert.True(host.CloseCurrent());
                Assert.Equal(new[] { "a:False", "b:False" }, log);
                Assert.False(host.CloseCurrent());
            });
        }

        [Fact]
        public void ProfileSheet_DungDuocTrongSheet_VaLenhSeChayLuonHien()
        {
            Sta(() =>
            {
                var workspace = new Workspace { Id = "w", Name = "W", Path = System.IO.Path.GetTempPath() };
                var sheet = new ProfileSheet(workspace, () => new Dictionary<int, string>());
                var host = new SideSheet();
                host.Open(sheet, ok => { });
                var preview = (System.Windows.Controls.TextBlock)sheet.FindName("PreviewText");
                Assert.NotNull(preview);
                Assert.False(string.IsNullOrWhiteSpace(preview.Text)); // bước 1 đã có chỗ báo, không để trống
                Assert.Equal("Ghim lệnh", sheet.SheetTitle);
                host.CloseCurrent();
            });
        }

        [Fact]
        public void ChiMotLanHoanTat_DialogResultLapLaiKhongGoiHaiLan()
        {
            Sta(() =>
            {
                var host = new SideSheet();
                var sheet = new PairsSheet("A", new List<ParamPair>(), new CapabilityEntry[0], null);
                var calls = 0;
                host.Open(sheet, ok => calls++);
                sheet.DialogResult = true;
                sheet.DialogResult = false;
                sheet.Close();
                Pump();
                Assert.Equal(1, calls);
            });
        }
    }
}
