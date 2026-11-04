using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>Khung ứng dụng: rail trái (Thư viện, workspace) và vùng nội dung đổi trang.</summary>
    public partial class MainWindow : Window
    {
        /// <summary>Trang đang hiện trong vùng nội dung.</summary>
        private enum Area { Library, Overview, Tasks, Workspace }

        private readonly ObservableCollection<WorkspaceRow> _rows = new ObservableCollection<WorkspaceRow>();
        private readonly TaskHost _tasks = new TaskHost(TaskHost.DefaultRunsDir);
        private WorkspaceStore _workspaces;
        private WorkspaceRow _current;
        private Area _area = Area.Library;

        public MainWindow(string fileToOpen = null)
        {
            InitializeComponent();
            Library.FileToOpen = fileToOpen;
            WorkspaceContent.Initialize(_tasks, TrySave);
            Inspector.Initialize(_tasks);
            OverviewPage.Initialize(_tasks, _rows);
            TasksPage.Initialize(_tasks, _rows);
            OverviewPage.ShowLogRequested += Inspector.Show;
            TasksPage.ShowLogRequested += Inspector.Show;
            OverviewPage.OpenWorkspaceRequested += row => Navigate(row);
            WorkspaceList.ItemsSource = _rows;
            Loaded += OnLoaded;
            Closing += OnClosing;
            // Tác vụ đổi trạng thái đến từ luồng khác: chuyển về luồng giao diện.
            _tasks.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshRows));
            // Quay lại cửa sổ → kiểm lại thư mục workspace còn hay mất.
            Activated += (s, e) => RefreshRows();
        }

        /// <summary>Tác vụ ngầm dùng chung cho các trang workspace.</summary>
        public TaskHost Tasks => _tasks;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _workspaces = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
            }
            catch (InvalidDataException ex)
            {
                // Không ghi đè file hỏng; thư viện vẫn dùng được, chỉ tắt phần workspace.
                AddWorkspaceButton.IsEnabled = false;
                Dialogs.Error(this, ex.Message + "\n\nWorkspace tạm thời không dùng được. Hãy sửa hoặc xoá file này rồi mở lại Toolbox.");
                return;
            }

            foreach (var workspace in _workspaces.Workspaces)
                _rows.Add(new WorkspaceRow(workspace));
            RefreshRows();
        }

        // ---- Rail ----

        private void RefreshRows()
        {
            var running = _tasks.Running();
            foreach (var row in _rows)
                row.Refresh(running.Count(t => string.Equals(t.Spec.WorkspaceId, row.Workspace.Id, StringComparison.OrdinalIgnoreCase)));
            NoWorkspaceHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = TaskBoard.StatusLine(running);
            TasksBadge.Visibility = running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TasksBadgeCount.Text = running.Count.ToString();

            if (_area == Area.Overview)
                OverviewPage.Refresh();
            else if (_area == Area.Tasks)
                TasksPage.Refresh();

            if (_current != null)
            {
                MissingBanner.Visibility = _current.IsMissing ? Visibility.Visible : Visibility.Collapsed;
                WorkspaceContent.Refresh();
            }
        }

        private void OnLibraryClick(object sender, RoutedEventArgs e) => Navigate(null);

        private void OnOverviewClick(object sender, RoutedEventArgs e) => GoTo(Area.Overview);

        private void OnTasksClick(object sender, RoutedEventArgs e) => GoTo(Area.Tasks);

        private void OnStatusTasksClick(object sender, RoutedEventArgs e) => GoTo(Area.Tasks);

        private void OnWorkspaceClick(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as WorkspaceRow;
            if (row != null)
                Navigate(row);
        }

        /// <summary>Đổi sang trang workspace; <c>null</c> là Thư viện.</summary>
        private void Navigate(WorkspaceRow row) => GoTo(row == null ? Area.Library : Area.Workspace, row);

        /// <summary>Đổi trang trong vùng nội dung. <paramref name="row"/> chỉ dùng cho <see cref="Area.Workspace"/>.</summary>
        /// <summary>
        /// Agent qua MCP có thể đăng ký workspace hoặc gắn tool khi Toolbox đang mở. Nạp lại trước khi chuyển trang,
        /// để lần lưu sau của GUI không ghi đè các thay đổi đó.
        /// </summary>
        private void SyncFromDisk()
        {
            WorkspaceStore fresh;
            try
            {
                fresh = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return;
            }
            _workspaces.MergeFrom(fresh);
            foreach (var workspace in _workspaces.Workspaces)
            {
                if (!_rows.Any(r => ReferenceEquals(r.Workspace, workspace)))
                    _rows.Add(new WorkspaceRow(workspace));
            }
            RefreshRows();
        }

        private void GoTo(Area area, WorkspaceRow row = null)
        {
            if (_workspaces != null)
                SyncFromDisk();
            _area = area;
            _current = area == Area.Workspace ? row : null;
            foreach (var r in _rows)
                r.IsSelected = ReferenceEquals(r, _current);
            LibraryNav.IsChecked = area == Area.Library;
            OverviewNav.IsChecked = area == Area.Overview;
            TasksNav.IsChecked = area == Area.Tasks;

            Library.Visibility = Visible(area == Area.Library);
            OverviewPage.Visibility = Visible(area == Area.Overview);
            TasksPage.Visibility = Visible(area == Area.Tasks);
            WorkspacePage.Visibility = Visible(area == Area.Workspace);
            if (area == Area.Overview)
                OverviewPage.Refresh();
            else if (area == Area.Tasks)
                TasksPage.Refresh();

            if (_current == null)
                return;

            WsName.Text = _current.Name;
            WsPath.Text = _current.Path;
            WsDot.Fill = _current.ColorBrush;
            MissingBanner.Visibility = _current.IsMissing ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceContent.Load(_current.Workspace);
        }

        private static Visibility Visible(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        // ---- Thêm / gỡ / trỏ lại ----

        private void OnAddWorkspace(object sender, RoutedEventArgs e)
        {
            if (_workspaces == null)
                return;
            var dialog = new AddWorkspaceWindow(_workspaces.Workspaces.Select(w => w.Path)) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var workspace = _workspaces.Add(dialog.Result);
            if (!TrySave())
            {
                _workspaces.Remove(workspace.Id);
                return;
            }

            var row = new WorkspaceRow(workspace);
            _rows.Add(row);
            RefreshRows();
            Navigate(row);
        }

        private void OnRemoveWorkspace(object sender, RoutedEventArgs e)
        {
            var row = _current;
            if (row == null)
                return;

            var running = _tasks.Running().Where(t => string.Equals(t.Spec.WorkspaceId, row.Workspace.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            var message = $"Gỡ workspace \"{row.Name}\" khỏi Toolbox?\n\nThư mục {row.Path} trên đĩa không bị xoá.";
            if (running.Count > 0)
                message += $"\n\nĐang có {running.Count} tác vụ chạy ngầm của workspace này; gỡ sẽ dừng chúng.";
            if (!Dialogs.Confirm(this, message, running.Count > 0))
                return;

            foreach (var task in running)
                _tasks.Stop(task.Id);
            _workspaces.Remove(row.Workspace.Id);
            WorkspaceContent.Forget(row.Workspace.Id);
            // Ghi lỗi thì đã báo người dùng; bộ nhớ vẫn đúng và lần lưu sau sẽ ghi lại việc gỡ này.
            TrySave();

            _rows.Remove(row);
            RefreshRows();
            Navigate(null);
        }

        private void OnRelocateWorkspace(object sender, RoutedEventArgs e)
        {
            var row = _current;
            if (row == null)
                return;

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = $"Chọn thư mục mới cho \"{row.Name}\"" })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var old = row.Workspace.Path;
                row.Workspace.Path = dialog.SelectedPath;
                if (!TrySave())
                {
                    row.Workspace.Path = old;
                    return;
                }
            }

            // Thư mục mới có thể có script/cổng khác: bỏ form đã nhớ rồi dựng lại.
            WorkspaceContent.Forget(row.Workspace.Id);
            RefreshRows();
            Navigate(row);
        }

        private void OnOpenWorkspaceFolder(object sender, RoutedEventArgs e)
        {
            var row = _current;
            if (row == null)
                return;
            if (!row.Workspace.PathExists)
            {
                Dialogs.Error(this, "Không tìm thấy thư mục: " + row.Path);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + row.Workspace.ResolvedPath + "\"") { UseShellExecute = true });
        }

        private bool TrySave()
        {
            try
            {
                _workspaces.Save();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(this, "Không lưu được workspace: " + ex.Message);
                return false;
            }
        }

        // ---- Đóng cửa sổ ----

        /// <summary>Tác vụ ngầm chạy bằng pipe nên chết theo Toolbox; hỏi trước rồi dừng sạch.</summary>
        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var running = _tasks.Running();
            if (running.Count == 0)
                return;

            var names = string.Join("\n", running.Take(5).Select(t => "• " + t.Spec.Label)) + (running.Count > 5 ? "\n…" : "");
            if (!Dialogs.Confirm(this, $"Đang có {running.Count} tác vụ chạy ngầm:\n{names}\n\nĐóng Toolbox sẽ dừng chúng. Vẫn đóng?", warning: true))
            {
                e.Cancel = true;
                return;
            }
            _tasks.StopAll();
        }
    }
}
