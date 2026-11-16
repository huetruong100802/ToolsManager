using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// Agent bất kỳ nói MCP qua stdio (Cline, Zed, JetBrains AI, Continue, script riêng…): Toolbox không biết file cấu hình của nó,
    /// nên tạo agent rồi đưa đoạn cấu hình để người dùng tự dán.
    /// </summary>
    public sealed class ManualClient : AgentClient
    {
        public override string Id => "other";
        public override string Name => "Agent khác";
        public override string Tagline => "Cline, Zed, JetBrains, Continue… · copy cấu hình rồi tự dán";
        public override string Monogram => "+";
        public override bool IsInstalled => true;
        public override bool HasProjectConfig => false;
        public override bool IsManual => true;
        public override string RestartHint => "Dán cấu hình vào phần MCP của agent rồi khởi động lại agent đó.";

        public override string ConfigPath(string location) => null;

        public override Dictionary<string, McpEntry> ReadServers(string location) =>
            new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);

        public override string Write(string location, string server, string command, IList<string> args) =>
            throw new NotSupportedException("Agent khác: hãy copy cấu hình và tự dán.");

        public override void Remove(string location, string server) { }

        /// <summary>Dạng <c>mcpServers</c> phổ biến nhất, kèm dòng lệnh cho agent chỉ nhận một lệnh.</summary>
        public override string Snippet(string location, string server, string command, IList<string> args) =>
            "{\n  \"mcpServers\": {\n    " + JsonConfig.Quote(server) + ": { \"command\": " + JsonConfig.Quote(command)
            + ", \"args\": [" + string.Join(", ", args.Select(JsonConfig.Quote)) + "] }\n  }\n}\n\n"
            + "// Dòng lệnh (stdio): " + CommandLine(command, args);
    }

    /// <summary>Các app agent Toolbox biết cách kết nối, theo thứ tự hiện trong cửa sổ Kết nối agent.</summary>
    public static class AgentClientCatalog
    {
        public static IReadOnlyList<AgentClient> CreateDefault() => new AgentClient[]
        {
            new ClaudeCodeClient(),
            JsonFileClient.Cursor(),
            JsonFileClient.VsCode(),
            JsonFileClient.ClaudeDesktop(),
            new CodexClient(),
            JsonFileClient.Gemini(),
            JsonFileClient.Windsurf(),
            new ManualClient(),
        };

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude"] = "Claude Code",
            ["claude-desktop"] = "Claude Desktop",
            ["cursor"] = "Cursor",
            ["vscode"] = "VS Code · Copilot",
            ["windsurf"] = "Windsurf",
            ["gemini"] = "Gemini CLI",
            ["codex"] = "Codex CLI",
            ["other"] = "Agent khác",
        };

        /// <summary>Tên app của agent: theo id đã lưu, không biết thì theo tên client tự báo khi kết nối (clientInfo).</summary>
        public static string DisplayName(string clientId, string reportedName = null)
        {
            if (!string.IsNullOrEmpty(clientId) && Names.TryGetValue(clientId, out var name) && clientId != "other")
                return name;
            if (!string.IsNullOrWhiteSpace(reportedName))
                return reportedName;
            return string.IsNullOrEmpty(clientId) ? "Agent" : Names.TryGetValue(clientId, out var fallback) ? fallback : clientId;
        }
    }
}
