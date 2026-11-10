using System;
using System.Linq;
using System.Windows.Controls;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Thông báo nổi và trạng thái trống: dựng thật với style của app (luồng STA).</summary>
    public class ToastTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        [Fact]
        public void Show_ThemRoiDong_VaGioiHanSoThongBao()
        {
            Sta(() =>
            {
                var host = new ToastHost();
                var first = host.Show("một");
                Assert.Equal(1, host.Count);
                for (var i = 0; i < 10; i++)
                    host.Show("thông báo " + i);
                Assert.Equal(ToastHost.MaxToasts, host.Count);
                host.Dismiss(first); // đã bị đẩy ra khi quá giới hạn: đóng lại không lỗi
                Assert.Equal(ToastHost.MaxToasts, host.Count);
            });
        }

        [Fact]
        public void NutHanhDong_ChayMotLanVaDongThongBao()
        {
            Sta(() =>
            {
                var host = new ToastHost();
                var calls = 0;
                var toast = host.Show("Đã xoá", ToastKind.Success, "Hoàn tác", () => calls++);
                Assert.Equal(System.Windows.Visibility.Visible, toast.ActionVisibility);
                Assert.Equal(8, toast.Seconds); // có Hoàn tác thì ở lại 8 giây
                var list = (ItemsControl)host.FindName("List");
                Assert.NotNull(list);

                host.Dismiss(toast);
                host.Dismiss(toast);
                Assert.Equal(0, host.Count);
                Assert.Equal(0, calls);
            });
        }

        [Fact]
        public void LoiO_LaiLauHonThongBaoThuong()
        {
            Sta(() =>
            {
                var host = new ToastHost();
                var error = host.Show("lỗi", ToastKind.Error);
                var info = host.Show("ok");
                Assert.True(error.Seconds > info.Seconds);
                Assert.Equal(System.Windows.Visibility.Collapsed, info.ActionVisibility);
            });
        }

        [Fact]
        public void Toasts_KhongCoHost_TraFalse_CoHost_ToiHost()
        {
            Sta(() =>
            {
                var previous = Toasts.Host;
                try
                {
                    Toasts.Host = null;
                    Assert.False(Toasts.Show("x"));
                    var host = new ToastHost();
                    Toasts.Host = host;
                    Assert.True(Toasts.Show("x", ToastKind.Success));
                    Assert.Equal(1, host.Count);
                }
                finally
                {
                    Toasts.Host = previous;
                }
            });
        }

        [Fact]
        public void EmptyState_NutDauLaNutChinh_VaBamDuocChay()
        {
            Sta(() =>
            {
                var state = new EmptyState { Title = "Chưa có gì", Body = "Làm việc đầu tiên." };
                var clicked = "";
                var primary = state.AddAction("Làm ngay", () => clicked += "a");
                var secondary = state.AddAction("Để sau", () => clicked += "b");
                Assert.NotSame(primary.Style, secondary.Style);
                primary.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                secondary.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("ab", clicked);
                Assert.Equal("Chưa có gì", ((TextBlock)state.FindName("TitleText")).Text);
            });
        }
    }
}
