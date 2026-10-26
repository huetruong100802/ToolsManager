using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;

namespace Toolbox.Core
{
    [DataContract]
    public class PackManifest
    {
        public const int CurrentFormatVersion = 1;

        [DataMember(Name = "formatVersion", Order = 0)]
        public int FormatVersion { get; set; }

        [DataMember(Name = "createdAt", Order = 1)]
        public string CreatedAt { get; set; }

        /// <summary>Path của tool đã kèm file là đường dẫn tương đối trong gói ("id/..."); pack=none/setup giữ nguyên path gốc (setup thì file bộ cài nằm ở "id/...").</summary>
        [DataMember(Name = "tools", Order = 2)]
        public List<ToolEntry> Tools { get; set; } = new List<ToolEntry>();
    }

    public class PackPreview
    {
        public ToolEntry Tool { get; set; }
        /// <summary>Thư mục sẽ được lấy; null nếu pack=none.</summary>
        public string SourceRoot { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        /// <summary>Thư mục rộng (gốc ổ đĩa, Downloads, Desktop…) — dễ là chọn nhầm.</summary>
        public bool IsBroadFolder { get; set; }
    }

    /// <summary>Đóng gói tool thành một file zip và giải nén gói nhận được.</summary>
    public static class Packer
    {
        public const string ManifestName = "toolbox-pack.json";
        /// <summary>Đuôi riêng để gắn mở bằng Toolbox khi double-click; bên trong là zip thường.</summary>
        public const string FileExtension = ".toolbox";

        public static PackPreview Preview(ToolEntry tool)
        {
            var preview = new PackPreview { Tool = tool };
            if (tool.Pack == PackMode.None)
                return preview;
            if (tool.Pack == PackMode.Setup)
            {
                var setup = RequireSetup(tool);
                preview.SourceRoot = Path.GetDirectoryName(setup);
                preview.FileCount = 1;
                preview.TotalBytes = new FileInfo(setup).Length;
                return preview;
            }

            var (root, files) = Collect(tool);
            preview.SourceRoot = root;
            preview.FileCount = files.Count;
            preview.TotalBytes = files.Sum(f => new FileInfo(f).Length);
            preview.IsBroadFolder = IsBroad(root);
            return preview;
        }

        public static void Pack(IEnumerable<ToolEntry> tools, string zipPath)
        {
            var list = tools.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Chưa chọn tool nào để đóng gói.", nameof(tools));
            foreach (var tool in list.Where(t => !Text.IsValidId(t.Id)))
                throw new ArgumentException($"Id không hợp lệ: \"{tool.Id}\".", nameof(tools));
            var duplicate = list.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new ArgumentException($"Tool \"{duplicate.Key}\" bị chọn trùng.", nameof(tools));

            var output = Path.GetFullPath(zipPath);
            var temp = output + ".tmp";
            var manifest = new PackManifest
            {
                FormatVersion = PackManifest.CurrentFormatVersion,
                CreatedAt = DateTime.Now.ToString("o"),
            };

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var tool in list)
                {
                    var copy = tool.Clone();
                    copy.Setup = null;
                    if (tool.Pack == PackMode.Setup)
                    {
                        // Chỉ gửi bộ cài; path giữ nguyên vì app sẽ được cài vào cùng chỗ trên máy nhận.
                        var setup = RequireSetup(tool);
                        copy.Setup = ToEntryName(tool.Id, Path.GetFileName(setup));
                        AddFile(zip, setup, copy.Setup);
                    }
                    else if (tool.Pack != PackMode.None)
                    {
                        var (root, files) = Collect(tool);
                        foreach (var file in files)
                        {
                            // Bỏ qua chính file gói nếu lưu gói vào trong thư mục đang đóng gói.
                            if (PathEquals(file, output) || PathEquals(file, temp))
                                continue;
                            AddFile(zip, file, ToEntryName(tool.Id, Relative(root, file)));
                        }
                        copy.Path = ToEntryName(tool.Id, Relative(root, Path.GetFullPath(tool.ResolvedPath)));
                    }
                    manifest.Tools.Add(copy);
                }

                using (var entryStream = zip.CreateEntry(ManifestName).Open())
                    Json.Write(entryStream, manifest);
            }

            if (File.Exists(output))
                File.Delete(output);
            File.Move(temp, output);
        }

        /// <summary>Đọc và kiểm tra manifest của gói (dùng cho màn xem trước khi nhập).</summary>
        public static PackManifest ReadManifest(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
                return ReadManifest(zip);
        }

        /// <summary>
        /// Giải nén các tool được chọn vào destRoot\&lt;id&gt; (trùng thư mục thì thêm -2, -3…, không ghi đè).
        /// Trả về entry với path đã đổi sang vị trí mới.
        /// </summary>
        public static IList<ToolEntry> Extract(string zipPath, string destRoot, IEnumerable<string> toolIds)
        {
            var selected = new HashSet<string>(toolIds, StringComparer.OrdinalIgnoreCase);
            var root = Path.GetFullPath(destRoot);
            var result = new List<ToolEntry>();
            var plan = new List<(ZipArchiveEntry Entry, string Destination)>();
            var folders = new List<string>();

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var manifest = ReadManifest(zip);
                var reservedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Lượt 1: kiểm toàn bộ đường dẫn trước, để gói độc hại không giải nén được file nào.
                foreach (var tool in manifest.Tools.Where(t => selected.Contains(t.Id)))
                {
                    var copy = tool.Clone();
                    if (tool.Pack != PackMode.Setup)
                        copy.Setup = null;
                    if (tool.Pack != PackMode.None)
                    {
                        var folder = UniqueFolder(root, tool.Id, reservedFolders);
                        folders.Add(folder);
                        var prefix = tool.Id + "/";
                        foreach (var entry in zip.Entries)
                        {
                            var name = entry.FullName.Replace('\\', '/');
                            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal))
                                continue;
                            plan.Add((entry, SafeCombine(folder, name.Substring(prefix.Length))));
                        }
                        if (tool.Pack == PackMode.Setup)
                            copy.Setup = SafeCombine(folder, tool.Setup.Substring(prefix.Length));
                        else
                            copy.Path = tool.Path.Length > tool.Id.Length
                                ? SafeCombine(folder, tool.Path.Substring(prefix.Length))
                                : folder;
                    }
                    result.Add(copy);
                }

                // Lượt 2: giải nén. Tạo sẵn thư mục của từng tool để tool dạng thư mục rỗng vẫn có path tồn tại.
                foreach (var folder in folders)
                    Directory.CreateDirectory(folder);
                foreach (var (entry, destination) in plan)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, overwrite: false);
                }
            }

            return result;
        }

        private static PackManifest ReadManifest(ZipArchive zip)
        {
            var entry = zip.GetEntry(ManifestName)
                ?? throw new InvalidDataException("File này không phải gói Toolbox (thiếu " + ManifestName + ").");

            PackManifest manifest;
            try
            {
                using (var stream = entry.Open())
                    manifest = Json.Read<PackManifest>(stream);
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("Manifest của gói bị hỏng: " + ex.Message, ex);
            }

            if (manifest == null || manifest.Tools == null)
                throw new InvalidDataException("Manifest của gói bị hỏng.");
            if (manifest.FormatVersion > PackManifest.CurrentFormatVersion)
                throw new InvalidDataException("Gói được tạo bởi phiên bản Toolbox mới hơn, hãy cập nhật Toolbox.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in manifest.Tools)
            {
                if (!Text.IsValidId(tool.Id))
                    throw new InvalidDataException($"Gói chứa id không hợp lệ: \"{tool.Id}\".");
                if (!ids.Add(tool.Id))
                    throw new InvalidDataException($"Gói chứa id trùng: \"{tool.Id}\".");
                if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Path))
                    throw new InvalidDataException($"Tool \"{tool.Id}\" trong gói thiếu tên hoặc đường dẫn.");
                var prefix = tool.Id + "/";
                if ((tool.Pack == PackMode.Folder || tool.Pack == PackMode.File)
                    && tool.Path != tool.Id && !tool.Path.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidDataException($"Tool \"{tool.Id}\" trong gói có đường dẫn không hợp lệ.");
                if (tool.Pack == PackMode.Setup
                    && (tool.Setup == null || tool.Setup.Length <= prefix.Length || !tool.Setup.StartsWith(prefix, StringComparison.Ordinal)))
                    throw new InvalidDataException($"Tool \"{tool.Id}\" trong gói có đường dẫn bộ cài không hợp lệ.");
            }
            return manifest;
        }

        private static (string Root, List<string> Files) Collect(ToolEntry tool)
        {
            var path = Path.GetFullPath(tool.ResolvedPath);
            if (Directory.Exists(path))
                return (path, Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList());
            if (!File.Exists(path))
                throw new FileNotFoundException($"Không tìm thấy \"{tool.Name}\": {path}", path);

            var root = Path.GetDirectoryName(path);
            return tool.Pack == PackMode.File
                ? (root, new List<string> { path })
                : (root, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList());
        }

        private static string RequireSetup(ToolEntry tool)
        {
            if (string.IsNullOrWhiteSpace(tool.Setup))
                throw new FileNotFoundException($"\"{tool.Name}\" chưa chọn file bộ cài.");
            var path = Path.GetFullPath(tool.ResolvedSetup);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Không tìm thấy bộ cài của \"{tool.Name}\": {path}", path);
            return path;
        }

        private static void AddFile(ZipArchive zip, string file, string entryName)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(file);
            // Cho phép đọc cả khi tool đang chạy.
            using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = entry.Open())
                source.CopyTo(target);
        }

        private static string ToEntryName(string id, string relative) =>
            relative.Length == 0 ? id : id + "/" + relative.Replace('\\', '/');

        private static string Relative(string root, string fullPath)
        {
            var trimmed = root.TrimEnd('\\', '/');
            if (string.Equals(trimmed, fullPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return "";
            return fullPath.Substring(trimmed.Length + 1);
        }

        /// <summary>Ghép đường dẫn và chặn thoát ra ngoài root (zip-slip).</summary>
        internal static string SafeCombine(string root, string relative)
        {
            var bad = relative.Length == 0 || Path.IsPathRooted(relative) || relative.Contains(":");
            string full = null;
            if (!bad)
            {
                full = Path.GetFullPath(Path.Combine(root, relative));
                bad = !full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            if (bad)
                throw new InvalidDataException($"Gói chứa đường dẫn không hợp lệ: \"{relative}\".");
            return full;
        }

        private static string UniqueFolder(string root, string id, HashSet<string> reserved)
        {
            for (var i = 1; ; i++)
            {
                var candidate = Path.Combine(root, i == 1 ? id : $"{id}-{i}");
                if (!Directory.Exists(candidate) && !File.Exists(candidate) && reserved.Add(candidate))
                    return candidate;
            }
        }

        internal static bool IsBroad(string folder)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var broad = new[]
            {
                profile,
                Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            };
            return Path.GetPathRoot(folder).TrimEnd('\\') == folder.TrimEnd('\\')
                || broad.Any(b => b.Length > 0 && PathEquals(b, folder));
        }

        private static bool PathEquals(string a, string b) =>
            string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
