using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Toolbox.Core.Workspaces
{
    public enum TaskState
    {
        Running,
        /// <summary>Thoát mã 0.</summary>
        Done,
        /// <summary>Thoát với mã khác 0.</summary>
        Failed,
        /// <summary>Người dùng bấm Dừng.</summary>
        Stopped,
    }

    public static class TaskSource
    {
        public const string User = "user";
        public const string Agent = "agent";
    }

    /// <summary>Mô tả một lần chạy; đủ để chạy lại.</summary>
    public sealed class TaskSpec
    {
        public string WorkspaceId { get; set; }
        /// <summary>Tên hiện trên giao diện, ví dụ "Angular · Chạy dev · :4200" hoặc tên profile.</summary>
        public string Label { get; set; }
        public string CommandLine { get; set; }
        public string Cwd { get; set; }
        /// <summary>Chạy ở cửa sổ terminal riêng: xem và nhập được nhưng không có log.</summary>
        public bool Window { get; set; }
        /// <summary>Cổng khai báo trước (từ tham số); không có thì thử bắt từ output.</summary>
        public int? Port { get; set; }
        public string Source { get; set; } = TaskSource.User;
        /// <summary>Khoá để giao diện biết dòng nào đang chạy: workspace|tool|action|variant|profile.</summary>
        public string Key { get; set; }

        public TaskSpec Clone() => (TaskSpec)MemberwiseClone();
    }

    /// <summary>Một tác vụ đã/đang chạy. Đọc được từ mọi luồng.</summary>
    public sealed class TaskInfo
    {
        private readonly object _lock = new object();
        private readonly LinkedList<string> _lines = new LinkedList<string>();
        private TaskState _state = TaskState.Running;
        private DateTime? _endedAt;
        private int? _exitCode;
        private int? _port;

        internal TaskInfo(string id, TaskSpec spec, string logPath)
        {
            Id = id;
            Spec = spec;
            LogPath = logPath;
            StartedAt = DateTime.UtcNow;
            _port = spec.Port;
        }

        public string Id { get; }
        public TaskSpec Spec { get; }
        public string LogPath { get; }
        public DateTime StartedAt { get; }
        internal int ProcessId { get; set; }
        internal bool StopRequested { get; set; }

        public TaskState State { get { lock (_lock) return _state; } }
        public DateTime? EndedAt { get { lock (_lock) return _endedAt; } }
        public int? ExitCode { get { lock (_lock) return _exitCode; } }
        public int? Port { get { lock (_lock) return _port; } }
        public bool IsRunning => State == TaskState.Running;

        /// <summary>Bản sao các dòng output gần nhất (tối đa <see cref="TaskHost.MaxLines"/>).</summary>
        public string[] Lines() { lock (_lock) return _lines.ToArray(); }

        internal void AddLine(string line, int max)
        {
            lock (_lock)
            {
                _lines.AddLast(line);
                while (_lines.Count > max)
                    _lines.RemoveFirst();
            }
        }

        internal bool SetPort(int port)
        {
            lock (_lock)
            {
                if (_port.HasValue)
                    return false;
                _port = port;
                return true;
            }
        }

        internal void Finish(TaskState state, int? exitCode)
        {
            lock (_lock)
            {
                _state = state;
                _exitCode = exitCode;
                _endedAt = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Chạy tác vụ ngầm (bắt output, ghi log) hoặc ở cửa sổ riêng, theo dõi trạng thái, dừng và chạy lại.
    /// Sự kiện bắn từ luồng bất kỳ; giao diện tự đưa về dispatcher.
    /// </summary>
    public sealed class TaskHost
    {
        public const int MaxLines = 2000;
        private const int MaxKeptFinished = 200;
        private const int MaxLogFiles = 200;

        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private static readonly Regex PortInOutput = new Regex(@"(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::\]|\*):(\d{4,5})\b", RegexOptions.Compiled);

        private readonly object _sync = new object();
        private readonly List<TaskInfo> _tasks = new List<TaskInfo>();
        private readonly string _runsDir;

        public TaskHost(string runsDir)
        {
            _runsDir = runsDir;
            PruneLogs();
        }

        public static string DefaultRunsDir =>
            Path.Combine(DataDir.Root, "runs");

        /// <summary>Trạng thái hoặc cổng của một tác vụ đổi, hoặc có tác vụ mới.</summary>
        public event Action Changed;

        /// <summary>Có dòng output mới.</summary>
        public event Action<TaskInfo> LineAdded;

        public IReadOnlyList<TaskInfo> Snapshot() { lock (_sync) return _tasks.ToArray(); }

        public IReadOnlyList<TaskInfo> Running() => Snapshot().Where(t => t.IsRunning).ToList();

        public TaskInfo Find(string id) { lock (_sync) return _tasks.Find(t => t.Id == id); }

        /// <summary>Cổng đang dùng → tên tác vụ, để cảnh báo trùng cổng.</summary>
        public IReadOnlyDictionary<int, string> UsedPorts()
        {
            var map = new Dictionary<int, string>();
            foreach (var t in Running())
                if (t.Port.HasValue && !map.ContainsKey(t.Port.Value))
                    map[t.Port.Value] = t.Spec.Label;
            return map;
        }

        public TaskInfo Start(TaskSpec spec)
        {
            if (string.IsNullOrWhiteSpace(spec.CommandLine))
                throw new ArgumentException("Lệnh trống.");

            var id = WorkspaceStore.NewId();
            var info = new TaskInfo(id, spec, spec.Window ? null : Path.Combine(_runsDir, id + ".log"));
            var process = new Process { StartInfo = CreateStartInfo(spec), EnableRaisingEvents = true };

            StreamWriter log = null;
            if (info.LogPath != null)
                log = OpenLog(info);

            if (!spec.Window)
            {
                process.OutputDataReceived += (s, e) => OnLine(info, log, e.Data);
                process.ErrorDataReceived += (s, e) => OnLine(info, log, e.Data);
            }
            // Tiến trình ngắn có thể thoát trước khi ta kịp bắt đầu đọc output; chờ đọc xong mới xử lý Exited.
            var reading = new System.Threading.ManualResetEventSlim(false);
            process.Exited += (s, e) => Task.Run(() =>
            {
                reading.Wait();
                OnExited(info, process, log);
            });

            try
            {
                process.Start();
                info.ProcessId = process.Id;
                lock (_sync)
                {
                    _tasks.Add(info);
                    Trim();
                }
                if (!spec.Window)
                {
                    process.StandardInput.Close();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
            }
            catch
            {
                reading.Set();
                process.Dispose();
                log?.Dispose();
                lock (_sync)
                    _tasks.Remove(info);
                throw;
            }
            reading.Set();
            Changed?.Invoke();
            return info;
        }

        /// <summary>Dừng cả cây tiến trình (ép tắt). Không chờ; trạng thái đổi khi tiến trình thật sự thoát.</summary>
        public void Stop(string id)
        {
            var info = Find(id);
            if (info == null || !info.IsRunning)
                return;
            info.StopRequested = true;
            var pid = info.ProcessId;
            Task.Run(() => Runner.KillTree(pid));
        }

        public void StopAll()
        {
            foreach (var t in Running())
                t.StopRequested = true;
            var pids = Running().Select(t => t.ProcessId).ToList();
            Parallel.ForEach(pids, Runner.KillTree);
        }

        /// <summary>Chạy lại đúng lệnh cũ. Task cũ đang chạy thì dừng trước.</summary>
        public TaskInfo Restart(string id)
        {
            var old = Find(id) ?? throw new ArgumentException("Không tìm thấy tác vụ " + id);
            if (old.IsRunning)
            {
                old.StopRequested = true;
                Runner.KillTree(old.ProcessId);
                SpinWait(() => !old.IsRunning, TimeSpan.FromSeconds(5));
            }
            return Start(old.Spec.Clone());
        }

        private static bool SpinWait(Func<bool> done, TimeSpan timeout) =>
            System.Threading.SpinWait.SpinUntil(done, timeout);

        private static ProcessStartInfo CreateStartInfo(TaskSpec spec)
        {
            var cwd = string.IsNullOrWhiteSpace(spec.Cwd) ? null : spec.Cwd;
            if (spec.Window)
                return new ProcessStartInfo("cmd.exe", $"/k \"{spec.CommandLine}\"") { UseShellExecute = true, WorkingDirectory = cwd };

            return new ProcessStartInfo("cmd.exe", $"/c \"{spec.CommandLine}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // Redirect cả stdin để tiến trình con không đọc trộm kênh MCP khi Toolbox chạy chế độ stdio.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = cwd,
            };
        }

        private void OnLine(TaskInfo info, StreamWriter log, string raw)
        {
            if (raw == null)
                return;
            var line = Ansi.Replace(raw, "");
            info.AddLine(line, MaxLines);
            WriteLog(log, line);

            var found = false;
            if (!info.Port.HasValue)
            {
                var m = PortInOutput.Match(line);
                if (m.Success && PortRules.TryParse(m.Groups[1].Value, out var port) && info.SetPort(port))
                    found = true;
            }
            LineAdded?.Invoke(info);
            if (found)
                Changed?.Invoke();
        }

        private void OnExited(TaskInfo info, Process process, StreamWriter log)
        {
            int? code = null;
            try
            {
                if (!info.Spec.Window)
                    process.WaitForExit(); // chờ đọc nốt output còn trong pipe
                code = process.ExitCode;
            }
            catch (InvalidOperationException) { }
            finally
            {
                process.Dispose();
            }

            var state = info.StopRequested ? TaskState.Stopped : code == 0 ? TaskState.Done : TaskState.Failed;
            info.Finish(state, code);
            WriteLog(log, $"# kết thúc: {state}" + (code.HasValue ? $", mã {code}" : ""));
            if (log != null)
                lock (log)
                    log.Dispose();
            Changed?.Invoke();
        }

        private StreamWriter OpenLog(TaskInfo info)
        {
            try
            {
                Directory.CreateDirectory(_runsDir);
                var stream = new FileStream(info.LogPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine($"# {info.Spec.CommandLine}");
                writer.WriteLine($"# thư mục: {info.Spec.Cwd}");
                return writer;
            }
            catch (IOException) { return null; }      // log là phụ: không có thì vẫn chạy
            catch (UnauthorizedAccessException) { return null; }
        }

        private static void WriteLog(StreamWriter log, string line)
        {
            if (log == null)
                return;
            lock (log)
            {
                try { log.WriteLine(line); }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            }
        }

        private void Trim()
        {
            var finished = _tasks.Where(t => !t.IsRunning).ToList();
            for (var i = 0; i < finished.Count - MaxKeptFinished; i++)
                _tasks.Remove(finished[i]);
        }

        private void PruneLogs()
        {
            try
            {
                if (!Directory.Exists(_runsDir))
                    return;
                foreach (var file in new DirectoryInfo(_runsDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxLogFiles))
                    file.Delete();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
