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
            try
            {
                using (var stream = File.OpenRead(filePath))
                    file = Json.Read<WorkspaceFile>(stream);
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException($"Không đọc được {filePath}: {ex.Message}", ex);
            }
            return new WorkspaceStore(filePath, file?.Workspaces ?? new List<Workspace>(), Math.Max(1, file?.SchemaVersion ?? 1));
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
                var temp = FilePath + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, new WorkspaceFile { SchemaVersion = _schema, Workspaces = _items });
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, destinationBackupFileName: null);
                else
                    File.Move(temp, FilePath);
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
        /// Nhận thay đổi từ tiến trình khác (agent qua MCP): thêm workspace mới, và cập nhật tool thư viện đã gắn của workspace đang có.
        /// Giữ nguyên đối tượng hiện có để các dòng giao diện vẫn trỏ đúng. Không ghi file.
        /// </summary>
        public void MergeFrom(WorkspaceStore fresh)
        {
            foreach (var workspace in fresh._items)
            {
                var existing = Find(workspace.Id);
                if (existing == null)
                    _items.Add(workspace);
                else
                {
                    // GUI lưu ngay sau mỗi thay đổi, nên file luôn là bản mới nhất: lấy nguyên các phần cấu hình.
                    existing.LibraryBindings = workspace.LibraryBindings;
                    existing.Bindings = workspace.Bindings;
                    existing.Customs = workspace.Customs;
                    existing.Profiles = workspace.Profiles;
                    existing.Stacks = workspace.Stacks;
                    existing.Worktree = workspace.Worktree;
                }
            }
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
