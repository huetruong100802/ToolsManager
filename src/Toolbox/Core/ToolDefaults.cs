using System;
using System.Diagnostics;
using System.IO;

namespace Toolbox.Core
{
    /// <summary>Gợi ý giá trị khi đăng ký tool từ một đường dẫn.</summary>
    public static class ToolDefaults
    {
        public static ToolEntry FromPath(string path)
        {
            var full = Path.GetFullPath(path);
            // Shortcut (vd kéo từ Desktop/Start Menu) → đăng ký file thật, không phải file .lnk.
            if (string.Equals(Path.GetExtension(full), ".lnk", StringComparison.OrdinalIgnoreCase)
                && Shortcuts.Resolve(full) is ShortcutInfo link)
                return FromShortcut(link);
            return new ToolEntry { Name = SuggestName(full), Path = full, Pack = SuggestPack(full) };
        }

        public static ToolEntry FromShortcut(ShortcutInfo link) => new ToolEntry
        {
            Name = link.Name,
            Path = link.TargetPath,
            Args = string.IsNullOrWhiteSpace(link.Arguments) ? null : link.Arguments,
            Description = string.IsNullOrWhiteSpace(link.Description) ? null : link.Description,
            Pack = SuggestPack(link.TargetPath),
        };

        public static string SuggestName(string path)
        {
            if (Directory.Exists(path))
                return new DirectoryInfo(path).Name;

            if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    return info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.ProductName))
                    return info.ProductName.Trim();
            }
            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// App cài trong Program Files → chỉ gửi thông tin; file nằm thẳng trong thư mục rộng (Downloads…) → chỉ lấy file;
        /// còn lại lấy cả thư mục.
        /// </summary>
        public static PackMode SuggestPack(string path)
        {
            if (IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                || IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
                return PackMode.None;

            if (File.Exists(path) && Packer.IsBroad(Path.GetDirectoryName(path)))
                return PackMode.File;

            return PackMode.Folder;
        }

        private static bool IsUnder(string path, string folder) =>
            folder.Length > 0 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
