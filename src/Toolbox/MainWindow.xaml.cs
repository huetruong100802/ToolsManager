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
using Toolbox.Core.Agents;
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
        private readonly Settings _settings = Settings.Load();
        private readonly Notifier _notifier = new Notifier();

        public MainWindow(string fileToOpen = null)
        {
            InitializeComponent();
            Library.FileToOpen = fileToOpen;
            WorkspaceContent.Initialize(_tasks, TrySave);
            Inspector.Initialize(_tasks);
            TasksPage.Initialize(_tasks, _rows);
            Dock.LogRequested += Inspector.Show;
            WorkspaceContent.ShowLogRequested += ShowLog;
            WorkspaceContent.SheetRequested += (sheet, done) => Sheet.Open(sheet, done);
            Inspector.SetWorkspaceRootResolver(id => _workspaces?.Find(id)?.ResolvedPath);
            Inspector.HintActionRequested += OnHintAction;
            Inspector.AskAgentRequested += OnAskAgentAboutTask;
            WorkspaceContent.ConnectAgentRequested += Library.OpenAgentWindow;
            Dock.StopRequested += task => _tasks.Stop(task.Id);
            Dock.OpenPortRequested += port => OpenInBrowser("http://localhost:" + port);
            Dock.AllRequested += () => GoTo(Area.Tasks);
            OverviewPage.Initialize(BuildHomeFeed);
            OverviewPage.ItemActivated += OnHomeItem;
            OverviewPage.StepActivated += OnHomeStep;
            TasksPage.ShowLogRequested += ShowLog;
            AgentsPage.ConnectRequested += Library.OpenAgentWindow;
            // Rail: ghim lên đầu, còn lại theo tên; ô tìm lọc theo tên (hiện khi workspace nhiều).
            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceRow.IsPinned), ListSortDirection.Descending));
            _rowsView.SortDescriptions.Add(new SortDescription(nameof(WorkspaceRow.Name), ListSortDirection.Ascending));
            _rowsView.Filter = o => MatchesSearch(o as WorkspaceRow);
            WorkspaceList.ItemsSource = _rowsView;
            ThemeText.Text = Theme.Label(Theme.Mode);
            Views.Toasts.Host = ToastLayer;
            RefreshNotifyText();
            Palette.Initialize(BuildSearchIndex);
            Palette.Activated += OnPaletteActivated;
            Palette.RevealRequested += OnPaletteReveal;
            PreviewKeyDown += OnWindowPreviewKeyDown;
            Loaded += OnLoaded;
            Closing += OnClosing;
            // Tác vụ đổi trạng thái đến từ luồng khác: chuyển về luồng giao diện.
            _tasks.Changed += () => Dispatcher.BeginInvoke(new Action(() => { RefreshRows(); CheckNotifications(); }));
            _notifier.Clicked += task => { Activate(); ShowLog(task); };
            Closed += (s, e) => _notifier.Dispose();
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
                Dialogs.Fatal(this, ex.Message + "\n\nWorkspace tạm thời không dùng được. Hãy sửa hoặc xoá file này rồi mở lại Toolbox.");
                return;
            }

            foreach (var workspace in _workspaces.Workspaces)
                _rows.Add(new WorkspaceRow(workspace));
            RefreshRows();
            StartWatching();
            StartPresence();
            MarkRunRequestsAnnounced();
            // Mở app là thấy việc cần làm, trừ khi được mở bằng file .toolbox (đi thẳng tới Thư viện để nhập).
            if (string.IsNullOrEmpty(Library.FileToOpen))
                GoTo(Area.Overview);
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

            if (files.Contains("activity.jsonl", StringComparer.OrdinalIgnoreCase))
                ShowAgentActivity();
            if (files.Contains("proposals.json", StringComparer.OrdinalIgnoreCase))
                AnnounceRunRequests();
            if (files.Contains("agents.json", StringComparer.OrdinalIgnoreCase) || files.Contains("activity.jsonl", StringComparer.OrdinalIgnoreCase))
                RefreshPresence();

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

            Dock.Update(_tasks.Snapshot());
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

        /// <summary>Mở log một tác vụ; nếu đó là lỗi đang nhắc ở dock thì coi như đã xem.</summary>
        private void ShowLog(TaskInfo task)
        {
            Dock.MarkSeen(task.Id);
            Inspector.Show(task);
        }

        /// <summary>"Nhờ agent…" trên thẻ lỗi: dựng prompt có ngữ cảnh workspace, lệnh, thư mục tương đối và log cuối.</summary>
        private void OnAskAgentAboutTask(TaskInfo task)
        {
            var workspace = _workspaces?.Find(task.Spec.WorkspaceId);
            if (workspace == null)
            {
                Dialogs.Error(this, "Workspace của tác vụ này đã bị gỡ khỏi Toolbox.");
                return;
            }
            var tail = task.Lines().Reverse().Take(FailureHints.CopyLogLines).Reverse().ToList();
            var hint = FailureHints.Analyze(task.ExitCode, tail, task.Spec.Cwd);
            Views.AgentAsk.Send(AgentPrompts.DiagnoseFailure(workspace, task.Spec.Label, task.Spec.CommandLine, task.Spec.Cwd, task.ExitCode, tail, hint),
                Library.OpenAgentWindow);
        }

        /// <summary>Nút hành động của thẻ lỗi trong panel log.</summary>
        private void OnHintAction(FailureHint hint, TaskInfo task)
        {
            switch (hint.Action)
            {
                case HintAction.OpenTasks:
                    GoTo(Area.Tasks);
                    break;
                case HintAction.RelocateWorkspace:
                    var row = RowOf(task.Spec.WorkspaceId);
                    if (row != null)
                    {
                        Navigate(row);
                        OnRelocateWorkspace(this, new RoutedEventArgs());
                    }
                    break;
                case HintAction.RunInstall:
                    try
                    {
                        var spec = task.Spec.Clone();
                        spec.Label = "npm install";
                        spec.CommandLine = hint.Command;
                        spec.Port = null;
                        spec.Key = (task.Spec.Key ?? task.Id) + "|install";
                        var started = _tasks.Start(spec);
                        ShowLog(started);
                        Dialogs.Info("Đang chạy npm install. Xong thì chạy lại tác vụ.");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is IOException)
                    {
                        Dialogs.Error(this, "Không chạy được npm install: " + ex.Message);
                    }
                    break;
            }
        }

        // ---- Thông báo Windows ----

        private bool NotifyEnabled => !_settings.NotifyOff;

        private void RefreshNotifyText() => NotifyText.Text = NotifyEnabled ? "Thông báo: Bật" : "Thông báo: Tắt";

        private void OnNotifyClick(object sender, RoutedEventArgs e)
        {
            var off = !_settings.NotifyOff;
            _settings.NotifyOff = Settings.Update(s => s.NotifyOff = off).NotifyOff;
            RefreshNotifyText();
            Dialogs.Info(NotifyEnabled
                ? "Sẽ báo khi tác vụ chạy trên 30 giây kết thúc lúc cửa sổ không ở trước."
                : "Đã tắt thông báo tác vụ.");
        }

        /// <summary>Gọi mỗi khi tác vụ đổi trạng thái: báo các tác vụ lâu vừa kết thúc nếu cửa sổ đang không ở trước.</summary>
        private void CheckNotifications()
        {
            var windowActive = IsActive && WindowState != WindowState.Minimized;
            _notifier.Check(_tasks.Snapshot(), NotifyEnabled, windowActive);
        }

        // ---- Hỏi trước khi chạy ----

        private readonly HashSet<string> _announcedRuns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static List<Proposal> PendingRunProposals()
        {
            try { return ProposalStore.Load(ProposalStore.DefaultPath).Pending.Where(p => p.Kind == ProposalKinds.Run).ToList(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return new List<Proposal>(); }
        }

        private static int PendingRunRequests() => PendingRunProposals().Count;

        /// <summary>Lúc mở app: yêu cầu đã chờ từ trước hiện ở Trang chủ, không bật thông báo dồn.</summary>
        private void MarkRunRequestsAnnounced()
        {
            foreach (var proposal in PendingRunProposals())
                _announcedRuns.Add(proposal.Id);
        }

        /// <summary>
        /// Agent ở chế độ "Hỏi trước mỗi lần chạy" vừa xin chạy một lệnh: hiện thông báo có ba nút ngay tại trang đang mở,
        /// không bắt người dùng đi tìm trang Agent.
        /// </summary>
        private void AnnounceRunRequests()
        {
            foreach (var proposal in PendingRunProposals().Where(p => _announcedRuns.Add(p.Id)))
            {
                var captured = proposal;
                var where = RowOf(captured.WorkspaceId)?.Name;
                var print = RunApprovals.PrintOf(captured);
                if (print.Length > 90)
                    print = print.Substring(0, 87) + "…";
                Dialogs.Ask($"{captured.AgentName} xin chạy{(where == null ? "" : " ở " + where)}: {print}",
                    "Cho chạy", () => DecideRun(captured, approve: true, always: false),
                    new Views.ToastAction("Luôn cho lệnh này", () => DecideRun(captured, approve: true, always: true)),
                    new Views.ToastAction("Từ chối", () => DecideRun(captured, approve: false, always: false)));
            }
        }

        private void DecideRun(Proposal proposal, bool approve, bool always)
        {
            try
            {
                RunApprovals.Decide(ProposalStore.DefaultPath, AgentStore.DefaultPath, ActivityLog.DefaultPath, proposal, approve, always);
                Dialogs.Success(!approve ? "Đã từ chối. Agent sẽ nhận lý do ở lần gọi sau."
                    : always ? "Đã nhớ: agent chạy lệnh này luôn, không hỏi lại." : "Đã cho phép. Agent gọi lại đúng lệnh này để chạy một lần.", seconds: 6);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Dialogs.Error(this, "Không xử lý được yêu cầu chạy: " + ex.Message);
            }
            RefreshRows();
            if (_area == Area.Agents)
                AgentsPage.Refresh();
        }

        // ---- Agent hiện diện ----

        /// <summary>Mốc đã đọc tới đâu trong activity.jsonl: chỉ báo việc agent làm sau mốc này.</summary>
        private DateTime _activityMark = DateTime.MaxValue;
        private readonly System.Windows.Threading.DispatcherTimer _presenceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };

        private void StartPresence()
        {
            _activityMark = AgentPresence.Watermark(ReadActivity());
            _presenceTimer.Tick += (s, e) => RefreshPresence();
            _presenceTimer.Start();
            RefreshPresence();
        }

        private static List<ActivityEntry> ReadActivity()
        {
            try { return ActivityLog.Read(ActivityLog.DefaultPath, 200); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new List<ActivityEntry>(); }
        }

        /// <summary>Chấm "agent đang kết nối" ở rail và dưới tên workspace đang mở.</summary>
        private void RefreshPresence()
        {
            IReadOnlyList<AgentRecord> agents;
            try { agents = AgentStore.Load(AgentStore.DefaultPath).Agents; }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return; }
            var now = DateTime.UtcNow;
            var connected = AgentPresence.ConnectedWorkspaces(agents, _rows.Select(r => r.Workspace.Id), now);
            foreach (var row in _rows)
                row.SetAgentConnected(connected.Contains(row.Workspace.Id));
            if (_current != null)
            {
                var names = AgentPresence.ConnectedFor(agents, _current.Workspace.Id, now).Select(a => a.Name).Distinct().ToList();
                WsAgent.Text = names.Count == 0 ? "" : "● Agent " + string.Join(", ", names) + " đang kết nối";
                WsAgent.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// activity.jsonl đổi: việc agent vừa làm ở trang đang mở hiện thành thông báo "Agent X vừa… · Hoàn tác" và đánh dấu "mới" trên mục đó;
        /// ở workspace khác thì bật chấm trên rail cho tới khi bạn mở workspace đó.
        /// </summary>
        private void ShowAgentActivity()
        {
            var log = ReadActivity();
            var fresh = AgentPresence.Fresh(log, _activityMark);
            _activityMark = AgentPresence.Watermark(log);
            if (fresh.Count == 0)
                return;

            var here = _area == Area.Workspace && _current != null ? _current.Workspace.Id : null;
            foreach (var group in fresh.GroupBy(e => e.WorkspaceId ?? ""))
            {
                var row = RowOf(group.Key);
                if (row == null)
                    continue;
                if (string.Equals(group.Key, here, StringComparison.OrdinalIgnoreCase))
                {
                    WorkspaceContent.MarkNew(group.SelectMany(AgentPresence.TouchedIds));
                    AnnounceAgentWork(group.ToList());
                }
                else
                    row.SetNewActivity(row.NewActivity + group.Count());
            }
        }

        private void AnnounceAgentWork(List<ActivityEntry> entries)
        {
            if (entries.Count > 2)
            {
                var who = string.Join(", ", entries.Select(e => e.AgentName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                Dialogs.Info($"{(who.Length == 0 ? "Agent" : who)} vừa thay đổi {entries.Count} mục ở workspace này.");
                return;
            }
            foreach (var entry in entries)
            {
                var message = AgentPresence.Describe(entry);
                if (CanUndoEntry(entry))
                    Dialogs.Success(message, "Hoàn tác", () => UndoAgentEntry(entry), 12);
                else
                    Dialogs.Success(message, "Xem", () => GoTo(Area.Agents), 8);
            }
        }

        private bool CanUndoEntry(ActivityEntry entry)
        {
            if (entry.Undo == null || !ActivityUndo.UndoableActions.Contains(entry.Action ?? ""))
                return false;
            try
            {
                var workspace = WorkspaceStore.Peek(WorkspaceStore.DefaultPath).Find(entry.WorkspaceId);
                return ActivityUndo.CanUndo(entry, workspace, ToolStore.Peek(ToolStore.DefaultPath), out _);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void UndoAgentEntry(ActivityEntry entry)
        {
            try
            {
                var message = ActivityUndo.Undo(entry, WorkspaceStore.DefaultPath, ToolStore.DefaultPath, ActivityLog.DefaultPath);
                Dialogs.Success(message, seconds: 6);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Dialogs.Error(this, "Không hoàn tác được: " + ex.Message);
            }
        }

        // ---- Trang chủ ----

        private HomeFeed BuildHomeFeed()
        {
            bool hasAgent;
            try { hasAgent = Core.Agents.AgentStore.Load(Core.Agents.AgentStore.DefaultPath).Agents.Count > 0; }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { hasAgent = false; }
            var index = BuildSearchIndex();
            return HomeFeed.Build(_workspaces?.Workspaces ?? new List<Workspace>(), Library.Tools, _tasks.Snapshot(),
                Views.AgentsView.PendingCount(), hasAgent, index.Entries, UsageStore.Shared, DateTime.UtcNow, null, PendingRunRequests());
        }

        private void OnHomeItem(HomeItem item, bool secondary)
        {
            switch (item.Target)
            {
                case HomeTarget.Agents:
                    GoTo(Area.Agents);
                    break;
                case HomeTarget.Task:
                    ShowLog(item.Task);
                    break;
                case HomeTarget.OpenPort:
                    if (secondary || !item.Port.HasValue)
                        ShowLog(item.Task);
                    else
                        OpenInBrowser("http://localhost:" + item.Port.Value);
                    break;
                case HomeTarget.Workspace:
                    var row = RowOf(item.WorkspaceId);
                    if (row != null)
                        Navigate(row);
                    break;
                case HomeTarget.Library:
                    Navigate(null);
                    Library.FocusTool(item.ToolId);
                    break;
                case HomeTarget.Run:
                    OpenEntry(item.Entry, run: !secondary);
                    break;
            }
        }

        private void OnHomeStep(OnboardingStep step)
        {
            switch (step.Target)
            {
                case HomeTarget.AddWorkspace:
                    OnAddWorkspace(this, new RoutedEventArgs());
                    break;
                case HomeTarget.PickApp:
                    Navigate(null);
                    Library.StartPickApp();
                    break;
                case HomeTarget.ConnectAgent:
                    Library.OpenAgentWindow();
                    break;
            }
        }

        private void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Dialogs.Error(this, "Không mở được trình duyệt: " + ex.Message);
            }
        }

        // ---- Bảng lệnh Ctrl+K ----

        private void OnPaletteClick(object sender, RoutedEventArgs e) => Palette.Open();

        /// <summary>
        /// Phím tắt toàn cửa sổ (xem <see cref="KeyRoute"/> và bảng F1). Chạy ở bước Preview nên Ctrl+K hoạt động cả khi đang gõ trong một ô.
        /// Esc chỉ bị nuốt khi có gì đó để đóng, để Esc xoá ô tìm của Thư viện vẫn hoạt động.
        /// </summary>
        private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var command = KeyRoute.Map(e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key, System.Windows.Input.Keyboard.Modifiers);
            if (command == WindowCommand.None)
                return;

            var handled = true;
            switch (command)
            {
                case WindowCommand.Palette: Palette.Toggle(); break;
                case WindowCommand.GoHome: GoTo(Area.Overview); break;
                case WindowCommand.GoLibrary: Navigate(null); break;
                case WindowCommand.GoTasks: GoTo(Area.Tasks); break;
                case WindowCommand.GoAgents: GoTo(Area.Agents); break;
                case WindowCommand.NextTab:
                case WindowCommand.PrevTab:
                    if (_area == Area.Workspace)
                        WorkspaceContent.CycleTab(command == WindowCommand.NextTab ? 1 : -1);
                    break;
                case WindowCommand.Run:
                case WindowCommand.Stop:
                    handled = RunOrStopSelected(command == WindowCommand.Run);
                    break;
                case WindowCommand.Help: Shortcuts.Toggle(); break;
                case WindowCommand.Escape:
                    handled = Palette.IsOpen ? CloseOverlay(Palette.Close)
                            : Shortcuts.IsOpen ? CloseOverlay(Shortcuts.Close)
                            : Sheet.IsOpen ? Sheet.CloseCurrent()
                            : Inspector.Hide();
                    break;
            }
            if (handled)
                e.Handled = true;
        }

        private static bool CloseOverlay(Action close)
        {
            close();
            return true;
        }

        /// <summary>F5 / Shift+F5 trên mục đang chọn của trang hiện tại; không có mục nào thì nhắc cách chọn.</summary>
        private bool RunOrStopSelected(bool run)
        {
            bool done;
            if (_area == Area.Workspace)
                done = run ? WorkspaceContent.RunFocused() : WorkspaceContent.StopFocused();
            else if (_area == Area.Library)
                done = run ? Library.RunSelected() : Library.StopSelected();
            else
                return false;
            if (!done && run)
                Dialogs.Info("Chọn một dòng (Tab tới nút của dòng đó) rồi bấm F5 để chạy.");
            return true;
        }


        private SearchIndex BuildSearchIndex() =>
            SearchIndex.Build(Library.Tools, _workspaces?.Workspaces, PaletteActions());

        private static IEnumerable<SearchEntry> PaletteActions()
        {
            SearchEntry Act(string code, string title, string keywords = null) =>
                new SearchEntry { Kind = SearchKind.Action, Key = UsageStore.Key("action", code), Title = title, Subtitle = "Hành động", TargetId = code, Keywords = keywords };
            yield return Act("go-library", "Mở Thư viện", "tool danh sách");
            yield return Act("go-overview", "Mở Trang chủ", "tổng quan việc cần làm");
            yield return Act("go-tasks", "Mở Tác vụ", "đang chạy log");
            yield return Act("go-agents", "Mở Agent", "duyệt đề xuất");
            yield return Act("add-workspace", "Thêm workspace", "thư mục repo");
            yield return Act("connect-agent", "Kết nối agent…", "claude cursor mcp");
            yield return Act("theme", "Đổi giao diện sáng/tối", "dark light theme");
        }

        private void ExecuteAction(string code)
        {
            switch (code)
            {
                case "go-library": Navigate(null); break;
                case "go-overview": GoTo(Area.Overview); break;
                case "go-tasks": GoTo(Area.Tasks); break;
                case "go-agents": GoTo(Area.Agents); break;
                case "add-workspace": OnAddWorkspace(this, new RoutedEventArgs()); break;
                case "connect-agent": Library.OpenAgentWindow(); break;
                case "theme": OnThemeClick(this, new RoutedEventArgs()); break;
            }
        }

        private WorkspaceRow RowOf(string workspaceId) =>
            _rows.FirstOrDefault(r => string.Equals(r.Workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Enter: chạy mục chạy được (đi tới workspace rồi chạy đúng đường nút Chạy), mở các mục còn lại.</summary>
        private void OnPaletteActivated(SearchEntry entry) => OpenEntry(entry, run: entry.Runnable);

        /// <summary>Ctrl+Enter: chỉ mở nơi chứa, không chạy.</summary>
        private void OnPaletteReveal(SearchEntry entry) => OpenEntry(entry, run: false);

        private void OpenEntry(SearchEntry entry, bool run)
        {
            if (entry.Kind != SearchKind.Action && !string.IsNullOrEmpty(entry.Key))
                UsageStore.Shared.Record(entry.Key);
            switch (entry.Kind)
            {
                case SearchKind.Action:
                    ExecuteAction(entry.TargetId);
                    return;
                case SearchKind.Tool:
                    Navigate(null);
                    Library.FocusTool(entry.TargetId);
                    return;
                case SearchKind.Workspace:
                    break;
            }

            var row = RowOf(entry.WorkspaceId);
            if (row == null)
            {
                Dialogs.Error(this, "Không tìm thấy workspace của mục này. Có thể nó vừa bị gỡ.");
                return;
            }
            Navigate(row);
            if (entry.Kind == SearchKind.Workspace)
                return;
            if (!run)
                WorkspaceContent.Reveal(entry.Kind, entry.TargetId);
            else if (!WorkspaceContent.RunTarget(entry.Kind, entry.TargetId))
                Dialogs.Error(this, $"Không tìm thấy \"{entry.Title}\" trong workspace. Có thể nó vừa bị xoá.");
        }

        private void OnThemeClick(object sender, RoutedEventArgs e)
        {
            Theme.SetMode(Theme.Next(Theme.Mode));
            ThemeText.Text = Theme.Label(Theme.Mode);
            _settings.Theme = Settings.Update(s => s.Theme = Theme.Name(Theme.Mode)).Theme;
        }

        private void OnLibraryClick(object sender, RoutedEventArgs e) => Navigate(null);

        private void OnOverviewClick(object sender, RoutedEventArgs e) => GoTo(Area.Overview);

        private void OnTasksClick(object sender, RoutedEventArgs e) => GoTo(Area.Tasks);


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
            // Rời trang workspace (hoặc sang workspace khác) là đã xem: bỏ dấu "mới" của agent.
            if (_area == Area.Workspace && (area != Area.Workspace || !ReferenceEquals(row, _current)))
                WorkspaceContent.ClearNew();
            _area = area;
            _current = area == Area.Workspace ? row : null;
            _current?.SetNewActivity(0);
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
