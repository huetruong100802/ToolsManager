using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Toolbox.Core
{
    public enum AgentLink
    {
        NotConnected,
        Connected,
        /// <summary>Có khai báo nhưng trỏ tới exe khác hoặc file không còn.</summary>
        Stale,
    }

    /// <summary>Một khai báo MCP server trong ~/.claude.json (toàn máy hoặc một workspace).</summary>
    public sealed class McpEntry
    {
        /// <summary>Lệnh của server; null nếu khai báo không có command (vd kiểu url).</summary>
        public string Command { get; set; }

        /// <summary>
        /// Các cách viết của workspace trong ~/.claude.json đang có server này
        /// (Claude Code lưu theo thư mục lúc chạy nên "C:/a" và "c:/a" là hai key khác nhau).
        /// </summary>
        public List<string> ConfiguredKeys { get; } = new List<string>();
    }

    /// <summary>Một chỗ Claude Code dùng được MCP: toàn máy (scope user) hoặc một workspace (scope local).</summary>
    public sealed class ClaudeTarget
    {
        /// <summary>null = toàn máy.</summary>
        public string Workspace { get; set; }

        /// <summary>Khai báo theo tên server (toolbox, desk-mcp…).</summary>
        public Dictionary<string, McpEntry> Servers { get; } = new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);

        public McpEntry Entry(string server) => Servers.TryGetValue(server, out var entry) ? entry : null;

        /// <param name="exes">Các exe được coi là đúng. Rỗng = không kiểm tra exe, có khai báo là đã kết nối.</param>
        public AgentLink LinkTo(string server, params string[] exes)
        {
            var entry = Entry(server);
            if (entry == null)
                return AgentLink.NotConnected;
            if (exes.Length == 0)
                return AgentLink.Connected;
            return entry.Command != null && File.Exists(entry.Command) && exes.Any(e => string.Equals(e, entry.Command, StringComparison.OrdinalIgnoreCase))
                ? AgentLink.Connected
                : AgentLink.Stale;
        }
    }

    public sealed class ClaudeConfig
    {
        public ClaudeTarget User { get; } = new ClaudeTarget();
        public List<ClaudeTarget> Workspaces { get; } = new List<ClaudeTarget>();
    }

    /// <summary>
    /// Kết nối MCP server vào Claude Code. Trạng thái đọc từ ~/.claude.json; ghi thì qua CLI `claude mcp add/remove`,
    /// vì Claude Code tự quản file đó và các phiên đang mở cũng ghi vào.
    /// </summary>
    public static class ClaudeCode
    {
        public const string ToolboxServer = "toolbox";

        private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(60);

        private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static string DefaultConfigPath => Path.Combine(UserProfile, ".claude.json");

        /// <summary>Đọc mọi khai báo MCP toàn máy và theo từng workspace Claude Code đã mở (chỉ giữ thư mục còn tồn tại).</summary>
        public static ClaudeConfig ReadConfig(string path)
        {
            var config = new ClaudeConfig();
            if (!File.Exists(path))
                return config;

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new InvalidDataException($"Không đọc được cấu hình Claude Code {path}: {ex.Message}", ex);
            }
            if (root == null)
                return config;

            foreach (var server in ServersOf(root))
                config.User.Servers[server.Key] = server.Value;

            if (root.TryGetValue("projects", out var value) && value is Dictionary<string, object> projects)
            {
                foreach (var group in projects.GroupBy(p => DisplayPath(p.Key), StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(group.Key))
                        continue;
                    var target = new ClaudeTarget { Workspace = group.Key };
                    foreach (var project in group)
                    {
                        foreach (var server in ServersOf(project.Value as Dictionary<string, object>))
                        {
                            if (!target.Servers.TryGetValue(server.Key, out var entry))
                                target.Servers[server.Key] = entry = new McpEntry { Command = server.Value.Command };
                            entry.Command = entry.Command ?? server.Value.Command;
                            entry.ConfiguredKeys.Add(project.Key.Replace('/', '\\'));
                        }
                    }
                    config.Workspaces.Add(target);
                }
            }
            return config;
        }

        /// <summary>Tìm `claude` trên PATH (bản cài native là .exe, bản npm là .cmd), rồi tới chỗ cài mặc định.</summary>
        public static string FindCli()
        {
            var found = FindOnPath("claude.exe", "claude.cmd");
            if (found != null)
                return found;
            return FindIn(Path.Combine(UserProfile, ".local", "bin"), "claude.exe", "claude.cmd")
                ?? FindIn(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), "claude.exe", "claude.cmd");
        }

        /// <summary>Tìm một file thực thi trên PATH; null nếu không có.</summary>
        public static string FindOnPath(params string[] names)
        {
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Select(d => d.Trim().Trim('"'))
                .Where(d => d.Length > 0);
            return dirs.Select(d => FindIn(d, names)).FirstOrDefault(f => f != null);
        }

        private static string FindIn(string dir, params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Mục PATH chứa ký tự không hợp lệ: bỏ qua.
                }
            }
            return null;
        }

        /// <param name="commandArgs">Phần sau "--": lệnh khởi chạy server và tham số của nó.</param>
        public static string AddArguments(string server, string workspace, string commandArgs) =>
            $"mcp add --scope {Scope(workspace)} {server} -- {commandArgs}";

        public static string RemoveArguments(string server, string workspace) =>
            $"mcp remove --scope {Scope(workspace)} {server}";

        /// <summary>Scope local lấy workspace theo thư mục làm việc của lệnh claude.</summary>
        public static ProcessStartInfo CreateCliStartInfo(string cli, string arguments, string workspace)
        {
            var info = cli.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(cli, arguments)
                : new ProcessStartInfo("cmd.exe", $"/d /s /c \"\"{cli}\" {arguments}\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.WorkingDirectory = workspace ?? UserProfile;
            return info;
        }

        /// <summary>Khai báo server cho <paramref name="target"/>; khai báo cũ của cùng server bị gỡ trước.</summary>
        public static void Connect(string cli, ClaudeTarget target, string server, string commandArgs)
        {
            if (target.Entry(server) != null)
                Disconnect(cli, target, server);
            RunCli(cli, AddArguments(server, target.Workspace, commandArgs), target.Workspace);
        }

        public static void Disconnect(string cli, ClaudeTarget target, string server)
        {
            if (target.Workspace == null)
            {
                RunCli(cli, RemoveArguments(server, null), null);
                return;
            }
            foreach (var key in target.Entry(server)?.ConfiguredKeys ?? new List<string>())
                RunCli(cli, RemoveArguments(server, key), key);
        }

        public static void RunCli(string cli, string arguments, string workspace)
        {
            var result = Runner.Capture(CreateCliStartInfo(cli, arguments, workspace), CliTimeout);
            if (result.TimedOut)
                throw new InvalidOperationException($"claude {arguments}: không phản hồi sau {CliTimeout.TotalSeconds:0} giây.");
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"claude {arguments}:\n{(result.Error + "\n" + result.Output).Trim()}");
        }

        private static string Scope(string workspace) => workspace == null ? "user" : "local";

        /// <summary>"c:/Repo/App/" → "C:\Repo\App", để gộp các cách viết của cùng một thư mục.</summary>
        private static string DisplayPath(string key)
        {
            var path = key.Replace('/', '\\');
            if (path.Length > 3)
                path = path.TrimEnd('\\');
            if (path.Length >= 2 && path[1] == ':')
                path = char.ToUpperInvariant(path[0]) + path.Substring(1);
            return path;
        }

        /// <summary>Mọi server trong mục mcpServers của một scope (toàn máy hoặc một project).</summary>
        private static Dictionary<string, McpEntry> ServersOf(Dictionary<string, object> scope)
        {
            var result = new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);
            if (scope == null || !scope.TryGetValue("mcpServers", out var servers) || !(servers is Dictionary<string, object> byName))
                return result;
            foreach (var server in byName)
            {
                if (!(server.Value is Dictionary<string, object> entry))
                    continue;
                var command = entry.TryGetValue("command", out var c) ? c as string : null;
                result[server.Key] = new McpEntry { Command = command };
            }
            return result;
        }
    }
}
