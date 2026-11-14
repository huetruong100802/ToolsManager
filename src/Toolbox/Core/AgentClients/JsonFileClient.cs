using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// App lưu MCP server trong một file JSON: <c>{ "mcpServers": { "toolbox": { "command", "args" } } }</c>
    /// (VS Code dùng khoá <c>servers</c> và cần <c>"type": "stdio"</c>). Toolbox ghi thẳng vào file, giữ nguyên các mục khác.
    /// </summary>
    public sealed class JsonFileClient : AgentClient
    {
        private readonly Func<string> _userPath;
        private readonly Func<bool> _detect;
        private readonly Dictionary<string, Dictionary<string, McpEntry>> _cache = new Dictionary<string, Dictionary<string, McpEntry>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="userPath">File cấu hình toàn máy của app.</param>
        /// <param name="projectFile">File cấu hình theo dự án, tương đối với thư mục workspace (".cursor\mcp.json"); null nếu app không có.</param>
        /// <param name="serversKey">Khoá chứa danh sách server: "mcpServers" hoặc "servers".</param>
        /// <param name="stdioType">Ghi thêm <c>"type": "stdio"</c> (VS Code bắt buộc).</param>
        public JsonFileClient(string id, string name, string tagline, string monogram, Func<string> userPath, string projectFile,
            string serversKey, bool stdioType, Func<bool> detect, string restartHint)
        {
            Id = id;
            Name = name;
            Tagline = tagline;
            Monogram = monogram;
            _userPath = userPath;
            ProjectFile = projectFile;
            ServersKey = serversKey;
            StdioType = stdioType;
            _detect = detect;
            RestartHint = restartHint;
        }

        public override string Id { get; }
        public override string Name { get; }
        public override string Tagline { get; }
        public override string Monogram { get; }
        public override string RestartHint { get; }
        public override bool HasProjectConfig => ProjectFile != null;
        public override bool IsInstalled => _detect();

        public string ProjectFile { get; }
        public string ServersKey { get; }
        public bool StdioType { get; }

        public override string ConfigPath(string location) =>
            location == null ? _userPath() : Path.Combine(location, ProjectFile ?? throw new InvalidOperationException(Name + " không có cấu hình theo dự án."));

        public override void Invalidate() => _cache.Clear();

        public override Dictionary<string, McpEntry> ReadServers(string location)
        {
            var path = ConfigPath(location);
            if (_cache.TryGetValue(path, out var cached))
                return new Dictionary<string, McpEntry>(cached, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);
            var root = JsonConfig.Load(path, out _);
            if (root.TryGetValue(ServersKey, out var value) && value is Dictionary<string, object> servers)
            {
                foreach (var server in servers)
                {
                    if (!(server.Value is Dictionary<string, object> entry))
                        continue;
                    result[server.Key] = new McpEntry
                    {
                        Command = entry.TryGetValue("command", out var c) ? c as string : null,
                        Args = entry.TryGetValue("args", out var a) && !(a is string) && a is System.Collections.IEnumerable items ? items.Cast<object>().OfType<string>().ToList() : new List<string>(),
                    };
                }
            }
            _cache[path] = result;
            return new Dictionary<string, McpEntry>(result, StringComparer.OrdinalIgnoreCase);
        }

        public override string Write(string location, string server, string command, IList<string> args)
        {
            var path = ConfigPath(location);
            var existed = File.Exists(path);
            var root = LoadForWrite(path);
            var servers = root.TryGetValue(ServersKey, out var value) && value is Dictionary<string, object> map ? map : null;
            if (servers == null)
                root[ServersKey] = servers = new Dictionary<string, object>();

            var entry = new Dictionary<string, object>();
            if (StdioType)
                entry["type"] = "stdio";
            entry["command"] = command;
            entry["args"] = args.Cast<object>().ToArray();
            // Giữ các trường người dùng tự thêm (env, disabled…) của khai báo cũ.
            if (servers.TryGetValue(server, out var old) && old is Dictionary<string, object> oldEntry)
                foreach (var field in oldEntry.Where(f => !entry.ContainsKey(f.Key)))
                    entry[field.Key] = field.Value;
            servers[server] = entry;

            JsonConfig.Save(path, root);
            Invalidate();
            return location != null && !existed ? GitExclude.Add(location, ProjectFile) : null;
        }

        public override void Remove(string location, string server)
        {
            var path = ConfigPath(location);
            if (!File.Exists(path))
                return;
            var root = LoadForWrite(path);
            if (root.TryGetValue(ServersKey, out var value) && value is Dictionary<string, object> servers && servers.Remove(server))
                JsonConfig.Save(path, root);
            Invalidate();
        }

        public override string Snippet(string location, string server, string command, IList<string> args)
        {
            var type = StdioType ? "\"type\": \"stdio\", " : "";
            var argList = string.Join(", ", args.Select(JsonConfig.Quote));
            return "{\n  " + JsonConfig.Quote(ServersKey) + ": {\n    " + JsonConfig.Quote(server) + ": { " + type
                + "\"command\": " + JsonConfig.Quote(command) + ", \"args\": [" + argList + "] }\n  }\n}";
        }

        private static Dictionary<string, object> LoadForWrite(string path)
        {
            var root = JsonConfig.Load(path, out var hadComments);
            if (hadComments)
                throw new InvalidDataException($"{path} có chú thích (//). Toolbox không ghi đè để khỏi mất chú thích của bạn; "
                    + "hãy dán tay đoạn cấu hình ở mục \"Tự khai báo\".");
            return root;
        }

        // ---------- Các app có sẵn ----------

        public static JsonFileClient ClaudeDesktop() => new JsonFileClient(
            "claude-desktop", "Claude Desktop", "App Claude trên máy tính · claude_desktop_config.json", "CD",
            ClaudeDesktopConfig, projectFile: null, serversKey: "mcpServers", stdioType: false,
            detect: () => AnyExists(Path.Combine(AppData, "Claude"), Path.Combine(LocalAppData, "AnthropicClaude"), ClaudeDesktopPackageDir()),
            restartHint: "Thoát hẳn Claude Desktop (cả biểu tượng ở khay hệ thống) rồi mở lại.");

        public static JsonFileClient Cursor() => new JsonFileClient(
            "cursor", "Cursor", "Trình soạn code AI · .cursor\\mcp.json", "Cu",
            () => Path.Combine(UserProfile, ".cursor", "mcp.json"), projectFile: @".cursor\mcp.json", serversKey: "mcpServers", stdioType: false,
            detect: () => AnyExists(Path.Combine(UserProfile, ".cursor"), Path.Combine(LocalAppData, "Programs", "cursor")),
            restartHint: "Cursor tự nhận file mới; nếu chưa thấy, bật server ở Settings › MCP.");

        public static JsonFileClient VsCode() => new JsonFileClient(
            "vscode", "VS Code · Copilot", "Agent mode của GitHub Copilot · .vscode\\mcp.json", "VS",
            () => Path.Combine(AppData, "Code", "User", "mcp.json"), projectFile: @".vscode\mcp.json", serversKey: "servers", stdioType: true,
            detect: () => AnyExists(Path.Combine(AppData, "Code"), Path.Combine(LocalAppData, "Programs", "Microsoft VS Code")),
            restartHint: "Trong VS Code, mở mcp.json và bấm Start trên server toolbox (hoặc chạy lệnh MCP: List Servers).");

        public static JsonFileClient Windsurf() => new JsonFileClient(
            "windsurf", "Windsurf", "Cascade của Windsurf · mcp_config.json", "Ws",
            () => Path.Combine(UserProfile, ".codeium", "windsurf", "mcp_config.json"), projectFile: null, serversKey: "mcpServers", stdioType: false,
            detect: () => AnyExists(Path.Combine(UserProfile, ".codeium", "windsurf"), Path.Combine(LocalAppData, "Programs", "Windsurf")),
            restartHint: "Trong Windsurf, bấm Refresh ở bảng MCP của Cascade.");

        public static JsonFileClient Gemini() => new JsonFileClient(
            "gemini", "Gemini CLI", "CLI của Google · .gemini\\settings.json", "Ge",
            () => Path.Combine(UserProfile, ".gemini", "settings.json"), projectFile: @".gemini\settings.json", serversKey: "mcpServers", stdioType: false,
            detect: () => AnyExists(Path.Combine(UserProfile, ".gemini")) || ClaudeCode.FindOnPath("gemini.cmd", "gemini.exe") != null,
            restartHint: "Mở lại phiên gemini; gõ /mcp để kiểm tra.");

        /// <summary>
        /// Claude Desktop cài từ gói MSIX đọc cấu hình trong thư mục ảo hoá của gói, không phải %APPDATA%\Claude.
        /// Có thư mục đó thì ghi vào đó.
        /// </summary>
        private static string ClaudeDesktopConfig()
        {
            var packaged = ClaudeDesktopPackageDir();
            var dir = packaged != null && Directory.Exists(Path.Combine(packaged, "LocalCache", "Roaming", "Claude"))
                ? Path.Combine(packaged, "LocalCache", "Roaming", "Claude")
                : Path.Combine(AppData, "Claude");
            return Path.Combine(dir, "claude_desktop_config.json");
        }

        private static string ClaudeDesktopPackageDir()
        {
            try
            {
                var packages = Path.Combine(LocalAppData, "Packages");
                return Directory.Exists(packages) ? Directory.GetDirectories(packages, "Claude_*").FirstOrDefault() : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// File cấu hình dự án do Toolbox tạo chứa đường dẫn exe và id agent của riêng máy này, nên không được commit.
    /// Thêm nó vào <c>.git/info/exclude</c> (bỏ qua cục bộ, không sửa .gitignore của repo).
    /// </summary>
    internal static class GitExclude
    {
        /// <returns>Ghi chú cho người dùng, hoặc null nếu không phải repo git.</returns>
        public static string Add(string workspace, string relativeFile)
        {
            try
            {
                var gitDir = Path.Combine(workspace, ".git");
                if (!Directory.Exists(gitDir))
                    return File.Exists(gitDir) ? $"Đã tạo {relativeFile}. Đây là worktree: đừng commit file này (chứa đường dẫn riêng của máy bạn)." : null;
                var exclude = Path.Combine(gitDir, "info", "exclude");
                var pattern = "/" + relativeFile.Replace('\\', '/');
                var lines = File.Exists(exclude) ? File.ReadAllLines(exclude) : new string[0];
                if (lines.Any(l => l.Trim() == pattern))
                    return null;
                Directory.CreateDirectory(Path.GetDirectoryName(exclude));
                var prefix = File.Exists(exclude) && new FileInfo(exclude).Length > 0 && !File.ReadAllText(exclude).EndsWith("\n") ? Environment.NewLine : "";
                File.AppendAllText(exclude, prefix + "# Toolbox: cấu hình MCP riêng của máy này" + Environment.NewLine + pattern + Environment.NewLine, new UTF8Encoding(false));
                return $"Đã thêm {pattern} vào .git/info/exclude để file không bị commit.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return $"Không ghi được .git/info/exclude ({ex.Message}); đừng commit {relativeFile}.";
            }
        }
    }
}
