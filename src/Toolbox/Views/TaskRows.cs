using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Một dòng tác vụ trên trang Tác vụ và Tổng quan. Cập nhật tại chỗ để nút không bị dựng lại mỗi giây.</summary>
    internal sealed class TaskItem : INotifyPropertyChanged
    {
        private readonly string _workspaceName;

        public TaskItem(TaskInfo task, string workspaceName)
        {
            Task = task;
            _workspaceName = workspaceName;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public TaskInfo Task { get; }

        public string Id => Task.Id;

        public string Label => Task.Spec.Label;

        public string WorkspaceName => _workspaceName;

        public string StateText { get; private set; }

        public Brush StateBrush { get; private set; }

        public string PortText { get; private set; }

        public string TimeText { get; private set; }

        public Visibility StopVisibility { get; private set; }

        public Visibility RestartVisibility { get; private set; }

        private bool _selected;

        /// <summary>Dòng đang được chọn để xem log (viền nhấn).</summary>
        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBorder)));
            }
        }

        public Brush RowBorder => Res(_selected ? "Accent" : "Line");

        /// <summary>Đọc lại trạng thái từ tác vụ; báo giao diện mọi thuộc tính.</summary>
        public void Refresh(DateTime utcNow)
        {
            var running = Task.IsRunning;
            StateText = TaskBoard.StateText(Task);
            StateBrush = Res(running ? "Accent" : Task.State == TaskState.Done ? "Ok" : Task.State == TaskState.Failed ? "Danger" : "Muted");
            // Có dấu phân cách đi kèm để dòng không có cổng không để lại " · " thừa.
            PortText = Task.Port.HasValue ? " · cổng " + Task.Port.Value : string.Empty;
            TimeText = TaskBoard.FormatElapsed(TaskBoard.Elapsed(Task, utcNow));
            StopVisibility = running ? Visibility.Visible : Visibility.Collapsed;
            RestartVisibility = running ? Visibility.Collapsed : Visibility.Visible;
            Raise();
        }

        private void Raise()
        {
            foreach (var name in new[]
            {
                nameof(StateText), nameof(StateBrush), nameof(PortText), nameof(TimeText),
                nameof(StopVisibility), nameof(RestartVisibility),
            })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    }

    /// <summary>Danh sách <see cref="TaskItem"/> giữ nguyên đối tượng qua các lần đồng bộ.</summary>
    internal sealed class TaskItems
    {
        private readonly Dictionary<string, TaskItem> _byId = new Dictionary<string, TaskItem>();

        public ObservableCollection<TaskItem> Items { get; } = new ObservableCollection<TaskItem>();

        /// <param name="tasks">Đã sắp xếp (<see cref="TaskBoard.Ordered"/>), có thể đã cắt bớt.</param>
        public void Sync(IReadOnlyList<TaskInfo> tasks, Func<string, string> workspaceName, DateTime utcNow)
        {
            var next = new List<TaskItem>(tasks.Count);
            foreach (var task in tasks)
            {
                if (!_byId.TryGetValue(task.Id, out var item))
                {
                    item = new TaskItem(task, workspaceName(task.Spec.WorkspaceId));
                    _byId[task.Id] = item;
                }
                next.Add(item);
            }

            var keep = new HashSet<string>(tasks.Select(t => t.Id));
            foreach (var id in _byId.Keys.Where(id => !keep.Contains(id)).ToList())
                _byId.Remove(id);

            // Chỉ dựng lại danh sách khi tập hoặc thứ tự đổi (tác vụ bật/kết thúc); còn lại chỉ cập nhật chữ.
            if (!Items.SequenceEqual(next))
            {
                Items.Clear();
                foreach (var item in next)
                    Items.Add(item);
            }
            foreach (var item in next)
                item.Refresh(utcNow);
        }
    }

    /// <summary>Bộ lọc trạng thái trên trang Tác vụ.</summary>
    internal enum TaskStateFilter { All, Running, Failed, Finished }

    /// <summary>Lọc danh sách tác vụ và dòng log. Thuần logic, không đụng giao diện.</summary>
    internal static class TaskFilter
    {
        private static readonly Regex ErrorLine = new Regex(
            @"(error|errors|fail|failed|failure|exception|fatal)|lỗi|thất bại",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="workspaceId">null/rỗng = mọi workspace.</param>
        /// <param name="query">Khớp (không phân biệt hoa thường) với nhãn hoặc lệnh; rỗng = không lọc.</param>
        public static bool Matches(TaskInfo task, TaskStateFilter state, string workspaceId, string query)
        {
            switch (state)
            {
                case TaskStateFilter.Running when !task.IsRunning:
                case TaskStateFilter.Failed when task.State != TaskState.Failed:
                case TaskStateFilter.Finished when task.IsRunning || task.State == TaskState.Failed:
                    return false;
            }
            if (!string.IsNullOrEmpty(workspaceId)
                && !string.Equals(task.Spec.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrWhiteSpace(query))
                return true;
            var q = query.Trim();
            return Contains(task.Spec.Label, q) || Contains(task.Spec.CommandLine, q);
        }

        /// <summary>Giữ các dòng log thoả cả "chỉ lỗi" và ô tìm.</summary>
        public static string[] LogLines(IEnumerable<string> lines, bool errorsOnly, string query)
        {
            var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            return lines.Where(l => (!errorsOnly || ErrorLine.IsMatch(l)) && (q == null || Contains(l, q))).ToArray();
        }

        private static bool Contains(string text, string q) =>
            text != null && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Thao tác dùng chung cho dòng tác vụ; báo lỗi cho người dùng thay vì để ném ra giao diện.</summary>
    internal static class TaskActions
    {
        /// <returns>Lần chạy mới, hoặc null nếu không chạy lại được (đã báo lỗi).</returns>
        public static TaskInfo Restart(TaskHost tasks, TaskInfo task, Window owner)
        {
            try
            {
                return tasks.Restart(task.Id);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException
                                       || ex is System.ComponentModel.Win32Exception || ex is IOException)
            {
                Dialogs.Error(owner, "Không chạy lại được: " + ex.Message);
                return null;
            }
        }

        public static void OpenLogFile(TaskInfo task, Window owner)
        {
            if (task.LogPath == null || !File.Exists(task.LogPath))
            {
                Dialogs.Error(owner, "Không tìm thấy file log. Tác vụ này có thể chạy ở cửa sổ riêng hoặc log đã bị xoá.");
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + task.LogPath + "\"") { UseShellExecute = true });
        }
    }
}
