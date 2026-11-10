using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Toolbox.Views
{
    public enum ToastKind { Info, Success, Error }

    /// <summary>Một thông báo nổi. Hết <see cref="Seconds"/> thì tự đóng; có nút hành động (vd. Hoàn tác) thì bấm là đóng luôn.</summary>
    public sealed class ToastItem
    {
        public string Message { get; set; }
        public ToastKind Kind { get; set; }
        public string ActionText { get; set; }
        public Action Action { get; set; }
        public double Seconds { get; set; }
        public Visibility ActionVisibility => string.IsNullOrEmpty(ActionText) ? Visibility.Collapsed : Visibility.Visible;
        internal DispatcherTimer Timer { get; set; }
    }

    /// <summary>
    /// Thông báo phản hồi tại chỗ thay cho hộp thoại: thành công, lỗi nhẹ, và "Đã làm X · Hoàn tác". Xếp chồng ở góc phải dưới,
    /// tối đa <see cref="MaxToasts"/> cái cùng lúc. Thông báo lỗi ở lại lâu hơn.
    /// </summary>
    public partial class ToastHost : UserControl
    {
        public const int MaxToasts = 4;
        private readonly ObservableCollection<ToastItem> _items = new ObservableCollection<ToastItem>();

        public ToastHost()
        {
            InitializeComponent();
            List.ItemsSource = _items;
        }

        /// <summary>Số thông báo đang hiện.</summary>
        public int Count => _items.Count;

        public ToastItem Show(string message, ToastKind kind = ToastKind.Info, string actionText = null, Action action = null, double? seconds = null)
        {
            var item = new ToastItem
            {
                Message = message,
                Kind = kind,
                ActionText = actionText,
                Action = action,
                Seconds = seconds ?? (kind == ToastKind.Error ? 12 : action != null ? 8 : 4.5),
            };
            while (_items.Count >= MaxToasts)
                Dismiss(_items[0]);
            _items.Add(item);
            item.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(item.Seconds) };
            item.Timer.Tick += (s, e) => Dismiss(item);
            item.Timer.Start();
            return item;
        }

        public void Dismiss(ToastItem item)
        {
            if (item == null || !_items.Remove(item))
                return;
            item.Timer?.Stop();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Dismiss((sender as FrameworkElement)?.Tag as ToastItem);

        private void OnAction(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as ToastItem;
            Dismiss(item);
            item?.Action?.Invoke();
        }
    }

    /// <summary>Điểm gọi toàn cục: cửa sổ chính đăng ký host; chưa có host (test, cửa sổ riêng) thì <see cref="Show"/> trả false.</summary>
    internal static class Toasts
    {
        public static ToastHost Host { get; set; }

        public static bool Show(string message, ToastKind kind = ToastKind.Info, string actionText = null, Action action = null, double? seconds = null)
        {
            var host = Host;
            if (host == null)
                return false;
            if (host.Dispatcher.CheckAccess())
                host.Show(message, kind, actionText, action, seconds);
            else
                host.Dispatcher.BeginInvoke(new Action(() => host.Show(message, kind, actionText, action, seconds)));
            return true;
        }
    }
}
