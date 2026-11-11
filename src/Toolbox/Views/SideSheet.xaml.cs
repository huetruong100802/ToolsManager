using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toolbox.Views
{
    /// <summary>
    /// Khung trượt bên phải cho việc sửa trong ngữ cảnh (profile mới, tham số, lệnh của tool), thay cho chồng cửa sổ modal.
    /// Chỉ một sheet mở một lúc; mở sheet khác thì sheet cũ được huỷ. Esc đóng sheet.
    /// </summary>
    public partial class SideSheet : UserControl
    {
        private SheetBase _current;
        private Action<bool> _done;

        public SideSheet()
        {
            InitializeComponent();
            PreviewKeyDown += OnKeyDown;
        }

        public bool IsOpen => _current != null;

        public SheetBase Current => _current;

        /// <summary>Mở <paramref name="sheet"/>; <paramref name="done"/> chạy khi xong (true: xác nhận, false: huỷ hoặc bị thay).</summary>
        public void Open(SheetBase sheet, Action<bool> done)
        {
            if (_current != null)
                Finish(false);
            _current = sheet;
            _done = done;
            Width = Math.Min(sheet.SheetWidth, Math.Max(360, (Window.GetWindow(this)?.ActualWidth ?? 1000) - 260));
            TitleText.Text = sheet.SheetTitle;
            Body.Content = sheet;
            sheet.Completed += OnSheetCompleted;
            Visibility = Visibility.Visible;
            sheet.Focus();
        }

        /// <summary>Đóng sheet đang mở như huỷ. Trả về true nếu có sheet vừa được đóng.</summary>
        public bool CloseCurrent()
        {
            if (_current == null)
                return false;
            Finish(false);
            return true;
        }

        private void OnSheetCompleted(bool ok)
        {
            // Sheet gọi DialogResult ngay trong sự kiện bấm nút của chính nó: đóng sau khi sự kiện đó xong cho an toàn.
            Dispatcher.BeginInvoke(new Action(() => Finish(ok)), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void Finish(bool ok)
        {
            var sheet = _current;
            var done = _done;
            if (sheet == null)
                return;
            _current = null;
            _done = null;
            sheet.Completed -= OnSheetCompleted;
            Body.Content = null;
            Visibility = Visibility.Collapsed;
            done?.Invoke(ok);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => CloseCurrent();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && IsOpen)
            {
                CloseCurrent();
                e.Handled = true;
            }
        }
    }
}
