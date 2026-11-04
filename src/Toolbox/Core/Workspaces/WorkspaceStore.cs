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
            [DataMember(Name = "workspaces")]
            public List<Workspace> Workspaces { get; set; }
        }

        private readonly List<Workspace> _items;

        private WorkspaceStore(string filePath, List<Workspace> items)
        {
            FilePath = filePath;
            _items = items;
        }

        public static string DefaultPath =>
            System.IO.Path.Combine(DataDir.Root, "workspaces.json");

        public string FilePath { get; }

        public IReadOnlyList<Workspace> Workspaces => _items;

        /// <summary>File chưa tồn tại → danh sách rỗng. File hỏng → ném lỗi, không ghi đè.</summary>
        public static WorkspaceStore Load(string filePath)
        {
            using (Lock())
            {
                if (!File.Exists(filePath))
                    return new WorkspaceStore(filePath, new List<Workspace>());
                try
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var file = Json.Read<WorkspaceFile>(stream);
                        return new WorkspaceStore(filePath, file?.Workspaces ?? new List<Workspace>());
                    }
                }
                catch (SerializationException ex)
                {
                    throw new InvalidDataException($"Không đọc được {filePath}: {ex.Message}", ex);
                }
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
                    Json.Write(stream, new WorkspaceFile { Workspaces = _items });
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

        private static IDisposable Lock()
        {
            var mutex = new Mutex(initiallyOwned: false, name: LockName);
            try
            {
                if (!mutex.WaitOne(LockTimeout))
                {
                    mutex.Dispose();
                    throw new IOException("workspaces.json đang được tiến trình khác ghi, thử lại sau.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Tiến trình trước chết khi đang giữ khoá; khoá vẫn được cấp cho ta.
            }
            return new Releaser(mutex);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly Mutex _mutex;
            public Releaser(Mutex mutex) { _mutex = mutex; }
            public void Dispose()
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
    }
}
