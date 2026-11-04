using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Quy tắc hiển thị tác vụ (thứ tự, nhãn trạng thái, cổng, thời gian). Tách khỏi giao diện để kiểm thử được.</summary>
    public static class TaskBoard
    {
        /// <summary>Tác vụ đang chạy đứng trước (mới bật lên đầu), rồi tới tác vụ đã kết thúc (kết thúc gần nhất lên đầu).</summary>
        public static List<TaskInfo> Ordered(IEnumerable<TaskInfo> tasks)
        {
            // Xét IsRunning đúng một lần cho mỗi tác vụ, để một tác vụ không rơi vào cả hai nhóm khi trạng thái đổi giữa chừng.
            var groups = tasks.ToLookup(t => t.IsRunning);
            var running = groups[true].OrderByDescending(t => t.StartedAt);
            var finished = groups[false].OrderByDescending(t => t.EndedAt ?? t.StartedAt);
            return running.Concat(finished).ToList();
        }

        public static string StateText(TaskInfo task)
        {
            switch (task.State)
            {
                case TaskState.Running:
                    return "Đang chạy";
                case TaskState.Done:
                    return "Xong";
                case TaskState.Stopped:
                    return "Đã dừng";
                default:
                    return task.ExitCode.HasValue ? $"Lỗi, mã {task.ExitCode}" : "Lỗi";
            }
        }

        /// <summary>Thời gian đã chạy. Tác vụ đã kết thúc tính tới lúc kết thúc; đang chạy tính tới <paramref name="utcNow"/>.</summary>
        public static TimeSpan Elapsed(TaskInfo task, DateTime utcNow)
        {
            var span = (task.EndedAt ?? utcNow) - task.StartedAt;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>"m:ss", hoặc "h:mm:ss" khi từ một giờ trở lên.</summary>
        public static string FormatElapsed(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";

        /// <summary>Cổng của các tác vụ đang chạy, không trùng, tăng dần.</summary>
        public static List<int> PortsOf(IEnumerable<TaskInfo> tasks) =>
            tasks.Where(t => t.IsRunning && t.Port.HasValue)
                 .Select(t => t.Port.Value)
                 .Distinct()
                 .OrderBy(p => p)
                 .ToList();

        /// <summary>Dòng trạng thái thanh dưới cùng.</summary>
        public static string StatusLine(IReadOnlyList<TaskInfo> running)
        {
            if (running.Count == 0)
                return "Không có tác vụ chạy ngầm";
            var ports = PortsOf(running);
            var portText = ports.Count == 0 ? string.Empty : " · cổng " + string.Join(", ", ports);
            return $"Đang chạy {running.Count} tác vụ{portText}";
        }
    }
}
