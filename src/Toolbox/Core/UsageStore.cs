using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Toolbox.Core
{
    /// <summary>Mức dùng của một mục: số lần chạy và lần cuối (UTC).</summary>
    [DataContract]
    public class UsageEntry
    {
        [DataMember(Name = "count")] public int Count { get; set; }
        [DataMember(Name = "last")] public DateTime Last { get; set; }
    }

    /// <summary>
    /// Mức dùng của từng mục chạy được (tool, profile, workflow, lệnh riêng), lưu ở usage.json để xếp thứ tự "dùng gần đây, dùng nhiều"
    /// cho bảng lệnh Ctrl+K và Trang chủ. Chỉ giữ một số mục dùng gần nhất. Lỗi file không bao giờ làm hỏng thao tác chạy.
    /// </summary>
    public class UsageStore
    {
        public const int MaxEntries = 300;

        [DataContract]
        private class UsageFile
        {
            [DataMember(Name = "items")] public Dictionary<string, UsageEntry> Items { get; set; }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, UsageEntry> _items;

        private UsageStore(string path, Dictionary<string, UsageEntry> items)
        {
            FilePath = path;
            _items = new Dictionary<string, UsageEntry>(items, StringComparer.OrdinalIgnoreCase);
        }

        public static string DefaultPath => Path.Combine(DataDir.Root, "usage.json");

        public string FilePath { get; }

        /// <summary>Dùng chung trong tiến trình GUI; test dùng <see cref="Load"/> với thư mục tạm.</summary>
        public static UsageStore Shared { get; } = Load(null);

        public static UsageStore Load(string path)
        {
            path = path ?? DefaultPath;
            try
            {
                if (File.Exists(path))
                    using (var stream = File.OpenRead(path))
                    {
                        var file = Json.Read<UsageFile>(stream);
                        if (file?.Items != null)
                            return new UsageStore(path, file.Items);
                    }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)
            {
                // File hỏng: bắt đầu lại, mức dùng không quan trọng bằng việc app chạy được.
            }
            return new UsageStore(path, new Dictionary<string, UsageEntry>());
        }

        /// <summary>Khoá cho từng loại mục; giữ một chỗ định nghĩa để bảng lệnh, Trang chủ và nơi ghi dùng cùng khoá.</summary>
        public static string Key(string kind, string id, string workspaceId = null) =>
            workspaceId == null ? kind + ":" + id : kind + ":" + workspaceId + ":" + id;

        /// <summary>Loại khoá của lệnh chạy nhanh (<c>QuickCommand</c>).</summary>
        public const string QuickKind = "quick";

        /// <summary>Khoá mức dùng của một lệnh chạy nhanh theo <c>QuickCommand.Id</c> (đã chứa workspace id).</summary>
        public static string QuickKey(string quickId) => Key(QuickKind, quickId);

        public void Record(string key, DateTime? now = null)
        {
            if (string.IsNullOrEmpty(key))
                return;
            lock (_gate)
            {
                if (!_items.TryGetValue(key, out var entry))
                    _items[key] = entry = new UsageEntry();
                entry.Count++;
                entry.Last = (now ?? DateTime.UtcNow).ToUniversalTime();
                if (_items.Count > MaxEntries)
                    foreach (var stale in _items.OrderBy(p => p.Value.Last).Take(_items.Count - MaxEntries).Select(p => p.Key).ToList())
                        _items.Remove(stale);
                Save();
            }
        }

        public UsageEntry Get(string key)
        {
            lock (_gate)
                return _items.TryGetValue(key ?? "", out var entry) ? new UsageEntry { Count = entry.Count, Last = entry.Last } : null;
        }

        /// <summary>Các khoá dùng gần nhất trước.</summary>
        public IReadOnlyList<string> Recent(int limit, Func<string, bool> filter = null)
        {
            lock (_gate)
                return _items.Where(p => filter == null || filter(p.Key))
                             .OrderByDescending(p => p.Value.Last)
                             .Take(limit)
                             .Select(p => p.Key)
                             .ToList();
        }

        /// <summary>
        /// Điểm dùng (0 nếu chưa dùng): tần suất theo log để một mục dùng cả trăm lần không át hẳn,
        /// cộng độ gần đây giảm dần theo ngày (nửa điểm sau khoảng một tuần).
        /// </summary>
        public double Score(string key, DateTime? now = null)
        {
            var entry = Get(key);
            if (entry == null)
                return 0;
            var days = Math.Max(0, ((now ?? DateTime.UtcNow) - entry.Last).TotalDays);
            return Math.Log(1 + entry.Count) + 3 * Math.Pow(0.9, days);
        }

        /// <summary>Gỡ mục không còn tồn tại (workspace bị gỡ, profile bị xoá).</summary>
        public void Forget(Func<string, bool> predicate)
        {
            lock (_gate)
            {
                var gone = _items.Keys.Where(predicate).ToList();
                if (gone.Count == 0)
                    return;
                foreach (var key in gone)
                    _items.Remove(key);
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));
                var temp = FilePath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, new UsageFile { Items = _items });
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, destinationBackupFileName: null);
                else
                    File.Move(temp, FilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
