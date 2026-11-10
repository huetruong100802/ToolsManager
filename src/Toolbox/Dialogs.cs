using System.Windows;

namespace Toolbox
{
    internal static class Dialogs
    {
        /// <summary>
        /// Báo lỗi. Từ cửa sổ chính thì hiện thông báo nổi tại chỗ (không chặn thao tác); từ hộp thoại con thì vẫn dùng MessageBox
        /// vì thông báo nổi nằm sau hộp thoại. Lỗi buộc người dùng đọc rồi mới đi tiếp (file dữ liệu hỏng) dùng <see cref="Fatal"/>.
        /// </summary>
        public static void Error(Window owner, string message)
        {
            if (owner is MainWindow && Views.Toasts.Show(message, Views.ToastKind.Error))
                return;
            Fatal(owner, message);
        }

        /// <summary>Lỗi phải đọc xong mới đi tiếp: luôn là hộp thoại.</summary>
        public static void Fatal(Window owner, string message) =>
            MessageBox.Show(owner, message, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);

        /// <summary>Báo kết quả thành công, kèm nút hành động (vd. Hoàn tác) nếu có. Không có thông báo nổi thì bỏ qua lặng lẽ.</summary>
        public static void Success(string message, string actionText = null, System.Action action = null, double? seconds = null) =>
            Views.Toasts.Show(message, Views.ToastKind.Success, actionText, action, seconds);

        public static void Info(string message) => Views.Toasts.Show(message);

        public static bool Confirm(Window owner, string message, bool warning = false) =>
            MessageBox.Show(owner, message, "Toolbox", MessageBoxButton.YesNo,
                warning ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;

        /// <summary>Có/Không/Huỷ. Trả về true (Yes), false (No) hoặc null (Cancel).</summary>
        public static bool? Choose(Window owner, string message)
        {
            var answer = MessageBox.Show(owner, message, "Toolbox", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
                return true;
            return answer == MessageBoxResult.No ? (bool?)false : null;
        }
    }

    internal static class Format
    {
        public static string Size(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
        }
    }
}
