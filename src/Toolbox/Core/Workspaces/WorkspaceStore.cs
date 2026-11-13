using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Danh sách workspace, lưu trong workspaces.json cạnh tools.json.</summary>
    public class WorkspaceStore
    {
        private const string LockName = @"Global\ToolboxWorkspaces";
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

        [DataContract]
        private class WorkspaceFile
        {
            /// <summary>Phiên bản schema; file cũ không có trường này (0) được coi là 1.</summary>
            [DataMember(Name = "schemaVersion", EmitDefaultValue = false)]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "workspaces")]
            public List<Workspace> Workspaces { get; set; }
        }

        private readonly List<Workspace> _items;

        /// <summary>Băm nội dung file ở lần đọc/ghi gần nhất; khác file hiện tại nghĩa là tiến trình khác (agent) vừa ghi.</summary>
        private string _diskHash;

        /// <summary>Bản sao từng workspace ở lần đọc/ghi gần nhất (theo id), để biết bên nào đã sửa khi gộp.</summary>
        private Dictionary<string, Workspace> _snapshot = new Dictionary<string, Workspace>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Schema của dữ liệu trong bộ nhớ; chỉ lên <see cref="WorkspaceMigration.CurrentSchema"/> khi đã nâng xong.</summary>
        private int _schema;

        private WorkspaceStore(string filePath, List<Workspace> items, int schema)
        {
            FilePath = filePath;
            _items = items;
            _schema = schema;
        }

        public static string DefaultPath =>
            System.IO.Path.Combine(DataDir.Root, "workspaces.json");

        public string FilePath { get; }

        public IReadOnlyList<Workspace> Workspaces => _items;

        /// <summary>
        /// File chưa tồn tại → danh sách rỗng. File hỏng → ném lỗi, không ghi đè.
        /// File schema cũ được nâng bằng <see cref="WorkspaceMigration"/>, thư viện là tools.json cùng thư mục.
        /// </summary>
        public static WorkspaceStore Load(string filePath) => Load(filePath, null);

        /// <summary>
        /// Như <see cref="Load(string)"/>, với <paramref name="toolsPath"/> là tools.json dùng khi nâng schema (null: cùng thư mục với
        /// <paramref name="filePath"/>). Có thay đổi thì sao lưu <c>workspaces.json.v{n}.bak</c> (n = schema cũ) rồi mới ghi;
        /// thư viện chỉ ghi khi vừa seed tool. Nâng lỗi (IO/dữ liệu) → <see cref="InvalidDataException"/>, file gốc giữ nguyên.
        /// </summary>
        public static WorkspaceStore Load(string filePath, string toolsPath)
        {
            WorkspaceStore store;
            using (Lock())
                store = Read(filePath);
            if (store._schema >= WorkspaceMigration.CurrentSchema)
                return store;
            // Nâng schema chạm cả tools.json: làm NGOÀI khoá workspaces để không đảo thứ tự khoá với nơi giữ khoá tools rồi ghi workspace
            // (vd. MCP đăng ký tool), nếu không hai bên chờ nhau tới hết thời hạn khoá.
            return store.Migrate(toolsPath ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(filePath)), "tools.json"));
        }

        /// <summary>Đọc file workspace, chưa nâng schema. Gọi trong khoá.</summary>
        private static WorkspaceStore Read(string filePath)
        {
            if (!File.Exists(filePath))
                return new WorkspaceStore(filePath, new List<Workspace>(), WorkspaceMigration.CurrentSchema);
            WorkspaceFile file;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
                using (var stream = new MemoryStream(bytes))
                    file = Json.Read<WorkspaceFile>(stream);
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException($"Không đọc được {filePath}: {ex.Message}", ex);
            }
            var store = new WorkspaceStore(filePath, file?.Workspaces ?? new List<Workspace>(), Math.Max(1, file?.SchemaVersion ?? 1));
            store._diskHash = Hash(bytes);
            store.TakeSnapshot();
            return store;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path))
                return null;
            try { return Hash(File.ReadAllBytes(path)); }
            catch (IOException) { return null; }
        }

        private void TakeSnapshot()
        {
            _snapshot = new Dictionary<string, Workspace>(StringComparer.OrdinalIgnoreCase);
            foreach (var workspace in _items)
                _snapshot[workspace.Id ?? ""] = Json.Clone(workspace);
        }

        /// <summary>
        /// Đọc không khoá (ghi là thay file nguyên khối nên không thấy file dở), thử lại vài lần nếu trúng lúc đang thay.
        /// Dùng để nạp thay đổi của agent vào GUI; đừng dùng để đọc-sửa-ghi.
        /// </summary>
        public static WorkspaceStore Peek(string filePath)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return Read(filePath);
                }
                catch (IOException) when (attempt < 4)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
        }

        /// <summary>
        /// Nạp thay đổi do tiến trình khác ghi vào bộ nhớ, không ghi file. Trả về id các workspace đã thêm hoặc đổi
        /// (rỗng nếu file không đổi từ lần đọc/ghi gần nhất).
        /// </summary>
        public IReadOnlyList<string> Sync()
        {
            var fresh = Peek(FilePath);
            return fresh._diskHash == _diskHash ? new List<string>() : MergeFrom(fresh);
        }

        /// <summary>
        /// Nâng schema: seed thư viện (khoá tools), rồi mới lấy khoá workspaces để sao lưu và ghi. Trong lúc chờ khoá, tiến trình khác
        /// có thể đã nâng xong: đọc lại và dùng bản đó. Không đổi gì thì không ghi file workspace nào.
        /// </summary>
        private WorkspaceStore Migrate(string toolsPath)
        {
            var from = _schema;
            try
            {
                var library = ToolStore.Load(toolsPath);
                var toolCount = library.Tools.Count;
                var changed = WorkspaceMigration.Upgrade(_items, library);
                if (library.Tools.Count != toolCount)
                    library.Save();

                using (Lock())
                {
                    var current = Read(FilePath);
                    if (current._schema >= WorkspaceMigration.CurrentSchema)
                        return current;
                    if (changed)
                    {
                        var backup = $"{FilePath}.v{from}.bak";
                        if (!File.Exists(backup))
                            File.Copy(FilePath, backup);
                    }
                    _schema = WorkspaceMigration.CurrentSchema;
                    if (changed)
                        Save();
                    return this;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Không nâng cấp được {FilePath} lên định dạng mới (file gốc giữ nguyên): {ex.Message}", ex);
            }
        }
        /// <summary>Ghi ra file tạm rồi thay thế để file không hỏng nếu ghi dở.</summary>
        /// <summary>
        /// Đọc-sửa-ghi trong một khoá liên tiến trình: đọc file mới nhất, để <paramref name="change"/> sửa, rồi ghi nếu nó trả về true.
        /// Dùng cho mọi ghi từ agent (MCP) để không ghi đè thay đổi của GUI hay tiến trình khác.
        /// </summary>
        public static void Update(string filePath, Func<WorkspaceStore, bool> change)
        {
            using (Lock())
            {
                var store = Load(filePath);
                if (change(store))
                    store.Save();
            }
        }

        public void Save()
        {
            using (Lock())
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(FilePath)));
                // Agent có thể vừa ghi: gộp trước khi ghi để không đè mất thay đổi của họ.
                if (HashFile(FilePath) != _diskHash)
                    MergeFrom(Read(FilePath));
                var temp = FilePath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, new WorkspaceFile { SchemaVersion = _schema, Workspaces = _items });
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, destinationBackupFileName: null);
                else
                    File.Move(temp, FilePath);
                _diskHash = HashFile(FilePath);
                TakeSnapshot();
            }
        }

        public Workspace Find(string id) =>
            _items.Find(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Thêm workspace: cấp id mới và màu kế tiếp chưa dùng nhiều nhất. Không ghi file; gọi <see cref="Save"/>.</summary>
        public Workspace Add(Workspace workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace.Name))
                throw new ArgumentException("Workspace cần có tên.");
            if (string.IsNullOrWhiteSpace(workspace.Path))
                throw new ArgumentException("Workspace cần có đường dẫn thư mục.");

            workspace.Id = NewId();
            workspace.ColorIndex = NextColor();
            _items.Add(workspace);
            return workspace;
        }

        public bool Remove(string id)
        {
            var found = Find(id);
            return found != null && _items.Remove(found);
        }

        /// <summary>Gỡ một tool thư viện khỏi mọi workspace (khi tool bị xoá khỏi thư viện). Không ghi file; gọi <see cref="Save"/>. Trả về số workspace đã đổi.</summary>
        public int UnbindToolEverywhere(string toolId) =>
            _items.Count(w => w.UnbindLibrary(toolId));

        /// <summary>
        /// Gộp bản trên đĩa (do agent qua MCP hoặc tiến trình khác ghi) vào bộ nhớ, ba chiều theo ảnh chụp lần đọc/ghi trước,
        /// chi tiết tới từng trường và từng mục (profile, lệnh riêng, bộ chạy, tool gắn): phần chỉ đổi ở file thì lấy bản file,
        /// phần chỉ đổi ở đây thì giữ, cả hai cùng đổi đúng một mục thì giữ bản trong bộ nhớ (thao tác của người dùng).
        /// Workspace đã gỡ ở đây không bị thêm lại. Mục không đổi giữ nguyên đối tượng để dòng giao diện và form đang mở vẫn trỏ đúng.
        /// Không ghi file. Trả về id các workspace đã thêm hoặc đổi.
        /// </summary>
        public IReadOnlyList<string> MergeFrom(WorkspaceStore fresh)
        {
            var changed = new List<string>();
            foreach (var disk in fresh._items)
            {
                var existing = Find(disk.Id);
                if (existing == null)
                {
                    if (!_snapshot.ContainsKey(disk.Id ?? ""))
                    {
                        _items.Add(disk);
                        changed.Add(disk.Id);
                    }
                    continue;
                }

                if (!_snapshot.TryGetValue(existing.Id ?? "", out var snap))
                    continue; // mới thêm ở đây trùng id với workspace mới ở file: giữ bản của GUI
                var any = false;
                any |= TakeScalar(existing.Name, snap.Name, disk.Name, v => existing.Name = v);
                any |= TakeScalar(existing.Path, snap.Path, disk.Path, v => existing.Path = v);
                any |= TakeScalar(existing.ColorIndex, snap.ColorIndex, disk.ColorIndex, v => existing.ColorIndex = v);
                any |= TakeScalar(existing.Pinned, snap.Pinned, disk.Pinned, v => existing.Pinned = v);
                any |= TakeScalar(existing.Archived, snap.Archived, disk.Archived, v => existing.Archived = v);
                any |= MergeList(existing.LibraryBindings, snap.LibraryBindings, disk.LibraryBindings, x => x.ToolId);
                any |= MergeList(existing.Bindings, snap.Bindings, disk.Bindings, x => x.ToolId);
                any |= MergeList(existing.Customs, snap.Customs, disk.Customs, x => x.Id);
                any |= MergeList(existing.Profiles, snap.Profiles, disk.Profiles, x => x.Id);
                any |= MergeList(existing.Stacks, snap.Stacks, disk.Stacks, x => x.Id);
                any |= MergeList(existing.VariableGroups, snap.VariableGroups, disk.VariableGroups, x => x.Id);
                if (Json.Canonical(existing.Worktree) == Json.Canonical(snap.Worktree)
                    && Json.Canonical(disk.Worktree) != Json.Canonical(snap.Worktree))
                {
                    existing.Worktree = disk.Worktree;
                    any = true;
                }
                if (any)
                    changed.Add(existing.Id);
            }
            _diskHash = fresh._diskHash;
            _snapshot = fresh._snapshot;
            return changed;
        }

        /// <summary>Giá trị đơn: chỉ file đổi so với ảnh chụp thì lấy giá trị của file.</summary>
        private static bool TakeScalar<T>(T mine, T before, T disk, Action<T> set)
        {
            var comparer = EqualityComparer<T>.Default;
            if (!comparer.Equals(mine, before) || comparer.Equals(disk, before))
                return false;
            set(disk);
            return true;
        }

        /// <summary>
        /// Danh sách mục có khoá, sửa tại chỗ trong <paramref name="mine"/>: mục mới ở file thì thêm, mục chỉ file đổi thì thay,
        /// mục chỉ file xoá (ở đây chưa sửa) thì xoá. Mục GUI đã xoá không bị thêm lại; mục hai bên cùng sửa giữ bản của GUI.
        /// </summary>
        private static bool MergeList<T>(List<T> mine, List<T> before, List<T> disk, Func<T, string> key) where T : class
        {
            if (mine == null || disk == null)
                return false;
            var beforeByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in before ?? new List<T>())
                beforeByKey[key(item) ?? ""] = Json.Canonical(item);
            var diskByKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in disk)
                diskByKey[key(item) ?? ""] = item;

            var changed = false;
            foreach (var item in mine.ToList())
            {
                var k = key(item) ?? "";
                var unchangedHere = beforeByKey.TryGetValue(k, out var was) && Json.Canonical(item) == was;
                if (!diskByKey.TryGetValue(k, out var onDisk))
                {
                    if (unchangedHere)
                    {
                        mine.Remove(item);
                        changed = true;
                    }
                }
                else if (unchangedHere && Json.Canonical(onDisk) != was)
                {
                    mine[mine.IndexOf(item)] = onDisk;
                    changed = true;
                }
            }
            foreach (var item in disk)
            {
                var k = key(item) ?? "";
                if (!mine.Any(m => string.Equals(key(m) ?? "", k, StringComparison.OrdinalIgnoreCase)) && !beforeByKey.ContainsKey(k))
                {
                    mine.Add(item);
                    changed = true;
                }
            }
            return changed;
        }


        /// <summary>Id mới cho lệnh riêng, profile, bộ chạy (8 ký tự, không trùng trong workspace).</summary>
        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        private int NextColor()
        {
            var counts = new int[Workspace.ColorCount];
            foreach (var w in _items)
                counts[Math.Abs(w.ColorIndex) % Workspace.ColorCount]++;
            return Array.IndexOf(counts, counts.Min());
        }

        /// <summary>
        /// Khoá workspaces.json. Thứ tự khoá toàn cục là tools.json rồi workspaces.json: mỗi lần lấy khoá này đều lấy khoá tools trước
        /// (đệ quy trong cùng luồng nên không phiền ai đã giữ sẵn). Nhờ vậy không thể có hai bên giữ khoá ngược thứ tự và chờ nhau,
        /// kể cả giữa GUI và MCP: mọi tool đều nằm trong thư viện nên chỗ ghi workspace thường phải tra tools.json trong lúc đang giữ khoá.
        /// </summary>
        private static IDisposable Lock()
        {
            var tools = ToolStore.Lock();
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name: LockName);
                try
                {
                    if (!mutex.WaitOne(LockTimeout))
                        throw new IOException("workspaces.json đang được tiến trình khác ghi, thử lại sau.");
                }
                catch (AbandonedMutexException)
                {
                    // Tiến trình trước chết khi đang giữ khoá; khoá vẫn được cấp cho ta.
                }
                return new Releaser(mutex, tools);
            }
            catch
            {
                mutex?.Dispose();
                tools.Dispose();
                throw;
            }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly Mutex _mutex;
            private readonly IDisposable _tools;
            public Releaser(Mutex mutex, IDisposable tools) { _mutex = mutex; _tools = tools; }
            public void Dispose()
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _tools.Dispose();
            }
        }    }
}
