using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>Khung ứng dụng: rail trái (Thư viện, workspace) và vùng nội dung đổi trang.</summary>
    public partial class MainWindow : Window
    {
        /// <summary>Trang đang hiện trong vùng nội dung.</summary>
        private enum Area { Library, Overview, Tasks, Agents, Workspace }

        private readonly ObservableCollection<WorkspaceRow> _rows = new ObservableCollection<WorkspaceRow>();
        private readonly TaskHost _tasks = new TaskHost(TaskHost.DefaultRunsDir);
        private ICollectionView _rowsView;
        private WorkspaceStore _workspaces;
        private WorkspaceRow _current;
        private Area _area = Area.Library;
        private StoreWatcher _watcher;

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
            AgentsPage.ConnectRequested += Library.OpenAgentWindow;
            // Rail: ghim lên đầu, còn lại theo tên; ô tìm lọc theo tên (hiện khi workspace nhiều).
            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceRow.IsPinned), ListSortDirection.Descending));
            _rowsView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceRow.Name), ListSortDirection.Ascending));
            _rowsView.Filter = o => MatchesSearch(o as WorkspaceRow);
            WorkspaceList.ItemsSource = _rowsView;
            ThemeText.Text = Theme.Label(Theme.Mode);
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
            // Đọc danh mục tool lúc mở; catalog.json lỗi thì báo luôn, không để lặng lẽ dùng mặc định.
            _ = ToolPresets.All;
            if (ToolPresets.LastError != null)
                Dialogs.Error(this, ToolPresets.LastError);
            _ = ToolCatalog.All;
            if (ToolCatalog.LastError != null)
                Dialogs.Error(this, ToolCatalog.LastError);

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
            StartWatching();
        }

        // ---- Đồng bộ trực tiếp với agent ----

        /// <summary>Theo dõi file dữ liệu: agent ghi qua MCP thì GUI nạp lại sau chừng 300 ms, không cần đổi trang.</summary>
        private void StartWatching()
        {
            _watcher = new StoreWatcher(DataDir.Root, TimeSpan.FromMilliseconds(300));
            _watcher.Changed += files => Dispatcher.BeginInvoke(new Action(() => OnStoreChanged(files)));
            Closed += (s, e) => _watcher.Dispose();
        }

        private void OnStoreChanged(IReadOnlyList<string> files)
        {
            var toolsChanged = files.Contains("tools.json", StringComparer.OrdinalIgnoreCase) && Library.SyncFromDisk();

            var changed = files.Contains("workspaces.json", StringComparer.OrdinalIgnoreCase)
                ? SyncFromDisk()
                : new List<string>();
            // Tool đổi cũng làm đổi danh sách lệnh của workspace, nên nạp lại trang workspace khi workspace đó hoặc tool đổi.
            if (_area == Area.Workspace && _current != null
                && (toolsChanged || changed.Contains(_current.Workspace.Id, StringComparer.OrdinalIgnoreCase)))
                WorkspaceContent.Load(_current.Workspace);

            RefreshRows();
            if (_area == Area.Agents)
                AgentsPage.Refresh();
        }

        // ---- Rail ----

        private void RefreshRows()
        {
            var running = _tasks.Running();
            foreach (var row in _rows)
                row.Refresh(running.Count(t => string.Equals(t.Spec.WorkspaceId, row.Workspace.Id, StringComparison.OrdinalIgnoreCase)));
            NoWorkspaceHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var archived = _rows.Count(r => r.IsArchived);
            ArchivedToggle.Visibility = archived > 0 ? Visibility.Visible : Visibility.Collapsed;
            ArchivedToggle.Content = _showArchived ? "Ẩn workspace đã lưu trữ" : $"Lưu trữ ({archived})";
            WorkspaceSearchBox.Visibility = _rows.Count >= SearchThreshold ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = TaskBoard.StatusLine(running);
            TasksBadge.Visibility = running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TasksBadgeCount.Text = running.Count.ToString();

            if (_area == Area.Overview)
                OverviewPage.Refresh();
            else if (_area == Area.Tasks)
                TasksPage.Refresh();

            var pending = Views.AgentsView.PendingCount();
            AgentsBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
            AgentsBadgeCount.Text = pending.ToString();

            if (_current != null)
            {
                MissingBanner.Visibility = _current.IsMissing ? Visibility.Visible : Visibility.Collapsed;
                WorkspaceContent.Refresh();
            }
        }

        /// <summary>Từ chừng này workspace trở lên mới hiện ô tìm, để rail gọn khi ít workspace.</summary>
        private const int SearchThreshold = 6;

        private bool MatchesSearch(WorkspaceRow row)
        {
            var query = Core.Text.Fold(WorkspaceSearch?.Text ?? "");
            return row != null && (_showArchived || !row.IsArchived) && (query.Length == 0 || Core.Text.Fold(row.Name).Contains(query));
        }

        /// <summary>Có hiện workspace đã lưu trữ trong rail không (bật bằng nút "Lưu trữ (n)").</summary>
        private bool _showArchived;

        private void OnToggleArchived(object sender, RoutedEventArgs e)
        {
            _showArchived = !_showArchived;
            RefreshRows();
            _rowsView.Refresh();
        }

        private void OnToggleArchive(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as WorkspaceRow;
            if (row == null)
                return;
            row.ToggleArchive();
            TrySave();
            RefreshRows();
            _rowsView.Refresh();
        }

        private void OnWorkspaceSearch(object sender, TextChangedEventArgs e)
        {
            WorkspaceSearchHint.Visibility = WorkspaceSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _rowsView?.Refresh();
        }

        private void OnTogglePin(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as WorkspaceRow;
            if (row == null)
                return;
            row.TogglePin();
            TrySave();
            _rowsView.Refresh();
        }

        private void OnThemeClick(object sender, RoutedEventArgs e)
        {
            Theme.SetMode(Theme.Next(Theme.Mode));
            ThemeText.Text = Theme.Label(Theme.Mode);
            var settings = Settings.Load();
            settings.Theme = Theme.Name(Theme.Mode);
            settings.Save();
        }

        private void OnLibraryClick(object sender, RoutedEventArgs e) => Navigate(null);

        private void OnOverviewClick(object sender, RoutedEventArgs e) => GoTo(Area.Overview);

        private void OnTasksClick(object sender, RoutedEventArgs e) => GoTo(Area.Tasks);

        private void OnStatusTasksClick(object sender, RoutedEventArgs e) => GoTo(Area.Tasks);

        private void OnAgentsClick(object sender, RoutedEventArgs e) => GoTo(Area.Agents);

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
        private IReadOnlyList<string> SyncFromDisk()
        {
            IReadOnlyList<string> changed;
            try
            {
                changed = _workspaces.Sync();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new List<string>();
            }
            foreach (var workspace in _workspaces.Workspaces)
            {
                if (!_rows.Any(r => ReferenceEquals(r.Workspace, workspace)))
                    _rows.Add(new WorkspaceRow(workspace));
            }
            RefreshRows();
            return changed;
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
            AgentsNav.IsChecked = area == Area.Agents;

            Library.Visibility = Visible(area == Area.Library);
            OverviewPage.Visibility = Visible(area == Area.Overview);
            TasksPage.Visibility = Visible(area == Area.Tasks);
            AgentsPage.Visibility = Visible(area == Area.Agents);
            WorkspacePage.Visibility = Visible(area == Area.Workspace);
            if (area == Area.Overview)
                OverviewPage.Refresh();
            else if (area == Area.Tasks)
                TasksPage.Refresh();
            else if (area == Area.Agents)
                AgentsPage.Refresh();

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

            // Tool nhận diện được là preset: đăng ký vào thư viện để workspace mới dùng được ngay như mọi tool khác.
            try
            {
                var library = ToolStore.Load(ToolStore.DefaultPath);
                if (ToolPresets.EnsureInLibrary(library, dialog.Result.Bindings.Select(b => b.ToolId)))
                    library.Save();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(this, "Không ghi được tool nhận diện vào thư viện: " + ex.Message);
                return;
            }

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
