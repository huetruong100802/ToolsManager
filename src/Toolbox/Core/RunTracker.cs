using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Toolbox.Core
{
    /// <summary>Một lần chạy tool do Toolbox khởi động (chỉ theo dõi trạng thái, không đọc output).</summary>
    public sealed class RunInstance
    {
        internal RunInstance(string toolId, Process process)
        {
            ToolId = toolId;
            Process = process;
            Pid = process.Id;
            StartedAt = DateTime.UtcNow;
        }

        public string ToolId { get; }
        public int Pid { get; }
        public DateTime StartedAt { get; }

        /// <summary>null khi còn đang chạy.</summary>
        public DateTime? EndedAt { get; internal set; }

        public int? ExitCode { get; internal set; }

        /// <summary>Người dùng bấm Dừng (khác với tự thoát hoặc lỗi).</summary>
        public bool Stopped { get; internal set; }

        public bool IsRunning => EndedAt == null;

        internal Process Process { get; }
    }

    /// <summary>
    /// Theo dõi các tool đang chạy: tool nào còn chạy, chạy bao lâu, lần chạy trước kết thúc ra sao, và Dừng.
    /// Mỗi tool giữ tối đa một lần chạy đang chạy cùng lần kết thúc gần nhất; chỉ lưu trong bộ nhớ.
    /// <see cref="Changed"/> được gọi từ luồng bất kỳ, nơi nhận phải tự chuyển về luồng giao diện.
    /// </summary>
    public sealed class RunTracker
    {
        private readonly object _gate = new object();
        private readonly List<RunInstance> _runs = new List<RunInstance>();
        private readonly Func<ToolEntry, string, Process> _starter;

        public RunTracker(Func<ToolEntry, string, Process> starter = null)
        {
            _starter = starter ?? Runner.StartProcess;
        }

        /// <summary>
        /// Nhiều app (trình duyệt, Electron, app một-instance) chỉ là bộ khởi động: thoát sạch ngay rồi giao việc cho
        /// tiến trình khác. Thoát mã 0 trong khoảng này thì không coi là một kết quả để khỏi báo "Đã thoát" sai.
        /// </summary>
        public TimeSpan HandoffWindow { get; set; } = TimeSpan.FromSeconds(3);

        public event Action Changed;

        public int RunningCount
        {
            get { lock (_gate) return _runs.Count(r => r.IsRunning); }
        }

        /// <summary>Lần chạy đang chạy của tool, hoặc null.</summary>
        public RunInstance Current(string toolId)
        {
            lock (_gate)
                return _runs.LastOrDefault(r => r.ToolId == toolId && r.IsRunning);
        }

        /// <summary>Lần chạy đã kết thúc gần nhất của tool, hoặc null.</summary>
        public RunInstance Last(string toolId)
        {
            lock (_gate)
                return _runs.LastOrDefault(r => r.ToolId == toolId && !r.IsRunning);
        }

        /// <summary>
        /// Chạy tool. Trả về null khi không theo dõi được: thư mục (chỉ mở Explorer), tiến trình do ShellExecute
        /// giao cho instance khác, hoặc tiến trình nâng quyền mà Toolbox không có quyền theo dõi.
        /// </summary>
        public RunInstance Start(ToolEntry tool, string args = null)
        {
            var process = _starter(tool, args);
            if (process == null)
                return null;
            if (!Runner.IsTrackable(tool))
            {
                process.Dispose();
                return null;
            }

            RunInstance run;
            try
            {
                run = new RunInstance(tool.Id, process);
                lock (_gate)
                {
                    _runs.RemoveAll(r => r.ToolId == tool.Id && !r.IsRunning);
                    _runs.Add(run);
                }
                process.Exited += (s, e) => OnExited(run);
                process.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // Tiến trình nâng quyền (UAC) hoặc đã mất handle: vẫn chạy, chỉ là không theo dõi được.
                lock (_gate)
                    _runs.RemoveAll(r => ReferenceEquals(r.Process, process));
                process.Dispose();
                return null;
            }

            Changed?.Invoke();
            return run;
        }

        /// <summary>Dừng ngay tool (cả cây tiến trình con). Dữ liệu chưa lưu của tool sẽ mất.</summary>
        public void Stop(string toolId)
        {
            List<RunInstance> running;
            lock (_gate)
            {
                running = _runs.Where(r => r.ToolId == toolId && r.IsRunning).ToList();
                foreach (var run in running)
                    run.Stopped = true;
            }

            // taskkill có thể chờ vài giây: không chặn luồng giao diện.
            foreach (var run in running)
            {
                var pid = run.Pid;
                Task.Run(() => Runner.KillTree(pid));
            }
        }

        private void OnExited(RunInstance run)
        {
            int? code = null;
            try
            {
                code = run.Process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // Không đọc được mã thoát: để null, giao diện chỉ hiện "Đã thoát".
            }

            lock (_gate)
            {
                run.ExitCode = code;
                run.EndedAt = DateTime.UtcNow;
                if (!run.Stopped && code == 0 && run.EndedAt.Value - run.StartedAt < HandoffWindow)
                    _runs.Remove(run);
            }

            try
            {
                run.Process.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Đã dispose.
            }

            Changed?.Invoke();
        }
    }
}
