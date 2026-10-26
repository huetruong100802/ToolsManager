using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Toolbox.Core
{
    public enum ConflictResolution
    {
        /// <summary>Thay entry cũ cùng id (không xoá file của tool cũ).</summary>
        Replace,
        /// <summary>Giữ cả hai, entry mới được đổi id thành id-2, id-3…</summary>
        KeepBoth,
        Skip,
    }

    /// <summary>Danh sách tool, lưu trong một file tools.json.</summary>
    public class ToolStore
    {
        [DataContract]
        private class ToolListFile
        {
            [DataMember(Name = "tools")]
            public List<ToolEntry> Tools { get; set; }
        }

        private readonly List<ToolEntry> _tools;

        private ToolStore(string filePath, List<ToolEntry> tools)
        {
            FilePath = filePath;
            _tools = tools;
        }

        public static string DefaultPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toolbox", "tools.json");

        public string FilePath { get; }

        public IReadOnlyList<ToolEntry> Tools => _tools;

        /// <summary>File chưa tồn tại → danh sách rỗng. File hỏng → ném lỗi, không ghi đè.</summary>
        public static ToolStore Load(string filePath)
        {
            if (!File.Exists(filePath))
                return new ToolStore(filePath, new List<ToolEntry>());

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var file = Json.Read<ToolListFile>(stream);
                    return new ToolStore(filePath, file?.Tools ?? new List<ToolEntry>());
                }
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException($"Không đọc được {filePath}: {ex.Message}", ex);
            }
        }

        /// <summary>Ghi ra file tạm rồi thay thế, để file không bị hỏng nếu ghi dở.</summary>
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(FilePath)));
            var temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                Json.Write(stream, new ToolListFile { Tools = _tools });

            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, destinationBackupFileName: null);
            else
                File.Move(temp, FilePath);
        }

        public ToolEntry Find(string id) =>
            _tools.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Thêm tool; id sinh từ tên nếu chưa có và được làm cho duy nhất.</summary>
        public ToolEntry Add(ToolEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new ArgumentException("Tool phải có tên.", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Path))
                throw new ArgumentException("Tool phải có đường dẫn.", nameof(entry));

            entry.Id = UniqueId(Text.IsValidId(entry.Id) ? entry.Id : Text.ToId(entry.Name));
            _tools.Add(entry);
            return entry;
        }

        /// <summary>Thay entry có cùng id.</summary>
        public void Update(ToolEntry entry)
        {
            var index = _tools.FindIndex(t => string.Equals(t.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new KeyNotFoundException($"Không có tool \"{entry.Id}\".");
            _tools[index] = entry;
        }

        /// <summary>Chỉ xoá khỏi danh sách, không đụng tới file của tool.</summary>
        public bool Remove(string id) =>
            _tools.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

        /// <summary>Tìm theo id/tên/mô tả/tag, không phân biệt hoa thường và dấu; lọc thêm theo tag nếu có.</summary>
        public IEnumerable<ToolEntry> Search(string query, string tag = null)
        {
            var terms = Text.Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var foldedTag = Text.Fold(tag);

            return _tools.Where(t =>
            {
                if (foldedTag.Length > 0 && !t.Tags.Any(x => Text.Fold(x) == foldedTag))
                    return false;

                var haystack = Text.Fold(string.Join(" ", new[] { t.Id, t.Name, t.Description }.Concat(t.Tags)));
                return terms.All(haystack.Contains);
            });
        }

        public IEnumerable<string> AllTags() =>
            _tools.SelectMany(t => t.Tags)
                  .Where(x => !string.IsNullOrWhiteSpace(x))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        /// <summary>Thêm tool từ gói nhập vào. Trả về entry đã thêm, hoặc null nếu bỏ qua.</summary>
        public ToolEntry Import(ToolEntry entry, ConflictResolution resolution)
        {
            if (Find(entry.Id) == null)
            {
                _tools.Add(entry);
                return entry;
            }

            switch (resolution)
            {
                case ConflictResolution.Replace:
                    Update(entry);
                    return entry;
                case ConflictResolution.KeepBoth:
                    entry.Id = UniqueId(entry.Id);
                    _tools.Add(entry);
                    return entry;
                default:
                    return null;
            }
        }

        internal string UniqueId(string baseId)
        {
            if (Find(baseId) == null)
                return baseId;

            for (var i = 2; ; i++)
            {
                var candidate = $"{baseId}-{i}";
                if (Find(candidate) == null)
                    return candidate;
            }
        }
    }
}
