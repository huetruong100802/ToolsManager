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

            if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                RunUninstall();
                Environment.Exit(0);
            }

            HookCrashHandlers();

            // Một cửa sổ cho mỗi thư mục dữ liệu: mở lần hai thì đưa cửa sổ đang chạy lên trước và chuyển file cần mở cho nó.
            var fileToOpen = e.Args.FirstOrDefault(a => File.Exists(a));
            var key = SingleInstance.KeyFor(DataDir.Root);
            _instance = SingleInstance.TryAcquire(key);
            if (_instance == null && SingleInstance.Send(key, fileToOpen == null ? "" : Path.GetFullPath(fileToOpen)))
                Environment.Exit(0);

            Theme.Initialize(Theme.Parse(Settings.Load().Theme));
            var window = new MainWindow(fileToOpen);
            _instance?.Listen(message => Dispatcher.BeginInvoke(new Action(() =>
            {
                window.BringToFront();
                if (!string.IsNullOrWhiteSpace(message) && File.Exists(message))
                    window.OpenPackage(message);
            })));
            Exit += (s, a) => _instance?.Dispose();
            window.Show();
        }

        private SingleInstance _instance;

        // ---- Lỗi không lường trước ----

        private DateTime _lastCrash = DateTime.MinValue;
        private int _crashBurst;

        /// <summary>
        /// Lỗi lọt ra tới vòng lặp giao diện: ghi crash.log, báo cho người dùng và để app chạy tiếp (dữ liệu đã lưu không bị ảnh hưởng).
        /// Lỗi dồn dập liên tiếp nghĩa là giao diện đã hỏng thật: báo một lần rồi đóng, thay vì hiện thông báo không dứt.
        /// </summary>
        private void HookCrashHandlers()
        {
            DispatcherUnhandledException += (s, a) =>
            {
                CrashLog.Write(a.Exception, "giao diện");
                a.Handled = true;
                var now = DateTime.UtcNow;
                _crashBurst = now - _lastCrash < TimeSpan.FromSeconds(5) ? _crashBurst + 1 : 1;
                _lastCrash = now;
                if (_crashBurst >= 4)
                {
                    MessageBox.Show("Toolbox gặp lỗi liên tiếp và phải đóng.\n\n" + a.Exception.Message + "\n\nChi tiết: " + CrashLog.DefaultPath,
                        "Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(1);
                }
                var message = "Có lỗi không mong muốn: " + a.Exception.Message + " Chi tiết đã ghi vào crash.log trong thư mục dữ liệu.";
                if (!Views.Toasts.Show(message, Views.ToastKind.Error))
                    MessageBox.Show(message, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => CrashLog.Write(a.ExceptionObject as Exception, "luồng nền");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, a) =>
            {
                CrashLog.Write(a.Exception, "tác vụ nền");
                a.SetObserved();
            };
        }

        // ---- Gỡ cài đặt ----

        /// <summary>
        /// <c>Toolbox.exe --uninstall</c> (nút Uninstall trong "Installed apps" của Windows): gỡ shortcut, liên kết đuôi .toolbox và
        /// bản đã cài. Dữ liệu của người dùng được giữ lại.
        /// </summary>
        private static void RunUninstall()
        {
            var installer = new Installer();
            var running = SingleInstance.TryAcquire(SingleInstance.KeyFor(DataDir.Root));
            if (running == null)
            {
                MessageBox.Show("Toolbox đang mở. Hãy đóng cửa sổ Toolbox rồi gỡ lại.", "Gỡ Toolbox", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (running)
            {
                if (MessageBox.Show("Gỡ Toolbox khỏi máy này?\n\nShortcut Start Menu, liên kết file .toolbox và bản đã cài sẽ bị gỡ. "
                        + "Danh sách tool, workspace và nhật ký của bạn được giữ lại ở:\n" + DataDir.Root,
                        "Gỡ Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                try
                {
                    installer.Uninstall();
                    installer.ScheduleRemoveFiles();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    MessageBox.Show("Không gỡ được hết: " + ex.Message, "Gỡ Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show("Đã gỡ Toolbox. Dữ liệu của bạn vẫn còn ở:\n" + DataDir.Root + "\n\nXoá thư mục đó nếu không dùng nữa.",
                    "Gỡ Toolbox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
