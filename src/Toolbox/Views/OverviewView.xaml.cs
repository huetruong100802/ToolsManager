using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Trang Tổng quan: số workspace, tác vụ đang chạy, cổng đang dùng, danh sách workspace và tác vụ gần đây.</summary>
    public partial class OverviewView : UserControl
    {
        private const int RecentCount = 8;

        private readonly TaskItems _recent = new TaskItems();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private TaskHost _tasks;
        private ObservableCollection<WorkspaceRow> _workspaces;

        /// <summary>Yêu cầu mở log của một tác vụ (ở panel inspector).</summary>
        public event Action<TaskInfo> ShowLogRequested;

        /// <summary>Yêu cầu mở trang của một workspace.</summary>
        public event Action<WorkspaceRow> OpenWorkspaceRequested;

        public OverviewView()
        {
            InitializeComponent();
            RecentList.ItemsSource = _recent.Items;
            _timer.Tick += (s, e) => { if (IsVisible) Refresh(); };
        }

        public void Initialize(TaskHost tasks, ObservableCollection<WorkspaceRow> workspaces)
        {
            _tasks = tasks;
            _workspaces = workspaces;
            WorkspaceList.ItemsSource = workspaces;
            _timer.Start();
        }

        /// <summary>Cập nhật số liệu và danh sách gần đây. Gọi khi tác vụ đổi, và mỗi giây khi đang hiện.</summary>
        public void Refresh()
        {
            if (_tasks == null)
                return;

            var workspaceCount = _workspaces.Count;
            var missing = _workspaces.Count(r => r.IsMissing);
            Subtitle.Text = workspaceCount == 0
                ? "Chưa có workspace nào."
                : $"Toolbox đang theo dõi {workspaceCount} workspace.";
            WorkspaceCount.Text = workspaceCount.ToString();
            WorkspaceDetail.Text = missing == 0 ? "Thư mục đều còn đủ" : $"{missing} thư mục không còn ở đường dẫn cũ";
            NoWorkspaceNote.Visibility = workspaceCount == 0 ? Visibility.Visible : Visibility.Collapsed;

            var running = _tasks.Running();
            RunningCount.Text = running.Count.ToString();
            RunningDetail.Text = running.Count == 0 ? "Không có tác vụ chạy ngầm" : "Tác vụ chạy ngầm đang hoạt động";
            var ports = TaskBoard.PortsOf(running);
            PortList.Text = ports.Count == 0 ? "—" : string.Join(", ", ports);

            var ordered = TaskBoard.Ordered(_tasks.Snapshot());
            _recent.Sync(ordered.Take(RecentCount).ToList(), WorkspaceName, DateTime.UtcNow);
            NoRecentNote.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string WorkspaceName(string workspaceId)
        {
            var row = _workspaces.FirstOrDefault(r => string.Equals(r.Workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
            return row?.Name ?? "workspace đã gỡ";
        }

        private void OnWorkspaceClick(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as WorkspaceRow;
            if (row != null)
                OpenWorkspaceRequested?.Invoke(row);
        }

        private void OnShowLog(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as TaskItem;
            if (item != null)
                ShowLogRequested?.Invoke(item.Task);
        }
    }
}
