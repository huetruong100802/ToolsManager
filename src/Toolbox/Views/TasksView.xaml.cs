using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Trang Tác vụ: mọi tác vụ đang chạy và đã kết thúc, kèm xem log, dừng, chạy lại.</summary>
    public partial class TasksView : UserControl
    {
        private readonly TaskItems _items = new TaskItems();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private TaskHost _tasks;
        private ObservableCollection<WorkspaceRow> _workspaces;

        /// <summary>Yêu cầu mở log của một tác vụ (ở panel inspector).</summary>
        public event Action<TaskInfo> ShowLogRequested;

        public TasksView()
        {
            InitializeComponent();
            TaskList.ItemsSource = _items.Items;
            _timer.Tick += (s, e) => { if (IsVisible) Refresh(); };
        }

        private Window Host => Window.GetWindow(this);

        public void Initialize(TaskHost tasks, ObservableCollection<WorkspaceRow> workspaces)
        {
            _tasks = tasks;
            _workspaces = workspaces;
            _timer.Start();
        }

        /// <summary>Đồng bộ danh sách với tác vụ hiện có. Gọi khi tác vụ đổi, và mỗi giây để cập nhật thời gian.</summary>
        public void Refresh()
        {
            if (_tasks == null)
                return;
            var ordered = TaskBoard.Ordered(_tasks.Snapshot());
            _items.Sync(ordered, WorkspaceName, DateTime.UtcNow);

            var running = ordered.Count(t => t.IsRunning);
            Summary.Text = $"Đang chạy {running} · đã kết thúc {ordered.Count - running}";
            EmptyHint.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string WorkspaceName(string workspaceId)
        {
            var row = _workspaces?.FirstOrDefault(r => string.Equals(r.Workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
            return row?.Name ?? "workspace đã gỡ";
        }

        private void OnShowLog(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as TaskItem;
            if (item != null)
                ShowLogRequested?.Invoke(item.Task);
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
                ShowLogRequested?.Invoke(fresh);
        }
    }
}
