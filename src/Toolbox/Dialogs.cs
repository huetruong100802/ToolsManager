using System.Windows;

namespace Toolbox
{
    internal static class Dialogs
    {
        public static void Error(Window owner, string message) =>
            MessageBox.Show(owner, message, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);

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
