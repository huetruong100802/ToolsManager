using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Toolbox.Core
{
    public sealed class RunResult
    {
        /// <summary>null nếu bị dừng do quá thời gian.</summary>
        public int? ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Chạy tool và mở vị trí của tool.</summary>
    public static class Runner
    {
        public const int MaxCapturedChars = 20000;

        /// <summary>
        /// Chạy như người dùng bấm (có cửa sổ). Luôn qua ShellExecute để tiến trình con không kế thừa
        /// stdin/stdout của Toolbox — quan trọng khi Toolbox đang chạy chế độ MCP qua stdio.
        /// </summary>
        public static ProcessStartInfo CreateStartInfo(ToolEntry tool, string args = null)
        {
            var path = RequireExisting(tool);
            args = args ?? tool.Args ?? "";

            if (Directory.Exists(path))
                return new ProcessStartInfo("explorer.exe", Quote(path)) { UseShellExecute = true };

            string file, arguments;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                // .ps1 mặc định mở bằng Notepad, nên gọi PowerShell; -NoExit để còn xem output.
                case ".ps1":
                    file = "powershell.exe";
                    arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -File {Quote(path)} {args}";
                    break;
                // /k giữ cửa sổ console sau khi script chạy xong.
                case ".bat":
                case ".cmd":
                    file = "cmd.exe";
                    arguments = $"/k \"{Quote(path)} {args}\"";
                    break;
                default:
                    file = path;
                    arguments = args;
                    break;
            }
            return new ProcessStartInfo(file, arguments.TrimEnd()) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) };
        }

        /// <summary>Chạy ẩn và lấy output (cho agent). Chỉ hỗ trợ file chạy console.</summary>
        public static ProcessStartInfo CreateCaptureStartInfo(ToolEntry tool, string args = null)
        {
            var path = RequireExisting(tool);
            if (Directory.Exists(path))
                throw new InvalidOperationException($"\"{tool.Name}\" là thư mục, không chạy được.");
            args = args ?? tool.Args ?? "";

            string file, arguments;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".exe":
                case ".com":
                    file = path;
                    arguments = args;
                    break;
                case ".bat":
                case ".cmd":
                    file = "cmd.exe";
                    arguments = $"/c \"{Quote(path)} {args}\"";
                    break;
                case ".ps1":
                    file = "powershell.exe";
                    arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {Quote(path)} {args}";
                    break;
                case ".py":
                    file = "python.exe";
                    arguments = $"{Quote(path)} {args}";
                    break;
                default:
                    throw new NotSupportedException($"Không chạy lấy output được file {extension}; chỉ hỗ trợ .exe, .com, .bat, .cmd, .ps1, .py.");
            }

            return new ProcessStartInfo(file, arguments.TrimEnd())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // Redirect cả stdin để tiến trình con không đọc trộm kênh MCP.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(path),
            };
        }

        public static int? Start(ToolEntry tool, string args = null)
        {
            using (var process = Process.Start(CreateStartInfo(tool, args)))
                return process?.Id;
        }

        public static RunResult RunAndCapture(ToolEntry tool, string args, TimeSpan timeout) =>
            Capture(CreateCaptureStartInfo(tool, args), timeout);

        /// <summary>Chạy ẩn tiến trình đã cấu hình redirect stdin/stdout/stderr, chờ tối đa <paramref name="timeout"/>.</summary>
        internal static RunResult Capture(ProcessStartInfo info, TimeSpan timeout)
        {
            using (var process = Process.Start(info))
            {
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();

                var exited = process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
                if (!exited)
                    KillTree(process.Id);
                // Tiến trình cháu có thể còn giữ pipe; không chờ vô hạn.
                Task.WaitAll(new Task[] { output, error }, TimeSpan.FromSeconds(5));

                return new RunResult
                {
                    ExitCode = exited ? process.ExitCode : (int?)null,
                    TimedOut = !exited,
                    Output = Truncate(output.Status == TaskStatus.RanToCompletion ? output.Result : ""),
                    Error = Truncate(error.Status == TaskStatus.RanToCompletion ? error.Result : ""),
                };
            }
        }

        /// <summary>Mở bộ cài đi kèm như người dùng double-click (có giao diện, UAC nếu cần). Trả về tiến trình để chờ.</summary>
        public static Process StartSetup(ToolEntry tool)
        {
            var setup = Path.GetFullPath(tool.ResolvedSetup);
            if (!File.Exists(setup))
                throw new FileNotFoundException($"Không tìm thấy bộ cài của \"{tool.Name}\": {setup}", setup);
            return Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setup) });
        }

        /// <summary>Mở Explorer và chọn sẵn file của tool.</summary>
        public static void OpenLocation(ToolEntry tool)
        {
            var path = RequireExisting(tool);
            var args = Directory.Exists(path) ? Quote(path) : "/select," + Quote(path);
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true })?.Dispose();
        }

        private static void KillTree(int processId)
        {
            var info = new ProcessStartInfo("taskkill.exe", $"/T /F /PID {processId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var killer = Process.Start(info))
                killer?.WaitForExit(5000);
        }

        private static string Truncate(string text) =>
            text.Length <= MaxCapturedChars ? text : text.Substring(0, MaxCapturedChars) + $"\n…(đã cắt, tổng {text.Length} ký tự)";

        private static string RequireExisting(ToolEntry tool)
        {
            var path = Path.GetFullPath(tool.ResolvedPath);
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException($"Không tìm thấy \"{tool.Name}\": {path}"
                    + (tool.CanInstall ? $"\nApp chưa được cài trên máy này. Có bộ cài kèm theo: {tool.ResolvedSetup} — bấm \"Cài đặt\" trong Toolbox." : ""), path);
            return path;
        }

        private static string Quote(string path) => "\"" + path + "\"";
    }
}
