using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Trang Tác vụ: danh sách có bộ lọc, khung log của tác vụ đang chọn ở dưới, dừng, chạy lại.</summary>
    public partial class TasksView : UserControl
    {
        private const string NoSelectionNote = "Chọn một tác vụ ở trên để xem log.";
        private const string WindowTaskNote = "Tác vụ này chạy ở cửa sổ terminal riêng nên Toolbox không ghi log.";
        private const string EmptyNote = "Chưa có output.";
        private const string NoMatchNote = "Không có dòng nào khớp bộ lọc.";

        private readonly TaskItems _items = new TaskItems();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private TaskHost _tasks;
        private ObservableCollection<WorkspaceRow> _workspaces;
        private TaskStateFilter _stateFilter = TaskStateFilter.All;
        private string _workspaceKey = string.Empty;
        private TaskInfo _selected;
        private volatile bool _logDirty;
        private bool _loading;

        /// <summary>Yêu cầu mở log của một tác vụ ở cửa sổ riêng (panel inspector).</summary>
        public event Action<TaskInfo> ShowLogRequested;

        public TasksView()
        {
            InitializeComponent();
            TaskList.ItemsSource = _items.Items;
            _timer.Tick += (s, e) => { if (IsVisible) Refresh(); };
            _logTimer.Tick += (s, e) => LogTick();
            // Đồng hồ chỉ chạy khi trang đang hiện; ẩn thì dừng hẳn, không đánh thức luồng giao diện mỗi giây.
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && _tasks != null)
                {
                    _timer.Start();
                    _logTimer.Start();
                    Refresh();
                }
                else
                {
                    _timer.Stop();
                    _logTimer.Stop();
                }
            };
            RenderLog(toBottom: true);
        }

        private Window Host => Window.GetWindow(this);

        public void Initialize(TaskHost tasks, ObservableCollection<WorkspaceRow> workspaces)
        {
            _tasks = tasks;
            _workspaces = workspaces;
            tasks.LineAdded += task => _logDirty = true;
            // Kết thúc tác vụ không đi qua LineAdded, nên cần cờ riêng để có dòng trạng thái cuối.
            tasks.Changed += () => _logDirty = true;
            if (IsVisible)
            {
                _timer.Start();
                _logTimer.Start();
            }
        }

        /// <summary>Đồng bộ danh sách với tác vụ hiện có. Gọi khi tác vụ đổi, và mỗi giây để cập nhật thời gian.</summary>
        public void Refresh()
        {
            if (_tasks == null)
                return;
            SyncWorkspaceChoices();

            var all = TaskBoard.Ordered(_tasks.Snapshot());
            var query = TaskSearch.Text;
            var shown = all.Where(t => TaskFilter.Matches(t, _stateFilter, _workspaceKey, query)).ToList();
            _items.Sync(shown, WorkspaceName, DateTime.UtcNow);

            // Giữ lựa chọn khi tác vụ vẫn còn trong danh sách; mất (bị lọc hoặc dọn) thì bỏ chọn.
            if (_selected != null && shown.All(t => t.Id != _selected.Id))
                Select(null);
            else
                MarkSelected();

            var running = all.Count(t => t.IsRunning);
            Summary.Text = $"Đang chạy {running} · đã kết thúc {all.Count - running}";
            StopAllButton.IsEnabled = running > 0;
            if (all.Count == 0)
                EmptyHint.Text = "Chưa có tác vụ nào. Chạy lệnh trong một workspace để thấy tác vụ ở đây.";
            else if (shown.Count == 0)
                EmptyHint.Text = "Không có tác vụ nào khớp bộ lọc.";
            EmptyHint.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string WorkspaceName(string workspaceId)
        {
            var row = _workspaces?.FirstOrDefault(r => string.Equals(r.Workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
            return row?.Name ?? "workspace đã gỡ";
        }

        /// <summary>Danh sách workspace trong ô lọc đổi khi thêm/xoá/đổi tên workspace.</summary>
        private void SyncWorkspaceChoices()
        {
            var signature = string.Join("|", _workspaces.Select(r => r.Workspace.Id + ":" + r.Name));
            if (Equals(WorkspaceFilter.Tag, signature))
                return;
            WorkspaceFilter.Tag = signature;

            _loading = true;
            try
            {
                var keep = _workspaceKey;
                WorkspaceFilter.Items.Clear();
                WorkspaceFilter.Items.Add(new ComboBoxItem { Content = "Mọi workspace", Tag = string.Empty });
                foreach (var row in _workspaces.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                    WorkspaceFilter.Items.Add(new ComboBoxItem { Content = row.Name, Tag = row.Workspace.Id });

                var match = WorkspaceFilter.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals((string)i.Tag, keep, StringComparison.OrdinalIgnoreCase));
                WorkspaceFilter.SelectedItem = match ?? WorkspaceFilter.Items[0];
                _workspaceKey = (string)((ComboBoxItem)WorkspaceFilter.SelectedItem).Tag;
            }
            finally
            {
                _loading = false;
            }
        }

        private void OnFilterChip(object sender, RoutedEventArgs e)
        {
            var chip = (ToggleButton)sender;
            _stateFilter = (TaskStateFilter)Enum.Parse(typeof(TaskStateFilter), (string)chip.Tag);
            foreach (var other in new[] { FilterAll, FilterRunning, FilterFailed, FilterFinished })
                other.IsChecked = ReferenceEquals(other, chip);
            Refresh();
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsInitialized || _tasks == null)
                return;
            if (WorkspaceFilter.SelectedItem is ComboBoxItem item)
                _workspaceKey = (string)item.Tag;
            Refresh();
        }

        private void OnStopAll(object sender, RoutedEventArgs e)
        {
            // Dừng đúng những tác vụ đang hiện theo bộ lọc, không đụng tác vụ của workspace khác đang bị lọc ẩn.
            var targets = _items.Items.Where(i => i.Task.IsRunning).ToList();
            if (targets.Count == 0)
                return;
            if (!Dialogs.Confirm(Host, $"Dừng {targets.Count} tác vụ đang chạy?"))
                return;
            foreach (var item in targets)
                _tasks.Stop(item.Id);
        }

        private void OnSelectRow(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TaskItem item)
                Select(item.Task);
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as TaskItem;
            if (item != null)
                _tasks.Stop(item.Id);
        }

        private void OnRestart(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as TaskItem;
            if (item == null)
                return;
            var fresh = TaskActions.Restart(_tasks, item.Task, Host);
            if (fresh != null)
            {
                Refresh();
                Select(fresh);
            }
        }

        // ---- khung log ----

        private void Select(TaskInfo task)
        {
            _selected = task;
            MarkSelected();
            _logDirty = true;
            RenderLog(toBottom: true);
        }

        private void MarkSelected()
        {
            foreach (var item in _items.Items)
                item.IsSelected = _selected != null && item.Id == _selected.Id;
        }

        private void LogTick()
        {
            if (_selected == null || !IsVisible)
                return;
            if (_logDirty)
            {
                _logDirty = false;
                RenderLog(toBottom: false);
            }
        }

        private void OnLogFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;
            RenderLog(toBottom: true);
        }

        /// <param name="toBottom">Chọn tác vụ mới hoặc đổi bộ lọc: xuống cuối. Cập nhật định kỳ: giữ vị trí đang đọc, trừ khi đang ở cuối.</param>
        private void RenderLog(bool toBottom)
        {
            var task = _selected;
            var hasTask = task != null;
            DetachButton.IsEnabled = hasTask;
            OpenFileButton.IsEnabled = hasTask && task.LogPath != null;
            LogTitle.Text = hasTask ? $"Log · {task.Spec.Label} · {TaskBoard.StateText(task)}" : "Log";

            if (!hasTask)
            {
                Log.Text = NoSelectionNote;
                return;
            }
            if (task.LogPath == null)
            {
                Log.Text = WindowTaskNote;
                return;
            }

            var raw = task.Lines();
            var lines = TaskFilter.LogLines(raw, ErrorsOnly.IsChecked == true, LogSearch.Text);
            var atBottom = toBottom || Log.LineCount <= 1 || Log.GetLastVisibleLineIndex() >= Log.LineCount - 2;
            var firstVisible = Log.GetFirstVisibleLineIndex();

            Log.Text = raw.Length == 0 ? EmptyNote
                : lines.Length == 0 ? NoMatchNote
                : string.Join(Environment.NewLine, lines);
            if (atBottom)
                Log.ScrollToEnd();
            else if (firstVisible >= 0)
                Log.ScrollToLine(Math.Min(firstVisible, Math.Max(0, Log.LineCount - 1)));
        }

        private void OnOpenLogFile(object sender, RoutedEventArgs e)
        {
            if (_selected != null)
                TaskActions.OpenLogFile(_selected, Host);
        }

        private void OnDetach(object sender, RoutedEventArgs e)
        {
            if (_selected != null)
                ShowLogRequested?.Invoke(_selected);
        }
    }
}
