using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Toolbox.Core;
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
                new McpServer(() => ToolStore.Load(ToolStore.DefaultPath)).Run(input, output);
                Environment.Exit(0);
            }

            new MainWindow(e.Args.FirstOrDefault(a => File.Exists(a))).Show();
        }
    }
}
