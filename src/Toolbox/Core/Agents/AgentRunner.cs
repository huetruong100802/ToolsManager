using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Toolbox.Core.Agents
{
    public enum AgentRunStatus
    {
        Ok,
        /// <summary>Không tìm thấy chương trình trong PATH hoặc không khởi động được.</summary>
        Missing,
        Failed,
        TimedOut,
        Cancelled,
    }

    public sealed class AgentRunResult
    {
        public AgentRunStatus Status { get; set; }
        public string Output { get; set; } = "";
        /// <summary>Thông báo cho người dùng khi không <see cref="AgentRunStatus.Ok"/>; lỗi của CLI được giữ nguyên văn.</summary>
        public string Error { get; set; } = "";
        public int? ExitCode { get; set; }
        public bool Ok => Status == AgentRunStatus.Ok;
    }

    /// <summary>
    /// Gọi một CLI agent có sẵn trên máy (ví dụ <c>claude -p</c>) ở chế độ không tương tác: prompt qua stdin, kết quả đọc từ stdout.
    /// Không lưu khoá, không dùng API. Có giới hạn thời gian và huỷ; huỷ hoặc quá giờ thì giết cả cây tiến trình con.
    /// </summary>
    public static class AgentRunner
    {
        public const string DefaultCommand = "claude -p";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Tách lệnh thành chương trình và tham số (chỉ hỗ trợ nháy kép bao đường dẫn có dấu cách).</summary>
        public static bool TryParse(string command, out string program, out string arguments)
        {
            program = arguments = "";
            command = (command ?? "").Trim();
            if (command.Length == 0)
                return false;
            if (command[0] == '"')
            {
                var end = command.IndexOf('"', 1);
                if (end < 0)
                    return false;
                program = command.Substring(1, end - 1);
                arguments = command.Substring(end + 1).Trim();
            }
            else
            {
                var space = command.IndexOf(' ');
                program = space < 0 ? command : command.Substring(0, space);
                arguments = space < 0 ? "" : command.Substring(space + 1).Trim();
            }
            return program.Length > 0;
        }

        /// <summary>Tìm chương trình theo PATH và PATHEXT; null nếu không có.</summary>
        public static string Resolve(string program)
        {
            if (string.IsNullOrWhiteSpace(program))
                return null;
            var extensions = new List<string> { "" };
            if (string.IsNullOrEmpty(Path.GetExtension(program)))
            {
                foreach (var ext in (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';'))
                    if (ext.Length > 0)
                        extensions.Add(ext);
            }

            if (program.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                foreach (var ext in extensions)
                    if (File.Exists(program + ext))
                        return Path.GetFullPath(program + ext);
                return null;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                foreach (var ext in extensions)
                {
                    try
                    {
                        var candidate = Path.Combine(dir.Trim('"'), program + ext);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
            return null;
        }

        public static string MissingMessage(string program) =>
            $"Không tìm thấy lệnh \"{program}\" trên máy. Cài CLI agent (ví dụ Claude Code: npm install -g @anthropic-ai/claude-code, rồi đăng nhập một lần bằng lệnh claude) " +
            "hoặc đổi lệnh gọi agent ở ô \"Lệnh gọi agent\".";

        public static Task<AgentRunResult> RunAsync(string command, string prompt, string workingDirectory, TimeSpan timeout, CancellationToken cancel) =>
            Task.Run(() => Run(command, prompt, workingDirectory, timeout, cancel));

        public static AgentRunResult Run(string command, string prompt, string workingDirectory, TimeSpan timeout, CancellationToken cancel)
        {
            if (!TryParse(string.IsNullOrWhiteSpace(command) ? DefaultCommand : command, out var program, out var arguments))
                return new AgentRunResult { Status = AgentRunStatus.Failed, Error = "Lệnh gọi agent không hợp lệ." };

            var resolved = Resolve(program);
            if (resolved == null)
                return new AgentRunResult { Status = AgentRunStatus.Missing, Error = MissingMessage(program) };

            var batch = resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            var info = new ProcessStartInfo
            {
                FileName = batch ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : resolved,
                Arguments = batch ? "/d /s /c \"\"" + resolved + "\" " + arguments + "\"" : arguments,
                WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                return new AgentRunResult { Status = AgentRunStatus.Missing, Error = "Không chạy được \"" + program + "\": " + ex.Message };
            }

            using (process)
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    var bytes = new UTF8Encoding(false).GetBytes(prompt ?? "");
                    process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // CLI đã thoát trước khi đọc hết prompt: xử lý theo mã thoát bên dưới.
                }

                var deadline = DateTime.UtcNow + timeout;
                while (!process.WaitForExit(100))
                {
                    if (cancel.IsCancellationRequested)
                    {
                        KillTree(process);
                        return new AgentRunResult { Status = AgentRunStatus.Cancelled, Error = "Đã huỷ." };
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        KillTree(process);
                        return new AgentRunResult
                        {
                            Status = AgentRunStatus.TimedOut,
                            Error = $"Agent chạy quá {(int)timeout.TotalSeconds} giây nên đã dừng. Thử lại hoặc dùng lệnh gọi agent nhanh hơn.",
                        };
                    }
                }
                process.WaitForExit(); // chờ luồng đọc xả hết

                string output, error;
                lock (stdout) output = stdout.ToString();
                lock (stderr) error = stderr.ToString().Trim();
                if (process.ExitCode != 0)
                {
                    return new AgentRunResult
                    {
                        Status = AgentRunStatus.Failed,
                        ExitCode = process.ExitCode,
                        Output = output,
                        Error = $"Agent thoát với mã {process.ExitCode}." + (error.Length > 0 ? "\n" + error : output.Trim().Length > 0 ? "\n" + output.Trim() : ""),
                    };
                }
                return new AgentRunResult { Status = AgentRunStatus.Ok, Output = output, ExitCode = 0, Error = error };
            }
        }

        private static void KillTree(Process process)
        {
            try
            {
                using (var kill = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + process.Id) { CreateNoWindow = true, UseShellExecute = false }))
                    kill?.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
            }
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
            }
        }
    }
}
