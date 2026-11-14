using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace Toolbox.Core.Agents
{
    /// <summary>Cách agent được chạy lệnh: tự chạy cái đã có, hỏi trước mỗi lần, hoặc không cho chạy.</summary>
    public static class RunModes
    {
        public const string Auto = "auto";
        public const string Ask = "ask";
        public const string None = "none";

        public static string Normalize(string value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            return v == Ask || v == None ? v : Auto;
        }
    }

    /// <summary>Một agent đã đăng ký. WorkspaceId rỗng nghĩa là agent toàn máy (chỉ quản lý thư viện).</summary>
    [DataContract]
    public class AgentRecord
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "client")] public string Client { get; set; }
        [DataMember(Name = "workspaceId")] public string WorkspaceId { get; set; }
        [DataMember(Name = "runMode")] public string RunMode { get; set; }
        [DataMember(Name = "createdAt")] public string CreatedAt { get; set; }
        [DataMember(Name = "lastSeenAt")] public string LastSeenAt { get; set; }

        /// <summary>Tên và phiên bản app agent tự báo lúc bắt tay MCP (clientInfo), ví dụ "cursor-vscode" "1.7.2".</summary>
        [DataMember(Name = "clientName", EmitDefaultValue = false)] public string ClientName { get; set; }
        [DataMember(Name = "clientVersion", EmitDefaultValue = false)] public string ClientVersion { get; set; }

        /// <summary>Các lệnh người dùng chọn "Luôn cho lệnh này" (chuỗi chuẩn hoá của <see cref="RunApprovals.Print"/>): chạy luôn, không hỏi lại.</summary>
        [DataMember(Name = "allowRuns", EmitDefaultValue = false)] public List<string> AllowRuns { get; set; }

        public bool IsGlobal => string.IsNullOrEmpty(WorkspaceId);
    }

    /// <summary>
    /// Đọc, sửa, ghi một danh sách trong file JSON với khoá liên tiến trình, dùng chung cho agents, đề xuất.
    /// File chưa có thì danh sách rỗng; file hỏng thì ném lỗi, không ghi đè.
    /// </summary>
    internal static class ListFile
    {
        [DataContract]
        private class Doc<T>
        {
            [DataMember(Name = "items")] public List<T> Items { get; set; }
        }

        public static List<T> Load<T>(string path, string lockName)
        {
            using (Lock(lockName))
            {
                if (!File.Exists(path))
                    return new List<T>();
                try
                {
                    using (var stream = File.OpenRead(path))
                        return Json.Read<Doc<T>>(stream)?.Items ?? new List<T>();
                }
                catch (SerializationException ex)
                {
                    throw new InvalidDataException($"Không đọc được {path}: {ex.Message}", ex);
                }
            }
        }

        public static void Save<T>(string path, string lockName, List<T> items)
        {
            using (Lock(lockName))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                var temp = path + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, new Doc<T> { Items = items });
                if (File.Exists(path))
                    File.Replace(temp, path, destinationBackupFileName: null);
                else
                    File.Move(temp, path);
            }
        }

        public static IDisposable Lock(string name)
        {
            var mutex = new Mutex(initiallyOwned: false, name: name);
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                {
                    mutex.Dispose();
                    throw new IOException("File đang được tiến trình khác ghi, thử lại sau.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Tiến trình trước chết khi đang giữ khoá; khoá vẫn được cấp cho ta.
            }
            return new Releaser(mutex);
        }

        public static string Now() => DateTime.UtcNow.ToString("o");

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

    /// <summary>Danh sách agent, lưu trong agents.json cạnh tools.json. MCP đọc lại mỗi lần gọi nên đổi quyền trong GUI có hiệu lực ngay.</summary>
    public class AgentStore
    {
        private const string LockName = @"Global\ToolboxAgents";
        private readonly List<AgentRecord> _items;

        private AgentStore(string filePath, List<AgentRecord> items)
        {
            FilePath = filePath;
            _items = items;
        }

        public static string DefaultPath =>
            Path.Combine(DataDir.Root, "agents.json");

        public string FilePath { get; }
        public IReadOnlyList<AgentRecord> Agents => _items;

        public static AgentStore Load(string filePath) => new AgentStore(filePath, ListFile.Load<AgentRecord>(filePath, LockName));

        public void Save() => ListFile.Save(FilePath, LockName, _items);

        /// <summary>Đọc-sửa-ghi trong một khoá: ghi nếu <paramref name="change"/> trả về true.</summary>
        public static void Update(string filePath, Func<AgentStore, bool> change)
        {
            using (ListFile.Lock(LockName))
            {
                var store = Load(filePath);
                if (change(store))
                    store.Save();
            }
        }

        public AgentRecord Find(string id) =>
            string.IsNullOrEmpty(id) ? null : _items.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Thêm agent: cấp id, chuẩn hoá chế độ chạy. Không ghi file; gọi <see cref="Save"/>.</summary>
        public AgentRecord Add(AgentRecord agent)
        {
            if (string.IsNullOrWhiteSpace(agent.Name))
                throw new ArgumentException("Agent cần có tên.");
            agent.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            agent.RunMode = RunModes.Normalize(agent.RunMode);
            agent.CreatedAt = ListFile.Now();
            _items.Add(agent);
            return agent;
        }

        public bool Remove(string id)
        {
            var found = Find(id);
            return found != null && _items.Remove(found);
        }

        /// <summary>Ghi lại lần cuối agent hoạt động. Không có agent thì bỏ qua.</summary>
        public static void Touch(string filePath, string id, string clientName = null, string clientVersion = null) =>
            Update(filePath, store =>
            {
                var agent = store.Find(id);
                if (agent == null)
                    return false;
                agent.LastSeenAt = ListFile.Now();
                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    agent.ClientName = clientName.Trim();
                    agent.ClientVersion = string.IsNullOrWhiteSpace(clientVersion) ? null : clientVersion.Trim();
                }
                return true;
            });
    }
}
