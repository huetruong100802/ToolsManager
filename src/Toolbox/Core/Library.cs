using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Toolbox.Core
{
    /// <summary>Thư viện bản chép của tool (nằm cạnh tools.json) và các ngưỡng dung lượng.</summary>
    public static class Library
    {
        /// <summary>Tool có thư mục chứa file nhỏ hơn ngưỡng này được chép vào thư viện (store=copy).</summary>
        public const long CopyLimitBytes = 50L * 1024 * 1024;

        /// <summary>Bộ cài lớn hơn ngưỡng này không được chép; tool khi đó là pack=none.</summary>
        public const long SetupLimitBytes = 500L * 1024 * 1024;

        public static string RootOf(ToolStore store) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(store.FilePath)), "library");

        public static string FolderOf(ToolStore store, string id) => Path.Combine(RootOf(store), id);

        /// <summary>Thư mục sẽ được chép khi chép tool: thư mục chứa file, hoặc chính thư mục.</summary>
        public static string CopyRootOf(string path) =>
            File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path)) : Path.GetFullPath(path);

        /// <summary>Đường dẫn nằm trong thư mục root (hoặc chính root).</summary>
        public static bool IsInside(string path, string root)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\') + '\\';
            var rootFull = Path.GetFullPath(root).TrimEnd('\\') + '\\';
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>App nằm trong Program Files: không chép, vì driver và thư mục cài đặt không chạy được khi chép.</summary>
        public static bool IsInProgramFiles(string path) =>
            new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }
            .Where(root => !string.IsNullOrEmpty(root))
            .Any(root => IsInside(path, root));

        /// <summary>
        /// Dung lượng file, hoặc tổng dung lượng thư mục. Ngừng đếm khi vượt <paramref name="limit"/>.
        /// Không đọc được thì coi như rất lớn, để tool không bị chép.
        /// </summary>
        public static long Measure(string path, long limit)
        {
            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;

                long total = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    total += new FileInfo(file).Length;
                    if (total > limit)
                        break;
                }
                return total;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return long.MaxValue;
            }
        }

        /// <summary>
        /// Chép file (kèm cả thư mục chứa nó) hoặc thư mục vào thư viện của tool, thay bản cũ nếu có.
        /// Trả về đường dẫn mới: file trong bản chép, hoặc chính thư mục chép.
        /// </summary>
        public static string CopyTarget(ToolStore store, string id, string source)
        {
            var sourceRoot = CopyRootOf(source);
            var dest = FolderOf(store, id);

            // Xoá thư mục đích trước khi chép: nguồn nằm trong đích hoặc đích nằm trong nguồn sẽ mất dữ liệu.
            if (IsInside(sourceRoot, dest) || IsInside(dest, sourceRoot))
                throw new ArgumentException("Nguồn và thư viện Toolbox chồng lên nhau; hãy dùng đường dẫn gốc của tool.", nameof(source));

            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            CopyDirectory(sourceRoot, dest);

            return File.Exists(source) ? Path.Combine(dest, Path.GetFileName(source)) : dest;
        }

        /// <summary>Chép bộ cài vào thư mục setup của tool trong thư viện. Trả về đường dẫn mới.</summary>
        public static string CopySetup(ToolStore store, string id, string setupFile)
        {
            var dir = Path.Combine(FolderOf(store, id), "setup");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(setupFile));
            File.Copy(setupFile, dest, overwrite: true);
            return dest;
        }

        /// <summary>Tool có thư mục riêng trong thư viện (bản chép hoặc bộ cài).</summary>
        public static bool HasCopy(ToolStore store, string id) =>
            Text.IsValidId(id) && Directory.Exists(FolderOf(store, id));

        /// <summary>
        /// Xoá thư mục thư viện của tool. Chỉ xoá khi id hợp lệ, nên không bao giờ chạm vào file gốc hay thư mục khác.
        /// Gọi sau khi đã bỏ tool khỏi tools.json, để không xoá bản chép của tool còn trong danh sách.
        /// </summary>
        public static void DeleteCopy(ToolStore store, string id)
        {
            if (HasCopy(store, id))
                Directory.Delete(FolderOf(store, id), recursive: true);
        }

        /// <summary>Tên, dung lượng và SHA-256 của một file. Thư mục hoặc đường dẫn không tồn tại thì trả về null.</summary>
        public static string Fingerprint(string path)
        {
            if (!File.Exists(path))
                return null;

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return $"{Path.GetFileName(path)}|{new FileInfo(path).Length}|{hash}";
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
