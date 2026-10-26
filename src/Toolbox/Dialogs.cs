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
