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
        /// <summary>Có khai báo "toolbox" nhưng trỏ tới exe khác hoặc file không còn.</summary>
        Stale,
    }

    /// <summary>Một chỗ Claude Code dùng được Toolbox: toàn máy (scope user) hoặc một workspace (scope local).</summary>
    public sealed class ClaudeTarget
    {
        /// <summary>null = toàn máy.</summary>
        public string Workspace { get; set; }

        /// <summary>Lệnh của MCP server "toolbox" đang khai báo; null nếu chưa có.</summary>
        public string Command { get; set; }

        /// <summary>
        /// Các cách viết của workspace trong ~/.claude.json đang có server "toolbox"
        /// (Claude Code lưu theo thư mục lúc chạy nên "C:/a" và "c:/a" là hai key khác nhau).
        /// </summary>
        public List<string> ConfiguredKeys { get; } = new List<string>();

        public AgentLink LinkTo(params string[] toolboxExes)
        {
            if (Command == null)
                return AgentLink.NotConnected;
            return File.Exists(Command) && toolboxExes.Any(e => string.Equals(e, Command, StringComparison.OrdinalIgnoreCase))
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
    /// Kết nối Toolbox vào Claude Code. Trạng thái đọc từ ~/.claude.json; ghi thì qua CLI `claude mcp add/remove`,
    /// vì Claude Code tự quản file đó và các phiên đang mở cũng ghi vào.
    /// </summary>
    public static class ClaudeCode
    {
        public const string ServerName = "toolbox";

        private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(60);

        private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static string DefaultConfigPath => Path.Combine(UserProfile, ".claude.json");

        /// <summary>Đọc khai báo "toolbox" toàn máy và theo từng workspace Claude Code đã mở (chỉ giữ thư mục còn tồn tại).</summary>
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

            config.User.Command = CommandOf(root);
            if (root.TryGetValue("projects", out var value) && value is Dictionary<string, object> projects)
            {
                foreach (var group in projects.GroupBy(p => DisplayPath(p.Key), StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(group.Key))
                        continue;
                    var target = new ClaudeTarget { Workspace = group.Key };
                    foreach (var project in group)
                    {
                        var command = CommandOf(project.Value as Dictionary<string, object>);
                        if (command == null)
                            continue;
                        target.Command = target.Command ?? command;
                        target.ConfiguredKeys.Add(project.Key.Replace('/', '\\'));
                    }
                    config.Workspaces.Add(target);
                }
            }
            return config;
        }

        /// <summary>Tìm `claude` trên PATH (bản cài native là .exe, bản npm là .cmd), rồi tới chỗ cài mặc định.</summary>
        public static string FindCli()
        {
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Select(d => d.Trim().Trim('"'))
                .Where(d => d.Length > 0)
                .Concat(new[]
                {
                    Path.Combine(UserProfile, ".local", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
                });
            foreach (var dir in dirs)
            {
                foreach (var name in new[] { "claude.exe", "claude.cmd" })
                {
                    try
                    {
                        var candidate = Path.Combine(dir, name);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch (ArgumentException)
                    {
                        // PATH có mục chứa ký tự không hợp lệ: bỏ qua.
                    }
                }
            }
            return null;
        }

        public static string AddArguments(string workspace, string exe) =>
            $"mcp add --scope {Scope(workspace)} {ServerName} -- \"{exe}\" --mcp";

        public static string RemoveArguments(string workspace) =>
            $"mcp remove --scope {Scope(workspace)} {ServerName}";

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

        /// <summary>Khai báo Toolbox cho <paramref name="target"/>; khai báo cũ (trỏ exe khác) bị gỡ trước.</summary>
        public static void Connect(string cli, ClaudeTarget target, string exe)
        {
            if (target.Command != null)
                Disconnect(cli, target);
            RunCli(cli, AddArguments(target.Workspace, exe), target.Workspace);
        }

        public static void Disconnect(string cli, ClaudeTarget target)
        {
            if (target.Workspace == null)
            {
                RunCli(cli, RemoveArguments(null), null);
                return;
            }
            foreach (var key in target.ConfiguredKeys)
                RunCli(cli, RemoveArguments(key), key);
        }

        private static void RunCli(string cli, string arguments, string workspace)
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

        private static string CommandOf(Dictionary<string, object> scope) =>
            scope != null
            && scope.TryGetValue("mcpServers", out var servers) && servers is Dictionary<string, object> byName
            && byName.TryGetValue(ServerName, out var server) && server is Dictionary<string, object> entry
            && entry.TryGetValue("command", out var command)
                ? command as string
                : null;
    }
}
