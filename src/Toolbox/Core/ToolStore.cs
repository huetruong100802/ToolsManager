using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

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
        /// <summary>Số bản sao lưu giữ lại trong thư mục backup.</summary>
        private const int MaxBackups = 50;
        private const string LockName = @"Global\ToolboxStore";
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

        [DataContract]
        private class ToolListFile
        {
            [DataMember(Name = "tools")]
            public List<ToolEntry> Tools { get; set; }
        }

        private readonly List<ToolEntry> _tools;
        private readonly List<Change> _pending = new List<Change>();

        private ToolStore(string filePath, List<ToolEntry> tools)
        {
            FilePath = filePath;
            _tools = tools;
        }

        public static string DefaultPath =>
            System.IO.Path.Combine(DataDir.Root, "tools.json");

        public string FilePath { get; }

        public IReadOnlyList<ToolEntry> Tools => _tools;

        /// <summary>Nguồn ghi vào changes.log: "gui" hoặc "mcp".</summary>
        public string Actor { get; set; } = "gui";

        /// <summary>File chưa tồn tại → danh sách rỗng. File hỏng → ném lỗi, không ghi đè.</summary>
        public static ToolStore Load(string filePath)
        {
            using (Lock())
                return ReadFile(filePath);
        }

        /// <summary>
        /// Đọc không lấy khoá, cho tra cứu chỉ đọc (vd. kiểm tool có trong thư viện) chạy được ngay cả khi đang giữ khoá workspaces:
        /// thứ tự khoá quy ước là tools rồi workspaces, nên lấy khoá tools từ trong khoá workspaces có thể chờ nhau tới hết thời hạn.
        /// Ghi là ghi ra file tạm rồi thay thế nên đọc không thấy file dở; đọc trúng lúc đang thay thế thì thử lại vài lần.
        /// Đừng dùng kết quả để đọc-sửa-ghi: dùng <see cref="Load"/>.
        /// </summary>
        public static ToolStore Peek(string filePath)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return ReadFile(filePath);
                }
                catch (IOException) when (attempt < 4)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
        }

        private static ToolStore ReadFile(string filePath)
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

        /// <summary>
        /// Khoá ghi tools.json giữa GUI và MCP server. Load/Save tự khoá; gọi trực tiếp khi cần đọc-sửa-ghi liền mạch.
        /// Khoá là đệ quy trong cùng một luồng.
        /// </summary>
        public static IDisposable Lock()
        {
            var mutex = new Mutex(initiallyOwned: false, name: LockName);
            try
            {
                if (!mutex.WaitOne(LockTimeout))
                {
                    mutex.Dispose();
                    throw new IOException("tools.json đang được tiến trình khác ghi, thử lại sau.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Tiến trình trước chết khi đang giữ khoá; khoá vẫn được cấp cho ta.
            }
            return new Releaser(mutex);
        }

        /// <summary>
        /// Ghi ra file tạm rồi thay thế, để file không bị hỏng nếu ghi dở.
        /// Nếu có thay đổi danh sách thì sao lưu bản cũ trước và ghi nhật ký sau khi ghi thành công.
        /// </summary>
        public void Save()
        {
            using (Lock())
            {
                var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(FilePath));
                Directory.CreateDirectory(directory);
                if (_pending.Count > 0)
                    Backup(directory);

                var temp = FilePath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, new ToolListFile { Tools = _tools });

                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, destinationBackupFileName: null);
                else
                    File.Move(temp, FilePath);

                if (_pending.Count > 0)
                {
                    AppendLog(directory);
                    _pending.Clear();
                }
            }
        }

        /// <summary>
        /// Ghi nhận lần chạy qua MCP. Không ghi vào nhật ký thay đổi và không sao lưu (chỉ thay đổi số liệu, không sửa danh sách).
        /// </summary>
        public void MarkUsed(string id)
        {
            var tool = Find(id);
            if (tool == null)
                return;
            tool.LastUsedAt = DateTime.UtcNow;
            tool.RunCount++;
        }

        public ToolEntry Find(string id) =>
            _tools.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Thêm tool; id sinh từ tên nếu chưa có và được làm cho duy nhất.</summary>
        public ToolEntry Add(ToolEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new ArgumentException("Tool phải có tên.", nameof(entry));
            // Tool lệnh có Path rỗng là hợp lệ (chạy tại thư mục workspace); tool file chạy thì phải có đường dẫn.
            if (string.IsNullOrWhiteSpace(entry.Path) && !entry.IsCommand)
                throw new ArgumentException("Tool phải có đường dẫn.", nameof(entry));

            entry.Id = UniqueId(Text.IsValidId(entry.Id) ? entry.Id : Text.ToId(entry.Name));
            _tools.Add(entry);
            Record("add", entry);
            return entry;
        }

        /// <summary>Thay entry có cùng id.</summary>
        public void Update(ToolEntry entry)
        {
            var index = _tools.FindIndex(t => string.Equals(t.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new KeyNotFoundException($"Không có tool \"{entry.Id}\".");
            _tools[index] = entry;
            Record("update", entry);
        }

        /// <summary>Chỉ xoá khỏi danh sách, không đụng tới file của tool.</summary>
        public bool Remove(string id)
        {
            var tool = Find(id);
            if (tool == null)
                return false;
            _tools.Remove(tool);
            Record("remove", tool);
            return true;
        }

        /// <summary>
        /// Tìm theo id/tên/mô tả/tag, không phân biệt hoa thường và dấu.
        /// Nếu có <paramref name="tags"/>: tool phải có ít nhất một tag trong danh sách (khớp bất kỳ).
        /// </summary>
        public IEnumerable<ToolEntry> Search(string query, IEnumerable<string> tags = null)
        {
            var terms = Text.Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var foldedTags = (tags ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Text.Fold)
                .ToList();

            return _tools.Where(t =>
            {
                if (foldedTags.Count > 0 && !t.Tags.Any(x => foldedTags.Contains(Text.Fold(x))))
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
                Record("import", entry);
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
                    Record("import", entry);
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

        private void Record(string action, ToolEntry tool) =>
            _pending.Add(new Change { Action = action, Id = tool.Id, Name = tool.Name });

        /// <summary>Chép tools.json hiện tại vào thư mục backup, giữ tối đa <see cref="MaxBackups"/> bản mới nhất.</summary>
        private void Backup(string directory)
        {
            if (!File.Exists(FilePath))
                return;

            var backupDir = System.IO.Path.Combine(directory, "backup");
            Directory.CreateDirectory(backupDir);
            var name = "tools-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json";
            File.Copy(FilePath, System.IO.Path.Combine(backupDir, name), overwrite: true);

            // Tên có dấu thời gian nên sắp theo tên là sắp theo thời gian.
            foreach (var old in Directory.GetFiles(backupDir, "tools-*.json")
                                         .OrderByDescending(f => f, StringComparer.Ordinal)
                                         .Skip(MaxBackups))
                File.Delete(old);
        }

        private void AppendLog(string directory)
        {
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var lines = _pending.Select(c => string.Join("\t", time, Actor, c.Action, Clean(c.Id), Clean(c.Name)));
            File.AppendAllLines(System.IO.Path.Combine(directory, "changes.log"), lines, Encoding.UTF8);
        }

        // Tab và xuống dòng trong tên sẽ làm hỏng định dạng từng dòng của nhật ký.
        private static string Clean(string value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private sealed class Change
        {
            public string Action { get; set; }
            public string Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly Mutex _mutex;

            public Releaser(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
    }
}
