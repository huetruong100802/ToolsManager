using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// Claude Code: đọc trạng thái từ ~/.claude.json, ghi qua <c>claude mcp add/remove</c> (scope user cho toàn máy, local cho workspace)
    /// vì Claude Code tự quản file đó và các phiên đang mở cũng ghi vào.
    /// </summary>
    public sealed class ClaudeCodeClient : AgentClient
    {
        private readonly string _configPath;
        private readonly Func<string> _findCli;
        private ClaudeConfig _config;

        public ClaudeCodeClient(string configPath = null, Func<string> findCli = null)
        {
            _configPath = configPath ?? ClaudeCode.DefaultConfigPath;
            _findCli = findCli ?? ClaudeCode.FindCli;
        }

        /// <summary>Giữ id "claude" như các bản trước để agent đã đăng ký vẫn khớp.</summary>
        public override string Id => "claude";
        public override string Name => "Claude Code";
        public override string Tagline => "CLI của Anthropic · ghi bằng lệnh claude mcp add";
        public override string Monogram => "CC";
        public override bool HasProjectConfig => true;
        public override bool IsInstalled => _findCli() != null || System.IO.File.Exists(_configPath);
        public override string RestartHint => "Mở lại phiên Claude Code đang chạy; gõ /mcp để kiểm tra.";

        public override string ConfigPath(string location) => _configPath;

        public override void Invalidate() => _config = null;

        private ClaudeConfig Config => _config ?? (_config = ClaudeCode.ReadConfig(_configPath));

        public override Dictionary<string, McpEntry> ReadServers(string location)
        {
            var target = Target(location);
            return target == null
                ? new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, McpEntry>(target.Servers, StringComparer.OrdinalIgnoreCase);
        }

        public override IEnumerable<string> KnownWorkspaces() => Config.Workspaces.Select(w => w.Workspace);

        public override string Write(string location, string server, string command, IList<string> args)
        {
            var commandArgs = CommandLine(command, args);
            var cli = Cli(Snippet(location, server, command, args));
            ClaudeCode.Connect(cli, Target(location) ?? new ClaudeTarget { Workspace = location }, server, commandArgs);
            Invalidate();
            return null;
        }

        public override void Remove(string location, string server)
        {
            var target = Target(location);
            if (target?.Entry(server) == null)
                return;
            ClaudeCode.Disconnect(Cli("claude " + ClaudeCode.RemoveArguments(server, location)), target, server);
            Invalidate();
        }

        public override string Snippet(string location, string server, string command, IList<string> args) =>
            (location != null ? $"cd \"{location}\"\n" : "") + "claude " + ClaudeCode.AddArguments(server, location, CommandLine(command, args));

        private string Cli(string manual) =>
            _findCli() ?? throw new ClientUnavailableException("Không tìm thấy lệnh claude (Claude Code CLI) trên máy. Cài Claude Code rồi thử lại, hoặc tự chạy lệnh dưới đây trong terminal.", manual);

        private ClaudeTarget Target(string location) =>
            location == null
                ? Config.User
                : Config.Workspaces.FirstOrDefault(w => string.Equals(w.Workspace.TrimEnd('\\'), location.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
