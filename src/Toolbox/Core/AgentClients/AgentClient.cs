using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// Một app agent dùng được Toolbox qua MCP (Claude Code, Cursor, VS Code…): nơi nó lưu khai báo MCP server và cách ghi vào đó.
    /// "Chỗ" (<c>location</c>) là null cho cấu hình toàn máy của app, hoặc đường dẫn thư mục workspace cho cấu hình theo dự án.
    /// </summary>
    public abstract class AgentClient
    {
        /// <summary>Tên server mặc định trong cấu hình MCP của app.</summary>
        public const string ServerName = "toolbox";

        /// <summary>Id ổn định, lưu trong <c>agents.json</c> (trường <c>client</c>).</summary>
        public abstract string Id { get; }
        public abstract string Name { get; }

        /// <summary>Một dòng giới thiệu ngắn: app gì, ghi vào đâu.</summary>
        public abstract string Tagline { get; }

        /// <summary>Hai ký tự hiện trên ô biểu tượng (không dùng logo của hãng).</summary>
        public abstract string Monogram { get; }

        /// <summary>Thấy app trên máy này (thư mục cấu hình hoặc file chạy). Chưa thấy vẫn ghi cấu hình sẵn được.</summary>
        public abstract bool IsInstalled { get; }

        /// <summary>
        /// true: app có cấu hình riêng cho từng thư mục dự án, kết nối workspace ghi vào đó với tên <see cref="ServerName"/>.
        /// false: app chỉ có cấu hình toàn máy, kết nối workspace là một server riêng tên <c>toolbox-&lt;workspace&gt;</c>.
        /// </summary>
        public abstract bool HasProjectConfig { get; }

        /// <summary>Không tự ghi được: người dùng copy cấu hình rồi tự dán vào app.</summary>
        public virtual bool IsManual => false;

        /// <summary>Việc người dùng cần làm để app nhận thay đổi (mở lại phiên, bật server…).</summary>
        public abstract string RestartHint { get; }

        /// <summary>File cấu hình của chỗ này, để hiện và mở; null nếu không có file cố định.</summary>
        public abstract string ConfigPath(string location);

        /// <summary>Các server MCP đã khai báo ở chỗ này (theo tên). File chưa có thì rỗng; file hỏng thì ném <see cref="InvalidDataException"/>.</summary>
        public abstract Dictionary<string, McpEntry> ReadServers(string location);

        /// <summary>Khai báo (hoặc thay) server. Trả về ghi chú cho người dùng, hoặc null.</summary>
        public abstract string Write(string location, string server, string command, IList<string> args);

        public abstract void Remove(string location, string server);

        /// <summary>Đoạn cấu hình để người dùng tự dán (đúng định dạng của app).</summary>
        public abstract string Snippet(string location, string server, string command, IList<string> args);

        /// <summary>Các thư mục app đã mở (Claude Code nhớ theo project). Mặc định không có.</summary>
        public virtual IEnumerable<string> KnownWorkspaces() => Enumerable.Empty<string>();

        /// <summary>Bỏ dữ liệu đọc đệm để lần đọc sau lấy từ file.</summary>
        public virtual void Invalidate() { }

        protected static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        protected static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        protected static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        protected static bool AnyExists(params string[] paths) =>
            paths.Any(p => !string.IsNullOrEmpty(p) && (Directory.Exists(p) || File.Exists(p)));

        /// <summary>Dòng lệnh từ lệnh và tham số; tham số có dấu cách được bọc nháy.</summary>
        public static string CommandLine(string command, IEnumerable<string> args) =>
            string.Join(" ", new[] { command }.Concat(args).Select(a => a.IndexOf(' ') >= 0 || a.Length == 0 ? "\"" + a + "\"" : a));
    }

    /// <summary>App không tự ghi được (thiếu CLI…): kèm lệnh hoặc cấu hình để người dùng tự làm.</summary>
    public sealed class ClientUnavailableException : InvalidOperationException
    {
        public ClientUnavailableException(string message, string manualStep) : base(message)
        {
            ManualStep = manualStep;
        }

        public string ManualStep { get; }
    }

    /// <summary>Phạm vi kết nối: toàn máy (<see cref="Path"/> null) hoặc một workspace.</summary>
    public sealed class ConnectTarget
    {
        public static readonly ConnectTarget Global = new ConnectTarget();

        /// <summary>Thư mục workspace; null = toàn máy.</summary>
        public string Path { get; set; }

        /// <summary>Id workspace trong Toolbox; null nếu thư mục chưa đăng ký.</summary>
        public string WorkspaceId { get; set; }

        /// <summary>Tên workspace, dùng đặt tên server cho app chỉ có cấu hình toàn máy.</summary>
        public string Name { get; set; }

        public bool IsGlobal => string.IsNullOrEmpty(Path);
    }

    public sealed class ConnectionState
    {
        public AgentLink Link { get; set; }

        /// <summary>Tên server đang khai báo, hoặc sẽ được ghi khi kết nối.</summary>
        public string Server { get; set; }

        public McpEntry Entry { get; set; }

        /// <summary>Vì sao là <see cref="AgentLink.Stale"/>.</summary>
        public string Reason { get; set; }

        /// <summary>Lỗi đọc cấu hình (file hỏng…); null nếu đọc được.</summary>
        public string Error { get; set; }
    }

    /// <summary>Trạng thái, kết nối và gỡ Toolbox trong một app agent, chung cho mọi app.</summary>
    public static class AgentConnections
    {
        /// <summary>Chỗ ghi khai báo của phạm vi này trong app: thư mục workspace (app có cấu hình dự án) hoặc toàn máy.</summary>
        public static string Location(AgentClient client, ConnectTarget target) =>
            !target.IsGlobal && client.HasProjectConfig ? target.Path : null;

        /// <param name="exes">Exe được coi là đúng. Rỗng: có khai báo là được, không kiểm exe.</param>
        public static ConnectionState State(AgentClient client, ConnectTarget target, params string[] exes)
        {
            if (client.IsManual)
                return new ConnectionState { Link = AgentLink.NotConnected, Server = ServerFor(new Dictionary<string, McpEntry>(), client, target) };

            Dictionary<string, McpEntry> servers;
            try
            {
                servers = client.ReadServers(Location(client, target));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ConnectionState { Link = AgentLink.NotConnected, Server = ServerName(target), Error = ex.Message };
            }

            var server = ServerFor(servers, client, target);
            servers.TryGetValue(server, out var entry);
            var state = new ConnectionState { Server = server, Entry = entry };
            if (entry == null)
                state.Link = AgentLink.NotConnected;
            else if (!entry.PointsTo(exes))
                Stale(state, "Trỏ tới exe khác");
            else if (target.IsGlobal ? entry.HasWorkspaceScope : target.WorkspaceId == null)
                Stale(state, target.IsGlobal ? "Kết nối cũ, cần kết nối lại" : "Chưa đăng ký workspace");
            else if (!target.IsGlobal && !entry.ScopedTo(target.WorkspaceId))
                Stale(state, "Kết nối cũ, cần kết nối lại");
            else
                state.Link = AgentLink.Connected;
            return state;
        }

        /// <summary>Tham số khởi chạy MCP server cho phạm vi này.</summary>
        public static List<string> Args(ConnectTarget target, string agentId)
        {
            var args = new List<string> { "--mcp" };
            if (!target.IsGlobal)
                args.AddRange(new[] { "--workspace", target.WorkspaceId });
            if (!string.IsNullOrEmpty(agentId))
                args.AddRange(new[] { "--agent", agentId });
            return args;
        }

        /// <summary>Ghi khai báo cho phạm vi (workspace phải đã đăng ký). Trả về ghi chú của app, hoặc null.</summary>
        public static string Connect(AgentClient client, ConnectTarget target, string exe, string agentId)
        {
            if (!target.IsGlobal && string.IsNullOrEmpty(target.WorkspaceId))
                throw new ArgumentException("Workspace cần được đăng ký trong Toolbox trước khi kết nối.");
            var state = State(client, target);
            if (state.Error != null)
                throw new InvalidDataException(state.Error);
            var note = client.Write(Location(client, target), state.Server, exe, Args(target, agentId));
            client.Invalidate();
            return note;
        }

        public static void Disconnect(AgentClient client, ConnectTarget target)
        {
            var state = State(client, target);
            if (state.Error != null)
                throw new InvalidDataException(state.Error);
            if (state.Entry != null)
                client.Remove(Location(client, target), state.Server);
            client.Invalidate();
        }

        public static string Snippet(AgentClient client, ConnectTarget target, string exe, string agentId)
        {
            var state = State(client, target);
            return client.Snippet(Location(client, target), state.Server, exe, Args(target, agentId));
        }

        private static void Stale(ConnectionState state, string reason)
        {
            state.Link = AgentLink.Stale;
            state.Reason = reason;
        }

        private static string ServerName(ConnectTarget target) => target.IsGlobal ? AgentClient.ServerName : AgentClient.ServerName + "-" + Slug(target.Name);

        /// <summary>
        /// Tên server của phạm vi. Toàn máy hoặc app có cấu hình dự án: "toolbox". App chỉ có cấu hình toàn máy: server đang giới hạn
        /// đúng workspace này nếu đã có, không thì "toolbox-&lt;tên&gt;" (thêm id nếu tên đã bị workspace khác dùng).
        /// </summary>
        private static string ServerFor(Dictionary<string, McpEntry> servers, AgentClient client, ConnectTarget target)
        {
            if (target.IsGlobal || client.HasProjectConfig)
                return AgentClient.ServerName;
            if (!string.IsNullOrEmpty(target.WorkspaceId))
            {
                var existing = servers.FirstOrDefault(s => s.Value.ScopedTo(target.WorkspaceId));
                if (existing.Key != null)
                    return existing.Key;
            }
            var name = ServerName(target);
            return servers.ContainsKey(name) && !string.IsNullOrEmpty(target.WorkspaceId) ? name + "-" + Slug(target.WorkspaceId) : name;
        }

        /// <summary>"Dự án API (v2)" → "du-an-api-v2": chữ thường không dấu, số và gạch ngang.</summary>
        public static string Slug(string name)
        {
            var folded = Text.Fold(name ?? "");
            var chars = folded.Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '-').ToArray();
            var slug = string.Join("-", new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            if (slug.Length > 32)
                slug = slug.Substring(0, 32).TrimEnd('-');
            return slug.Length == 0 ? "workspace" : slug;
        }
    }
}
