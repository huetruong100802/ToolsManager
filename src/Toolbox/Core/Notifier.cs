using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    /// <summary>
    /// Thông báo Windows khi tác vụ chạy lâu kết thúc hoặc lỗi lúc cửa sổ không ở trước: nháy taskbar và hiện thông báo của hệ thống,
    /// bấm vào thông báo thì mở log. Quyết định có thông báo hay không nằm ở <see cref="ShouldNotify"/> (thuần logic, test được).
    /// </summary>
    public sealed class Notifier : IDisposable
    {
        /// <summary>Tác vụ ngắn hơn ngưỡng này kết thúc thì không đáng làm phiền.</summary>
        public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(30);

        private readonly HashSet<string> _handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private NotifyIcon _icon;
        private TaskInfo _lastTask;

        /// <summary>Bấm vào thông báo: mở log của tác vụ.</summary>
        public event Action<TaskInfo> Clicked;

        /// <summary>
        /// Có nên thông báo cho tác vụ đã kết thúc này không: bật công tắc, cửa sổ không ở trước, chạy tối thiểu 30 giây,
        /// và không phải do người dùng tự dừng.
        /// </summary>
        public static bool ShouldNotify(TaskInfo task, bool enabled, bool windowActive)
        {
            if (!enabled || windowActive || task.IsRunning || task.State == TaskState.Stopped || !task.EndedAt.HasValue)
                return false;
            return task.EndedAt.Value - task.StartedAt >= MinDuration;
        }

        /// <summary>
        /// Xét các tác vụ vừa kết thúc, mỗi tác vụ chỉ một lần (kể cả khi không thông báo). Trả về các tác vụ đã được báo.
        /// </summary>
        public List<TaskInfo> Check(IEnumerable<TaskInfo> tasks, bool enabled, bool windowActive)
        {
            var notified = new List<TaskInfo>();
            foreach (var task in tasks)
            {
                if (task.IsRunning || !_handled.Add(task.Id))
                    continue;
                if (ShouldNotify(task, enabled, windowActive))
                {
                    notified.Add(task);
                    Show(task);
                }
            }
            return notified;
        }

        /// <summary>Đánh dấu các tác vụ đã có từ trước là đã xử lý (lúc mở app), để không báo lại việc cũ.</summary>
        public void MarkExisting(IEnumerable<TaskInfo> tasks)
        {
            foreach (var task in tasks)
                if (!task.IsRunning)
                    _handled.Add(task.Id);
        }

        public static string TitleFor(TaskInfo task) =>
            task.State == TaskState.Failed ? "Tác vụ bị lỗi" : "Tác vụ đã xong";

        public static string TextFor(TaskInfo task) =>
            $"{task.Spec.Label}" + (task.State == TaskState.Failed && task.ExitCode.HasValue ? $" (mã thoát {task.ExitCode})" : "")
            + " · chạy " + Text.Duration((task.EndedAt ?? DateTime.UtcNow) - task.StartedAt);

        private void Show(TaskInfo task)
        {
            Flash();
            try
            {
                if (_icon == null)
                {
                    _icon = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Toolbox" };
                    _icon.BalloonTipClicked += (s, e) => { var t = _lastTask; if (t != null) Clicked?.Invoke(t); };
                    _icon.BalloonTipClosed += (s, e) => { if (_icon != null) _icon.Visible = false; };
                }
                _lastTask = task;
                _icon.Visible = true;
                _icon.ShowBalloonTip(6000, TitleFor(task), TextFor(task), task.State == TaskState.Failed ? ToolTipIcon.Error : ToolTipIcon.Info);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // Không có vùng thông báo (phiên từ xa...): vẫn nháy taskbar là đủ.
            }
        }

        // ---- Nháy taskbar ----

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        private static void Flash()
        {
            var window = System.Windows.Application.Current?.MainWindow;
            var handle = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            // 0x0F = FLASHW_ALL | FLASHW_TIMERNOFG: nháy tới khi cửa sổ được đưa ra trước.
            var info = new FlashInfo { Size = (uint)Marshal.SizeOf(typeof(FlashInfo)), Window = handle, Flags = 0x0F, Count = 5, Timeout = 0 };
            try { FlashWindowEx(ref info); }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
        }

        public void Dispose()
        {
            if (_icon == null)
                return;
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
