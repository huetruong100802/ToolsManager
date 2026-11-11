using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;

namespace Toolbox
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Toolbox.exe --mcp: chạy MCP server qua stdio cho agent, không mở cửa sổ.
            if (e.Args.Contains("--mcp", StringComparer.OrdinalIgnoreCase))
            {
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                var input = new StreamReader(Console.OpenStandardInput(), encoding);
                var output = new StreamWriter(Console.OpenStandardOutput(), encoding) { NewLine = "\n" };
                var workspaceId = WorkspaceArgument(e.Args);
                var agentId = ValueOf(e.Args, "--agent");
                // Trong workspace luôn có ngữ cảnh agent, để sửa tool dùng chung thành đề xuất dù chưa đăng ký agent.
                var agent = workspaceId != null || agentId != null ? AgentContext.Default(agentId) : null;
                new McpServer(() => ToolStore.Load(ToolStore.DefaultPath), workspaceId, () => WorkspaceStore.Load(WorkspaceStore.DefaultPath), agent: agent)
                    .Run(input, output);
                Environment.Exit(0);
            }

            Theme.Initialize(Theme.Parse(Settings.Load().Theme));
            new MainWindow(e.Args.FirstOrDefault(a => File.Exists(a))).Show();
        }

        /// <summary>
        /// --workspace &lt;id&gt;: giới hạn MCP server trong một workspace đã đăng ký. Không có thì là toàn máy.
        /// Có cờ mà thiếu id thì thoát ngay, để không vô tình chạy ở chế độ toàn máy.
        /// </summary>
        private static string ValueOf(string[] args, string flag)
        {
            var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
        }

        private static string WorkspaceArgument(string[] args)
        {
            var index = Array.FindIndex(args, a => string.Equals(a, "--workspace", StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Thiếu id sau --workspace. Ví dụ: Toolbox.exe --mcp --workspace 1a2b3c4d");
                Environment.Exit(2);
            }
            return args[index + 1];
        }
    }
}
