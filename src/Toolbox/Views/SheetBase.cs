using System;
using System.Windows;
using System.Windows.Controls;

namespace Toolbox.Views
{
    /// <summary>
    /// Nền chung cho nội dung hiện trong <see cref="SideSheet"/> (thay cho cửa sổ modal): giữ cách dùng quen của hộp thoại
    /// (<see cref="DialogResult"/> = true để xác nhận, <see cref="Close"/> để huỷ) nên mã bên trong gần như không đổi.
    /// </summary>
    public abstract class SheetBase : UserControl
    {
        private bool _completed;

        /// <summary>Tiêu đề hiện ở đầu sheet.</summary>
        public string SheetTitle { get; protected set; } = "";

        /// <summary>Bề rộng sheet (điểm ảnh).</summary>
        public virtual double SheetWidth => 500;

        /// <summary>Báo sheet đã xong: true nếu xác nhận, false nếu huỷ.</summary>
        public event Action<bool> Completed;

        /// <summary>Đặt true để xác nhận (đóng sheet và gọi bên gọi), false để huỷ.</summary>
        public bool? DialogResult
        {
            set => Complete(value == true);
        }

        /// <summary>Huỷ và đóng sheet.</summary>
        public void Close() => Complete(false);

        private void Complete(bool ok)
        {
            if (_completed)
                return;
            _completed = true;
            Completed?.Invoke(ok);
        }

        /// <summary>Cửa sổ chứa sheet, dùng làm chủ cho hộp thoại và thông báo lỗi bên trong.</summary>
        protected Window Host => Window.GetWindow(this);
    }
}
