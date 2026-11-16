using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// Codex CLI: server MCP nằm trong <c>~/.codex/config.toml</c> (hoặc <c>%CODEX_HOME%</c>) dạng bảng <c>[mcp_servers.&lt;tên&gt;]</c>.
    /// Toolbox chỉ thay đúng bảng của server mình, giữ nguyên phần còn lại của file.
    /// </summary>
    public sealed class CodexClient : AgentClient
    {
        private static readonly Regex Header = new Regex(@"^\s*\[\s*mcp_servers\.(?:""(?<q>[^""]+)""|(?<b>[A-Za-z0-9_-]+))(?<sub>\.[^\]]*)?\s*\]\s*(#.*)?$");
        private static readonly Regex AnyHeader = new Regex(@"^\s*\[");

        private readonly Func<string> _configPath;

        public CodexClient(Func<string> configPath = null)
        {
            _configPath = configPath ?? DefaultConfigPath;
        }

        public override string Id => "codex";
        public override string Name => "Codex CLI";
        public override string Tagline => "CLI của OpenAI · ~/.codex/config.toml";
        public override string Monogram => "Cx";
        public override bool HasProjectConfig => false;
        public override string RestartHint => "Mở lại phiên codex; gõ /mcp để kiểm tra.";

        public override bool IsInstalled =>
            AnyExists(Path.GetDirectoryName(_configPath())) || ClaudeCode.FindOnPath("codex.cmd", "codex.exe") != null;

        private static string DefaultConfigPath()
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.Combine(string.IsNullOrWhiteSpace(home) ? Path.Combine(UserProfile, ".codex") : home, "config.toml");
        }

        public override string ConfigPath(string location) => _configPath();

        public override Dictionary<string, McpEntry> ReadServers(string location)
        {
            var result = new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);
            var path = _configPath();
            if (!File.Exists(path))
                return result;

            McpEntry current = null;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var header = Header.Match(line);
                if (header.Success)
                {
                    var name = header.Groups["q"].Success ? header.Groups["q"].Value : header.Groups["b"].Value;
                    if (header.Groups["sub"].Success && header.Groups["sub"].Length > 0)
                    {
                        current = null; // [mcp_servers.x.env]: không có command/args
                        continue;
                    }
                    if (!result.TryGetValue(name, out current))
                        result[name] = current = new McpEntry();
                    continue;
                }
                if (AnyHeader.IsMatch(line))
                {
                    current = null;
                    continue;
                }
                if (current == null)
                    continue;

                var pair = Regex.Match(line, @"^\s*(?<key>command|args)\s*=\s*(?<value>.*)$");
                if (!pair.Success)
                    continue;
                var raw = pair.Groups["value"].Value;
                if (pair.Groups["key"].Value == "command")
                {
                    current.Command = Strings(raw).FirstOrDefault();
                    continue;
                }
                // Mảng nhiều dòng: gom tới dấu ].
                var text = new StringBuilder(raw);
                while (!ClosesArray(text.ToString()) && i + 1 < lines.Length)
                    text.Append('\n').Append(lines[++i]);
                current.Args = Strings(text.ToString()).ToList();
            }
            return result;
        }

        public override string Write(string location, string server, string command, IList<string> args)
        {
            var path = _configPath();
            var kept = Without(path, server);
            while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0)
                kept.RemoveAt(kept.Count - 1);
            if (kept.Count > 0)
                kept.Add("");
            kept.AddRange(Table(server, command, args).Split('\n'));
            Save(path, kept);
            return null;
        }

        public override void Remove(string location, string server)
        {
            var path = _configPath();
            if (File.Exists(path))
                Save(path, Without(path, server));
        }

        public override string Snippet(string location, string server, string command, IList<string> args) => Table(server, command, args);

        private static string Table(string server, string command, IList<string> args) =>
            $"[mcp_servers.{Key(server)}]\ncommand = {TomlString(command)}\nargs = [{string.Join(", ", args.Select(TomlString))}]";

        private static string Key(string name) => Regex.IsMatch(name, "^[A-Za-z0-9_-]+$") ? name : TomlString(name);

        /// <summary>Chuỗi TOML cơ bản: escape \ và ".</summary>
        private static string TomlString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Các dòng của file, bỏ bảng <c>[mcp_servers.server]</c> và bảng con của nó (kèm dòng trống ngay trước).</summary>
        private static List<string> Without(string path, string server)
        {
            var kept = new List<string>();
            if (!File.Exists(path))
                return kept;
            var skipping = false;
            foreach (var line in File.ReadAllLines(path))
            {
                var header = Header.Match(line);
                if (header.Success)
                {
                    var name = header.Groups["q"].Success ? header.Groups["q"].Value : header.Groups["b"].Value;
                    var wasSkipping = skipping;
                    skipping = string.Equals(name, server, StringComparison.OrdinalIgnoreCase);
                    if (skipping)
                    {
                        while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0)
                            kept.RemoveAt(kept.Count - 1);
                        continue;
                    }
                    if (wasSkipping && kept.Count > 0)
                        kept.Add("");
                }
                else if (AnyHeader.IsMatch(line))
                {
                    if (skipping && kept.Count > 0)
                        kept.Add("");
                    skipping = false;
                }
                if (!skipping)
                    kept.Add(line);
            }
            return kept;
        }

        private static void Save(string path, List<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".toolbox.tmp";
            File.WriteAllText(temp, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, path + ".toolbox.bak");
            else
                File.Move(temp, path);
        }

        private static bool ClosesArray(string text)
        {
            var inString = '\0';
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inString != '\0')
                {
                    if (c == '\\' && inString == '"')
                        i++;
                    else if (c == inString)
                        inString = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                    inString = c;
                else if (c == ']')
                    return true;
                else if (c == '#')
                    return false;
            }
            return false;
        }

        /// <summary>Các chuỗi TOML ("..." hoặc '...') trong một giá trị, theo thứ tự.</summary>
        private static IEnumerable<string> Strings(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '#')
                    yield break;
                if (c == '\'')
                {
                    var end = value.IndexOf('\'', i + 1);
                    if (end < 0)
                        yield break;
                    yield return value.Substring(i + 1, end - i - 1);
                    i = end;
                }
                else if (c == '"')
                {
                    var sb = new StringBuilder();
                    for (i++; i < value.Length && value[i] != '"'; i++)
                    {
                        if (value[i] == '\\' && i + 1 < value.Length)
                        {
                            var next = value[++i];
                            sb.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next);
                        }
                        else
                        {
                            sb.Append(value[i]);
                        }
                    }
                    yield return sb.ToString();
                }
            }
        }
    }
}
