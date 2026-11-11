using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    /// <summary>Một chip trên dock tác vụ: tác vụ đang chạy, hoặc tác vụ vừa lỗi mà người dùng chưa xem.</summary>
    public sealed class DockChip
    {
        public TaskInfo Task { get; set; }
        public bool Failed { get; set; }
        public string Title => Task.Spec.Label;
        public int? Port => Task.Port;
    }

    /// <summary>
    /// Tập tác vụ lỗi người dùng đã xem (mở log hoặc đóng chip). Chỉ giữ trong bộ nhớ: lỗi cũ sau khi mở lại app không đáng nhắc nữa.
    /// </summary>
    public sealed class SeenFailures
    {
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Contains(string taskId) => _ids.Contains(taskId);

        public void Mark(string taskId)
        {
            if (!string.IsNullOrEmpty(taskId))
                _ids.Add(taskId);
        }
    }

    public static class DockChips
    {
        /// <summary>Số chip tối đa hiện ra; phần còn lại gom thành "+n".</summary>
        public const int MaxShown = 4;

        /// <summary>
        /// Tác vụ đang chạy (cũ trước), rồi các tác vụ lỗi chưa xem. Tác vụ lỗi chỉ lấy lần chạy mới nhất của mỗi khoá
        /// và biến mất khi bạn mở log, đóng chip, hoặc chạy lại thành công.
        /// </summary>
        public static List<DockChip> Build(IEnumerable<TaskInfo> tasks, SeenFailures seen, DateTime now)
        {
            var list = tasks.ToList();
            var chips = list.Where(t => t.IsRunning).OrderBy(t => t.StartedAt).Select(t => new DockChip { Task = t }).ToList();
            chips.AddRange(HomeFeed.FailedTasks(list, now).Where(t => !seen.Contains(t.Id)).Select(t => new DockChip { Task = t, Failed = true }));
            return chips;
        }
    }
}
