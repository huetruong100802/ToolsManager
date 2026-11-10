using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Thân trang workspace: tab theo tool, lệnh với biến thể và tham số, profile, lệnh riêng.</summary>
    public partial class WorkspaceView : UserControl
    {
        private TaskHost _tasks;
        private Func<bool> _save;
        private Workspace _workspace;
        private readonly List<TabRow> _tabs = new List<TabRow>();
        private List<TabRow> _topTabs = new List<TabRow>();
        private List<TabRow> _toolNav = new List<TabRow>();
        private readonly Dictionary<string, string> _selectedTool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private TabRow _tab;

        // Giữ form đã điền khi chuyển qua lại giữa các trang: workspaceId → "kind|action" → dòng.
        private readonly Dictionary<string, Dictionary<string, ActionRow>> _actionCache =
            new Dictionary<string, Dictionary<string, ActionRow>>(StringComparer.OrdinalIgnoreCase);

        // Tab đang chọn của từng workspace (kind, hoặc "" là Lệnh riêng).
        private readonly Dictionary<string, string> _selectedTab = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<ActionRow> _actions = new List<ActionRow>();
        private List<ProfileRow> _profiles = new List<ProfileRow>();
        private List<CustomRow> _customs = new List<CustomRow>();
        private List<StackRow> _stacks = new List<StackRow>();

        // Lần chạy gần nhất của từng workflow ("workspaceId|stackId"), giữ khi chuyển trang để còn thấy kết quả.
        private readonly Dictionary<string, StackRun> _runs = new Dictionary<string, StackRun>(StringComparer.OrdinalIgnoreCase);
        private StackRunner _runner;

        public WorkspaceView()
        {
            InitializeComponent();
            PackPage.ImportRequested += ImportPackIntoWorkspace;
            ActionSearch.TextChanged += (s, e) => ApplyActionSearch();
        }

        /// <summary>Lệnh khớp ô tìm: tên, id, hoặc tên biến thể (không phân biệt hoa thường và dấu).</summary>
        private bool MatchesActionSearch(ActionRow row)
        {
            var query = Core.Text.Fold((ActionSearch.Text ?? "").Trim());
            if (query.Length == 0)
                return true;
            var haystack = row.Title + " " + row.Action.Id + " " + string.Join(" ", row.Variants.Select(v => v.Label));
            return Core.Text.Fold(haystack).Contains(query);
        }

        private void ApplyActionSearch()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_actions);
            view?.Refresh();
            var shown = _actions.Count(MatchesActionSearch);
            ActionSearchEmpty.Visibility = _actions.Count > 0 && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Window Host => Window.GetWindow(this);

        /// <param name="tasks">Tác vụ ngầm dùng chung.</param>
        /// <param name="save">Ghi workspace xuống đĩa; false nếu lỗi (người dùng đã được báo).</param>
        public void Initialize(TaskHost tasks, Func<bool> save)
        {
            _tasks = tasks;
            _save = save;
            _runner = new StackRunner(tasks);
            // Thời gian đã chờ của bước đang chạy tăng theo giây: chỉ chạy khi có workflow đang chạy.
            var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) =>
            {
                if (IsVisible && _stacks.Any(r => r.Run?.IsActive == true))
                    foreach (var stack in _stacks)
                        stack.Update(RunOf(stack.Stack.Id));
            };
            clock.Start();
        }

        /// <summary>Hiện workspace này. Gọi lại cho cùng workspace thì giữ tab và form đã điền.</summary>
        public void Load(Workspace workspace)
        {
            _workspace = workspace;
            Rebuild();
        }

        /// <summary>Workspace bị gỡ: bỏ form đã nhớ.</summary>
        public void Forget(string workspaceId)
        {
            _actionCache.Remove(workspaceId);
            _selectedTab.Remove(workspaceId);
            foreach (var key in _runs.Keys.Where(k => k.StartsWith(workspaceId + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _runs[key].Stop();
                _runs.Remove(key);
            }
            if (_workspace != null && string.Equals(_workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                _workspace = null;
        }

        /// <summary>Cập nhật trạng thái đang chạy / chạy được của mọi dòng (tác vụ đổi, hoặc quay lại cửa sổ).</summary>
        public void Refresh()
        {
            if (_workspace == null || _tasks == null)
                return;
            var running = _tasks.Running().Where(t => IsMine(t.Spec.Key)).ToList();
            var canRun = _workspace.PathExists;
            foreach (var row in _actions.Cast<RunRow>().Concat(_profiles).Concat(_customs).ToList())
            {
                row.UpdateRunning(running);
                if (!(row is ProfileRow p) || p.Problem == null)
                    row.CanRun = canRun;
            }
            // Cổng trống/bận thay đổi theo tác vụ đang chạy.
            foreach (var action in _actions)
                if (!action.IsRunning)
                    action.Revalidate();
            foreach (var stack in _stacks)
                stack.Update(RunOf(stack.Stack.Id));
        }

        private bool IsMine(string key) =>
            string.Equals(TaskKey.Split(key)[0], _workspace.Id, StringComparison.OrdinalIgnoreCase);

        // ---- Dựng giao diện ----

        private void Rebuild()
        {
            var ws = _workspace;
            _tabs.Clear();
            _tabs.Add(TabRow.Run());
            foreach (var binding in ws.Bindings)
                _tabs.Add(new TabRow(ToolCatalog.Find(binding.ToolId)?.Name ?? binding.ToolId, binding));
            _tabs.Add(TabRow.Stacks());
            _tabs.Add(new TabRow("Lệnh riêng", null));
            _tabs.Add(new TabRow("Chia sẻ", null, PackTab.TabId));

            _selectedTab.TryGetValue(ws.Id, out var remembered);
            var pick = _tabs.FirstOrDefault(t => string.Equals(t.Id, remembered ?? "\u0001", StringComparison.OrdinalIgnoreCase))
                       ?? _tabs[0];
            _toolNav = _tabs.Where(t => t.Binding != null || t.IsCustom).ToList();
            _topTabs = new List<TabRow>
            {
                new TabRow("Chạy", null, TabRow.RunId),
                new TabRow("Tool", null, TabRow.ToolsId),
                new TabRow("Workflow", null, TabRow.StacksId),
                new TabRow("Chia sẻ", null, PackTab.TabId),
            };
            TabStrip.ItemsSource = null;
            TabStrip.ItemsSource = _topTabs;
            ToolNav.ItemsSource = null;
            ToolNav.ItemsSource = _toolNav;
            Select(pick);
        }

        private void Select(TabRow tab)
        {
            _tab = tab;
            foreach (var t in _tabs)
                t.IsSelected = ReferenceEquals(t, tab);
            _selectedTab[_workspace.Id] = tab.Id;

            var isPack = string.Equals(tab.Id, PackTab.TabId, StringComparison.Ordinal);
            var section = tab.IsRun ? TabRow.RunId : tab.IsStacks ? TabRow.StacksId : isPack ? PackTab.TabId : TabRow.ToolsId;
            foreach (var top in _topTabs)
                top.IsSelected = string.Equals(top.Id, section, StringComparison.Ordinal);
            ToolNavPanel.Visibility = section == TabRow.ToolsId ? Visibility.Visible : Visibility.Collapsed;
            if (section == TabRow.ToolsId)
                _selectedTool[_workspace.Id] = tab.Id;
            ToolPage.Visibility = isPack || tab.IsRun || tab.IsCustom || tab.IsStacks ? Visibility.Collapsed : Visibility.Visible;
            PackPage.Visibility = isPack ? Visibility.Visible : Visibility.Collapsed;
            RunPage.Visibility = tab.IsRun ? Visibility.Visible : Visibility.Collapsed;
            CustomPage.Visibility = tab.IsCustom ? Visibility.Visible : Visibility.Collapsed;
            StackPage.Visibility = tab.IsStacks ? Visibility.Visible : Visibility.Collapsed;
            _stacks = new List<StackRow>();
            if (isPack)
                PackPage.Load(_workspace, LoadLibraryStore);
            else if (tab.IsRun)
                BuildRun();
            else if (tab.IsStacks)
                BuildStacks();
            else if (tab.IsCustom)
                BuildCustoms();
            else
                BuildToolPage(tab.Binding);
            Refresh();
        }

        private void BuildToolPage(ToolBinding binding)
        {
            var ws = _workspace;
            var tool = ToolCatalog.Find(binding.ToolId);
            if (tool == null)
                ToolRule.Text = $"Tool \"{binding.ToolId}\" không còn trong thư viện. Bấm \"Gỡ tool\" để dọn.";
            else
            {
                var info = string.IsNullOrWhiteSpace(tool.Command) ? tool.Name : $"{tool.Name} · {tool.Command}";
                ToolRule.Text = string.IsNullOrWhiteSpace(tool.Rule) ? info : info + " · " + tool.Rule;
            }

            var cwd = ToolDir(binding);
            ToolCwd.Text = Directory.Exists(cwd) ? "Chạy tại: " + cwd : "Không tìm thấy thư mục chạy: " + cwd;
            ToolCwd.Foreground = (System.Windows.Media.Brush)FindResource(Directory.Exists(cwd) ? "Muted" : "Danger");

            var actions = ToolCatalog.ActionsFor(ws, binding);
            if (!_actionCache.TryGetValue(ws.Id, out var cache))
                _actionCache[ws.Id] = cache = new Dictionary<string, ActionRow>(StringComparer.OrdinalIgnoreCase);
            _actions = new List<ActionRow>();
            foreach (var action in actions)
            {
                var key = binding.ToolId + "|" + action.Id;
                // Dòng cũ chỉ dùng lại khi action cùng thư mục chạy (npm đổi script thì tạo lại).
                if (!cache.TryGetValue(key, out var row) || !ReferenceEquals(row.Binding, binding) || row.Action.Variants.Count != action.Variants.Count)
                    cache[key] = row = new ActionRow(tool, binding, action, () => _tasks.UsedPorts(), cwd);
                else
                    row.RefreshSuggestions();
                _actions.Add(row);
            }
            ActionSearch.Text = "";
            ActionList.ItemsSource = null;
            ActionList.ItemsSource = _actions;
            ActionSearch.Visibility = _actions.Count > 3 ? Visibility.Visible : Visibility.Collapsed;
            var actionView = System.Windows.Data.CollectionViewSource.GetDefaultView(_actions);
            actionView.Filter = o => MatchesActionSearch((ActionRow)o);
            ActionSearchEmpty.Visibility = Visibility.Collapsed;

            // Dòng chạy lệnh gốc luôn có; chỉ báo "chưa có lệnh" khi ngoài nó không còn gì.
            var onlyBase = _actions.Count <= 1;
            NoActions.Visibility = onlyBase ? Visibility.Visible : Visibility.Collapsed;
            NoActions.Text = tool?.ActionsFrom != null
                ? "Không đọc được lệnh nào từ " + tool.ActionsFrom.Source.File + " tại thư mục chạy. Bấm \"Lệnh…\" để quét hoặc nhập tay."
                : "Tool chưa có lệnh nào. Bấm \"Lệnh…\" để quét từ trợ giúp hoặc nhập tay; lệnh khai ở đó sẽ hiện thành lệnh và dùng được trong profile.";

            BuildProfiles(binding.ToolId);
        }

        /// <summary>Thư mục chạy thật của một tool đã gắn (ghi đè theo workspace, thư mục đăng ký của tool, hoặc gốc workspace).</summary>
        private string ToolDir(ToolBinding binding) =>
            CommandBuilder.ToolCwd(_workspace, binding, ToolCatalog.FindEntry(binding.ToolId));

        /// <summary>Tab Chạy: mọi workflow và profile của workspace (lọc theo ô tìm), dùng lại dòng của các tab khác.</summary>
        private void BuildRun()
        {
            _actions = new List<ActionRow>();
            _customs = new List<CustomRow>();
            var query = Core.Text.Fold(RunSearch.Text ?? "");
            _profiles = _workspace.Profiles
                .Where(p => query.Length == 0 || Core.Text.Fold(p.Label ?? "").Contains(query))
                .Select(p => new ProfileRow(_workspace, p))
                .ToList();
            _stacks = _workspace.Stacks.Select(s => new StackRow(_workspace, s)).ToList();

            RunStackList.ItemsSource = null;
            RunStackList.ItemsSource = _stacks;
            RunProfileList.ItemsSource = null;
            RunProfileList.ItemsSource = _profiles;
            NoRunStacks.Visibility = _stacks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoRunProfiles.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoRunProfiles.Text = _workspace.Profiles.Count == 0
                ? "Chưa có profile. Mở tab của một tool, chọn biến thể và tham số rồi bấm \"Lưu profile\"."
                : $"Không có profile nào khớp \"{RunSearch.Text}\". Workspace có {_workspace.Profiles.Count} profile.";
            Refresh();
        }

        private void OnRunSearch(object sender, TextChangedEventArgs e)
        {
            if (_workspace != null && _tab != null && _tab.IsRun)
                BuildRun();
        }

        private void BuildProfiles(string kind)
        {
            if (_tab != null && _tab.IsRun)
            {
                BuildRun();
                return;
            }
            _profiles = _workspace.Profiles
                .Where(p => string.Equals(p.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Select(p => new ProfileRow(_workspace, p))
                .ToList();
            ProfileList.ItemsSource = null;
            ProfileList.ItemsSource = _profiles;
            NoProfiles.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildCustoms()
        {
            _actions = new List<ActionRow>();
            _profiles = new List<ProfileRow>();
            _customs = _workspace.Customs.Select(c => new CustomRow(c)).ToList();
            CustomList.ItemsSource = null;
            CustomList.ItemsSource = _customs;
            NoCustoms.Visibility = _customs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildStacks()
        {
            if (_tab != null && _tab.IsRun)
            {
                BuildRun();
                return;
            }
            _actions = new List<ActionRow>();
            _profiles = new List<ProfileRow>();
            _customs = new List<CustomRow>();
            _stacks = _workspace.Stacks.Select(s => new StackRow(_workspace, s)).ToList();
            StackList.ItemsSource = null;
            StackList.ItemsSource = _stacks;
            NoStacks.Visibility = _stacks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Tab / tool ----

        private void OnTabClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TabRow tab)
                SelectTop(tab);
        }

        private void OnToolNavClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TabRow tab)
                Select(tab);
        }

        /// <summary>Chọn một trong bốn tab cố định. Tab Tool mở lại tool đã xem lần trước (hoặc tool đầu tiên).</summary>
        private void SelectTop(TabRow top)
        {
            if (_workspace == null)
                return;
            if (top.IsRun)
                Select(_tabs.First(t => t.IsRun));
            else if (top.IsStacks)
                Select(_tabs.First(t => t.IsStacks));
            else if (string.Equals(top.Id, PackTab.TabId, StringComparison.Ordinal))
                Select(_tabs.First(t => string.Equals(t.Id, PackTab.TabId, StringComparison.Ordinal)));
            else
            {
                _selectedTool.TryGetValue(_workspace.Id, out var remembered);
                Select(_toolNav.FirstOrDefault(t => string.Equals(t.Id, remembered ?? "0001", StringComparison.OrdinalIgnoreCase)) ?? _toolNav.First());
            }
        }

        /// <summary>Cấu hình worktree (file local, lệnh cài đặt) và chuẩn bị một worktree. Cùng dữ liệu với MCP prepare_worktree.</summary>
        private void OnWorktreeSetup(object sender, RoutedEventArgs e)
        {
            var workspace = _workspace;
            if (workspace == null)
                return;

            var dialog = new WorktreeSetupWindow(workspace, (path, command) =>
            {
                var full = Path.GetFullPath(path);
                Start(new TaskSpec
                {
                    WorkspaceId = workspace.Id,
                    Label = "Cài đặt worktree",
                    CommandLine = command,
                    Cwd = full,
                    Key = string.Join("|", workspace.Id, "worktree", "install", full, ""),
                });
            }) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            Mutate(() =>
            {
                workspace.Worktree.Files = dialog.Files;
                workspace.Worktree.Command = dialog.Command;
            });
        }

        private void OnAddTool(object sender, RoutedEventArgs e)
        {
            if (_workspace == null)
                return;
            var library = LoadLibraryStore();
            if (library == null)
                return;
            var dialog = new AddToolWindow(_workspace, library) { Owner = Host };
            if (dialog.ShowDialog() != true || dialog.Picks.Count == 0)
                return;

            // Tool chưa có trong thư viện (gợi ý từ thư mục hoặc đăng ký mới) vào thư viện trước, rồi mới gắn vào workspace.
            var bindings = new List<ToolBinding>();
            try
            {
                foreach (var pick in dialog.Picks)
                {
                    var entry = pick.IsNew ? library.Add(pick.Entry) : pick.Entry;
                    bindings.Add(new ToolBinding { ToolId = entry.Id, Cwd = pick.Cwd });
                }
                if (dialog.Picks.Any(p => p.IsNew))
                    library.Save();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không đăng ký được tool vào thư viện: " + ex.Message);
                return;
            }

            if (!Mutate(() =>
            {
                foreach (var binding in bindings.Where(b => _workspace.Binding(b.ToolId) == null))
                    _workspace.Bindings.Add(binding);
            }))
                return;
            _selectedTab[_workspace.Id] = bindings[0].ToolId;
            Rebuild();
        }

        /// <summary>Gắn tool có sẵn trong thư viện vào workspace này. Tool vẫn nằm trong thư viện, không copy.</summary>
        /// <summary>Trang một tool thư viện: đường dẫn, tham số riêng của workspace, nút chạy và gỡ.</summary>
        /// <summary>
        /// Nhận gói vào workspace này: tool vào thư viện (theo lựa chọn ở hộp thoại), còn profile, workflow, lệnh riêng
        /// vào workspace. Lưu thư viện trước, rồi workspace; lỗi thì báo, không ghi đè thầm.
        /// </summary>
        private void ImportPackIntoWorkspace()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn gói cần nhập vào workspace",
                Filter = "Gói Toolbox (*.toolbox;*.zip)|*.toolbox;*.zip",
            };
            if (dialog.ShowDialog(Host) != true)
                return;

            PackManifest manifest;
            try
            {
                manifest = Packer.ReadManifest(dialog.FileName);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không mở được gói: " + ex.Message);
                return;
            }

            var library = LoadLibraryStore();
            if (library == null)
                return;
            var choices = new ImportWindow(manifest, library) { Owner = Host };
            if (choices.ShowDialog() != true)
                return;

            WorkspaceImportResult result;
            try
            {
                result = PackageImporter.ImportIntoWorkspace(library, _workspace, dialog.FileName, choices.Destination, choices.Choices);
                library.Save();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(Host, "Không nhập được gói: " + ex.Message);
                return;
            }
            if (!_save())
                return;

            PackPage.Load(_workspace, LoadLibraryStore);
            Refresh();
            var summary = $"Đã nhập {result.Tools.Count} tool, {result.ProfilesAdded} profile, {result.StacksAdded} workflow, {result.CustomsAdded} lệnh riêng.";
            if (result.Warnings.Count > 0)
                summary += "\n\nCảnh báo:\n" + string.Join("\n", result.Warnings);
            if (result.Warnings.Count == 0)
                Dialogs.Success(summary, seconds: 8);
            else
                MessageBox.Show(Host, summary, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private ToolStore LoadLibraryStore()
        {
            try
            {
                return ToolStore.Load(ToolStore.DefaultPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không đọc được thư viện: " + ex.Message);
                return null;
            }
        }

        /// <summary>Cửa sổ chính mở một sheet bên phải (thay cho hộp thoại modal); <c>done</c> chạy khi sheet xong.</summary>
        public event Action<SheetBase, Action<bool>> SheetRequested;

        private void OpenSheet(SheetBase sheet, Action<bool> done)
        {
            if (SheetRequested == null)
                throw new InvalidOperationException("Chưa có nơi hiện sheet.");
            SheetRequested(sheet, done);
        }

        /// <summary>Mở bảng lệnh của tool (cờ đọc từ trợ giúp, cờ cổng, mục nhập tay). Đóng cửa sổ thì vẽ lại tab.</summary>
        private void OnCapabilities(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            var tool = binding == null ? null : ToolCatalog.Find(binding.ToolId);
            if (binding == null || tool == null)
                return;
            var kind = binding.ToolId;
            var sheet = new CommandsSheet(tool, _workspace.ResolveCwd(binding.Cwd), () => ClearActionCache(kind));
            OpenSheet(sheet, ok =>
            {
                ClearActionCache(kind);
                if (_workspace != null && _tab?.Binding != null && string.Equals(_tab.Binding.ToolId, kind, StringComparison.OrdinalIgnoreCase))
                    Select(_tab);
            });
        }

        private void ClearActionCache(string kind)
        {
            if (_actionCache.TryGetValue(_workspace.Id, out var cache))
                foreach (var key in cache.Keys.Where(k => k.StartsWith(kind + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    cache.Remove(key);
        }

        private void OnEditTool(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            if (binding == null)
                return;
            var dialog = new ToolBindingWindow(_workspace, binding, ToolCatalog.FindEntry(binding.ToolId)) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var oldCwd = binding.Cwd;
            var oldArgs = binding.Args;
            if (!Mutate(() => { binding.Cwd = dialog.Result.Cwd; binding.Args = dialog.Result.Args; },
                        () => { binding.Cwd = oldCwd; binding.Args = oldArgs; }))
                return;
            // Đổi thư mục chạy có thể đổi danh sách script npm và cổng; tạo lại form của tool này.
            ClearActionCache(binding.ToolId);
            Select(_tab);
        }

        /// <summary>Sửa chính tool trong thư viện (cùng form với trang Thư viện). Mọi workspace dùng tool này đều thấy thay đổi.</summary>
        private void OnEditToolEntry(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            var library = binding == null ? null : LoadLibraryStore();
            var entry = library?.Find(binding.ToolId);
            if (entry == null)
            {
                if (library != null)
                    Dialogs.Error(Host, "Tool này không còn trong thư viện.");
                return;
            }
            var copy = entry.Clone();
            var dialog = new ToolEditWindow(copy, isNew: false) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                library.Update(copy);
                library.Save();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không lưu được tool: " + ex.Message);
                return;
            }
            ClearActionCache(binding.ToolId);
            Rebuild();
        }

        private void OnRemoveTool(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            if (binding == null)
                return;

            var tool = ToolCatalog.Find(binding.ToolId);
            var name = tool?.Name ?? binding.ToolId;
            var running = _tasks.Running()
                .Where(t => IsMine(t.Spec.Key) && string.Equals(TaskKey.Split(t.Spec.Key)[1], binding.ToolId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var profileCount = _workspace.Profiles.Count(p => string.Equals(p.Kind, binding.ToolId, StringComparison.OrdinalIgnoreCase));
            var stackSteps = _workspace.Stacks.Sum(s => s.Steps.Count(st => string.Equals(st.Kind, binding.ToolId, StringComparison.OrdinalIgnoreCase)));

            var message = $"Gỡ tool \"{name}\" khỏi workspace \"{_workspace.Name}\"?";
            if (profileCount > 0)
                message += $"\n\n{profileCount} profile của tool này cũng bị xoá.";
            if (stackSteps > 0)
                message += $"\n\n{stackSteps} bước trong workflow đang dùng tool này sẽ không chạy được cho tới khi gắn lại.";
            if (running.Count > 0)
                message += $"\n\nĐang có {running.Count} tác vụ của tool này chạy ngầm; gỡ sẽ dừng chúng.";
            if (!Dialogs.Confirm(Host, message, running.Count > 0 || stackSteps > 0))
                return;

            foreach (var task in running)
                _tasks.Stop(task.Id);
            var kind = binding.ToolId;
            if (!Mutate(() =>
            {
                _workspace.Bindings.Remove(binding);
                _workspace.Profiles.RemoveAll(p => string.Equals(p.Kind, kind, StringComparison.OrdinalIgnoreCase));
            }))
                return;

            if (_actionCache.TryGetValue(_workspace.Id, out var cache))
                foreach (var key in cache.Keys.Where(k => k.StartsWith(kind + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    cache.Remove(key);
            _selectedTab.Remove(_workspace.Id);
            Rebuild();
        }

        // ---- Bàn phím ----

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: sang tab kế hoặc trước, quay vòng.</summary>
        public void CycleTab(int delta)
        {
            if (_workspace == null || _topTabs.Count == 0)
                return;
            var index = Math.Max(0, _topTabs.FindIndex(t => t.IsSelected));
            SelectTop(_topTabs[((index + delta) % _topTabs.Count + _topTabs.Count) % _topTabs.Count]);
        }

        /// <summary>F5: chạy dòng đang có con trỏ bàn phím (profile, lệnh, workflow, lệnh riêng). false nếu không có dòng nào.</summary>
        public bool RunFocused()
        {
            switch ((System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.DataContext)
            {
                case ActionRow action:
                    RunAction(action);
                    return true;
                case ProfileRow profile:
                    Used("profile", profile.Profile.Id);
                    RunProfile(profile);
                    return true;
                case CustomRow custom:
                    Used("custom", custom.Command.Id);
                    RunCustom(custom);
                    return true;
                case StackRow stack:
                    StartStack(stack);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Shift+F5: dừng dòng đang có con trỏ bàn phím nếu nó đang chạy.</summary>
        public bool StopFocused()
        {
            switch ((System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.DataContext)
            {
                case RunRow row when row.RunningTaskId != null:
                    _tasks.Stop(row.RunningTaskId);
                    return true;
                case StackRow stack when stack.Run != null:
                    stack.Run.Stop();
                    return true;
                default:
                    return false;
            }
        }

        // ---- Chạy / dừng ----

        private void OnRun(object sender, RoutedEventArgs e)
        {
            switch ((sender as FrameworkElement)?.DataContext)
            {
                case ActionRow action:
                    RunAction(action);
                    break;
                case ProfileRow profile:
                    Used("profile", profile.Profile.Id);
                    RunProfile(profile);
                    break;
                case CustomRow custom:
                    Used("custom", custom.Command.Id);
                    RunCustom(custom);
                    break;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RunRow row && row.RunningTaskId != null)
                _tasks.Stop(row.RunningTaskId);
        }

        private void RunAction(ActionRow row)
        {
            if (!CanStartTool(row.Binding))
                return;
            if (row.Revalidate())
                return;
            var pairs = row.Pairs;
            if (pairs.Count > 0)
            {
                // Cặp "hỏi lúc chạy": hỏi trước, rồi điền vào bản sao dùng cho lần chạy này.
                var answers = AskAnswers(new Profile { Label = row.Title, Pairs = pairs });
                if (answers == null)
                    return;
                pairs = PairLine.Resolve(pairs, answers);
            }
            Start(SpecFor(row.Tool, row.Binding, row.Action, row.Variant, row.Values(), null,
                TaskKey.ForAction(_workspace.Id, row.Tool.Kind, row.Action.Id, row.Variant.Id), pairs));
        }

        private void RunProfile(ProfileRow row)
        {
            var answers = AskAnswers(row.Profile);
            if (answers == null)
                return;
            if (ProfileKind.IsCustom(row.Profile.Kind))
            {
                // Lệnh riêng: không thuộc tool nào, resolve qua workflow.
                var resolved = ResolveProfile(row.Profile, answers);
                if (resolved.IsBroken)
                {
                    Dialogs.Error(Host, $"Không chạy được profile \"{row.Profile.Label}\":\n" + resolved.Problem);
                    return;
                }
                Start(resolved.Spec);
                return;
            }
            if (row.Problem != null || !CanStartTool(row.Binding))
                return;
            var vals = row.Profile.Vals;
            if (row.Profile.HasPairs)
            {
                // Cặp thay cho ô {args}; các ô còn lại (cổng...) vẫn kiểm trùng cổng như thường.
                vals = new Dictionary<string, string>(vals ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase) { [ProfileKind.ArgsKey] = "" };
            }
            var errors = CommandBuilder.Validate(row.Variant, vals, _tasks.UsedPorts());
            if (errors.Count > 0)
            {
                Dialogs.Error(Host, $"Không chạy được profile \"{row.Profile.Label}\":\n" + string.Join("\n", errors.Values)
                    + "\n\nDừng tác vụ đang giữ cổng, hoặc chọn cổng khác ở thẻ lệnh rồi lưu lại profile.");
                return;
            }
            if (row.Profile.HasPairs)
            {
                var resolved = ResolveProfile(row.Profile, answers);
                if (resolved.IsBroken)
                {
                    Dialogs.Error(Host, $"Không chạy được profile \"{row.Profile.Label}\":\n" + resolved.Problem);
                    return;
                }
                Start(resolved.Spec);
                return;
            }
            Start(SpecFor(row.Tool, row.Binding, row.Action, row.Variant, row.Profile.Vals, row.Profile.Label,
                TaskKey.ForProfile(_workspace.Id, row.Profile)));
        }

        private ResolvedStep ResolveProfile(Profile profile, IList<string> answers) =>
            StackResolver.Resolve(_workspace, new StackStep
            {
                Kind = profile.Kind,
                ActionId = profile.ActionId,
                VariantId = profile.VariantId,
                ProfileId = profile.Id,
                Answers = answers,
            }, 0);

        /// <summary>
        /// Hỏi giá trị cho các cặp "hỏi lúc chạy" của profile, lần lượt từng ô. Trả danh sách (rỗng nếu không có gì để hỏi);
        /// null nếu người dùng huỷ.
        /// </summary>
        private List<string> AskAnswers(Profile profile)
        {
            var answers = new List<string>();
            foreach (var pair in profile.PendingAsks)
            {
                var name = string.IsNullOrWhiteSpace(pair.Key) ? "đối số" : pair.Key.Trim();
                var prompt = new PromptWindow("Nhập giá trị", $"Giá trị cho {name} *", "",
                    $"Profile \"{profile.Label}\" hỏi giá trị này mỗi lần chạy.") { Owner = Host };
                if (prompt.ShowDialog() != true)
                    return null;
                answers.Add(prompt.Value);
            }
            return answers;
        }

        private void RunCustom(CustomRow row) => RunCustomCommand(row.Command);

        private void RunCustomCommand(CustomCommand command)
        {
            if (!CanStartIn(command.Cwd))
                return;
            Start(new TaskSpec
            {
                WorkspaceId = _workspace.Id,
                Label = command.Label,
                CommandLine = command.CommandLine,
                Cwd = _workspace.ResolveCwd(command.Cwd),
                Window = command.Window,
                Key = TaskKey.ForCustom(_workspace.Id, command.Id),
            });
        }

        private void Used(string kind, string id)
        {
            if (_workspace != null)
                UsageStore.Shared.Record(UsageStore.Key(kind, id, _workspace.Id));
        }

        /// <summary>Bảng lệnh Ctrl+Enter: đưa người dùng tới mục (không chạy): profile và workflow ở tab Chạy, lệnh riêng ở tab Lệnh riêng.</summary>
        public void Reveal(SearchKind kind, string id)
        {
            if (_workspace == null)
                return;
            if (kind == SearchKind.Custom)
            {
                Select(_tabs.FirstOrDefault(t => t.IsCustom) ?? _tabs[0]);
                return;
            }
            var label = kind == SearchKind.Profile ? _workspace.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))?.Label : "";
            RunSearch.Text = label ?? "";
            Select(_tabs[0]);
        }

        /// <summary>
        /// Chạy một mục từ bảng lệnh Ctrl+K: mở tab Chạy rồi chạy profile, workflow hoặc lệnh riêng theo id,
        /// dùng đúng đường chạy của nút Chạy (hỏi giá trị lúc chạy, kiểm cổng, kiểm thư mục).
        /// </summary>
        public bool RunTarget(SearchKind kind, string id)
        {
            if (_workspace == null || _tasks == null)
                return false;
            RunSearch.Text = "";
            Select(_tabs[0]);
            switch (kind)
            {
                case SearchKind.Profile:
                    var profile = _profiles.FirstOrDefault(p => string.Equals(p.Profile.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (profile == null)
                        return false;
                    Used("profile", id);
                    RunProfile(profile);
                    return true;
                case SearchKind.Workflow:
                    var stack = _stacks.FirstOrDefault(r => string.Equals(r.Stack.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (stack == null)
                        return false;
                    StartStack(stack);
                    return true;
                case SearchKind.Custom:
                    var custom = _workspace.Customs.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (custom == null)
                        return false;
                    Used("custom", id);
                    RunCustomCommand(custom);
                    return true;
                default:
                    return false;
            }
        }

        private TaskSpec SpecFor(ToolDef tool, ToolBinding binding, ActionDef action, VariantDef variant,
            IDictionary<string, string> vals, string profileLabel, string key, IList<ParamPair> pairs = null)
        {
            var hasPairs = pairs != null && pairs.Count > 0;
            var port = CommandBuilder.PortOf(variant, vals);
            var label = profileLabel ?? StackResolver.ActionLabel(tool, action, variant, port);
            // Lệnh gốc (lệnh rỗng) dựng qua đường chung của mọi tool để tool file chạy cũng đúng cách chạy theo đuôi file.
            var commandLine = action.Id.Length == 0
                ? CommandBuilder.ToolCommandLine(ToolCatalog.FindEntry(binding.ToolId), binding, hasPairs ? PairLine.Render(pairs) : Values(vals, "args"))
                : hasPairs ? CommandBuilder.BuildWithPairs(variant, vals, pairs) : CommandBuilder.Build(variant, vals);
            return new TaskSpec
            {
                WorkspaceId = _workspace.Id,
                Label = label,
                CommandLine = commandLine,
                Cwd = ToolDir(binding),
                Port = port,
                Key = key,
            };
        }

        private static string Values(IDictionary<string, string> vals, string key) =>
            vals != null && vals.TryGetValue(key, out var value) ? value : null;

        /// <summary>Workspace và thư mục chạy còn tồn tại; không thì báo lỗi rõ ràng thay vì để tiến trình chết im lặng.</summary>
        private bool CanStartIn(string cwd) =>
            WorkspaceExists() && CanStartInFull(_workspace.ResolveCwd(cwd));

        /// <summary>Như <see cref="CanStartIn"/> nhưng cho tool đã gắn: thư mục chạy theo <see cref="ToolDir"/>.</summary>
        private bool CanStartTool(ToolBinding binding) =>
            WorkspaceExists() && CanStartInFull(ToolDir(binding));

        private bool WorkspaceExists()
        {
            if (_workspace.PathExists)
                return true;
            Dialogs.Error(Host, "Không tìm thấy thư mục workspace: " + _workspace.Path + "\nHãy trỏ lại thư mục trước khi chạy.");
            return false;
        }

        /// <summary>Thư mục chạy đã là đường dẫn đầy đủ còn tồn tại.</summary>
        private bool CanStartInFull(string full)
        {
            if (!Directory.Exists(full))
            {
                Dialogs.Error(Host, "Không tìm thấy thư mục chạy: " + full);
                return false;
            }
            return true;
        }

        private void Start(TaskSpec spec)
        {
            try
            {
                _tasks.Start(spec);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                Dialogs.Error(Host, "Không chạy được lệnh: " + ex.Message);
            }
        }

        // ---- Workflow ----

        private string RunKey(string stackId) => _workspace.Id + "|" + stackId;

        private StackRun RunOf(string stackId) => _runs.TryGetValue(RunKey(stackId), out var run) ? run : null;

        private void OnAddStack(object sender, RoutedEventArgs e)
        {
            var dialog = new StackEditWindow(_workspace) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            var stack = dialog.Result;
            stack.Id = WorkspaceStore.NewId();
            if (!Mutate(() => _workspace.Stacks.Add(stack)))
                return;
            BuildStacks();
            Refresh();
        }

        /// <summary>Dựng workflow mới bằng sơ đồ.</summary>
        private void OnDiagramNew(object sender, RoutedEventArgs e)
        {
            var stack = StackDiagram.ShowDialog(Host, _workspace, null);
            if (stack == null)
                return;
            stack.Id = WorkspaceStore.NewId();
            if (!Mutate(() => _workspace.Stacks.Add(stack)))
                return;
            BuildStacks();
            Refresh();
        }

        /// <summary>Sửa workflow bằng sơ đồ; cửa sổ danh sách (Sửa) vẫn là dạng xem thứ hai của cùng dữ liệu.</summary>
        private void OnDiagramStack(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StackRow row))
                return;
            var index = _workspace.Stacks.IndexOf(row.Stack);
            if (index < 0)
                return;
            var edited = StackDiagram.ShowDialog(Host, _workspace, row.Stack);
            if (edited == null)
                return;
            if (!Mutate(() => _workspace.Stacks[index] = edited))
                return;
            _runs.Remove(RunKey(edited.Id));
            BuildStacks();
            Refresh();
        }

        private void OnEditStack(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StackRow row))
                return;
            var index = _workspace.Stacks.IndexOf(row.Stack);
            if (index < 0)
                return;
            var dialog = new StackEditWindow(_workspace, row.Stack) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            var edited = dialog.Result;
            if (!Mutate(() => _workspace.Stacks[index] = edited))
                return;
            // Kết quả cũ thuộc về các bước cũ.
            _runs.Remove(RunKey(edited.Id));
            BuildStacks();
            Refresh();
        }

        private void OnDeleteStack(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StackRow row))
                return;
            var message = $"Xoá workflow \"{row.Title}\"?";
            if (row.CanStop)
                message += "\n\nCác tác vụ đã bật vẫn đang chạy; xoá workflow không dừng chúng.";
            if (!Dialogs.Confirm(Host, message))
                return;
            if (!Mutate(() => _workspace.Stacks.Remove(row.Stack)))
                return;
            _runs.Remove(RunKey(row.Stack.Id));
            BuildStacks();
            Refresh();
        }

        private void OnRunStack(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StackRow row)
                StartStack(row);
        }

        /// <summary>Yêu cầu mở log một tác vụ (panel log của cửa sổ chính).</summary>
        public event Action<TaskInfo> ShowLogRequested;

        private void OnRetryStack(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StackRow row && row.HasFailedStep)
                StartStack(row, row.FailedIndex);
        }

        private void OnFailedLog(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StackRow row) || row.FailedTaskId == null)
                return;
            var task = _tasks.Find(row.FailedTaskId);
            if (task != null)
                ShowLogRequested?.Invoke(task);
        }

        private void StartStack(StackRow row, int fromIndex = 0)
        {
            if (!row.CanRun && fromIndex == 0 || row.Run?.IsActive == true)
                return;
            Used("workflow", row.Stack.Id);
            var run = _runner.Start(_workspace, row.Stack, fromIndex);
            _runs[RunKey(row.Stack.Id)] = run;
            // Tiến độ đến từ luồng nền: chuyển về luồng giao diện.
            run.Changed += () => Dispatcher.BeginInvoke(new Action(Refresh));
            Refresh();
        }

        private void OnStopStack(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StackRow row)
                row.Run?.Stop();
        }

        // ---- Profile ----

        private void OnSaveProfile(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ActionRow row))
                return;

            // Lưu thì không cần cổng đang trống; chỉ cần giá trị hợp lệ.
            var errors = CommandBuilder.Validate(row.Variant, row.Values());
            if (errors.Count > 0)
            {
                row.Revalidate();
                Dialogs.Error(Host, "Chưa lưu được profile:\n" + string.Join("\n", errors.Values));
                return;
            }

            if (row.Editor.Error != null)
            {
                Dialogs.Error(Host, "Chưa lưu được profile:\n" + row.Editor.Error);
                return;
            }
            var vals = new Dictionary<string, string>(CommandBuilder.Effective(row.Variant, row.Values()));
            var suggested = $"{row.Tool.Name} · {row.Action.Label}";
            if (row.Action.Variants.Count > 1)
                suggested += " · " + row.Variant.Label;
            var port = CommandBuilder.PortOf(row.Variant, vals);
            if (port.HasValue)
                suggested += " · :" + port.Value;

            var prompt = new PromptWindow("Lưu profile", "Tên profile *", suggested,
                "Profile nhớ biến thể và tham số hiện tại để chạy lại bằng một cú bấm.") { Owner = Host };
            if (prompt.ShowDialog() != true)
                return;

            var label = prompt.Value;
            var same = _workspace.Profiles.FirstOrDefault(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));
            if (same != null && !Dialogs.Confirm(Host, $"Đã có profile \"{label}\". Ghi đè bằng cấu hình này?", false))
                return;

            var profile = new Profile
            {
                Id = same?.Id ?? WorkspaceStore.NewId(),
                Label = label,
                Kind = row.Tool.Kind,
                ActionId = row.Action.Id,
                VariantId = row.Variant.Id,
                Vals = vals,
                Pairs = row.Pairs.Count > 0 ? row.Pairs : null,
            };
            if (!Mutate(() =>
            {
                if (same != null)
                    _workspace.Profiles.Remove(same);
                _workspace.Profiles.Add(profile);
            }))
                return;
            BuildProfiles(row.Tool.Kind);
            Refresh();
        }

        /// <summary>Hộp thoại ba bước: tạo profile cho tool dựng sẵn, tool thư viện hoặc lệnh riêng.</summary>
        private void OnNewProfile(object sender, RoutedEventArgs e)
        {
            var dialog = new ProfileSheet(_workspace, () => _tasks.UsedPorts());
            OpenSheet(dialog, confirmed => FinishNewProfile(dialog, confirmed));
        }

        private void FinishNewProfile(ProfileSheet dialog, bool confirmed)
        {
            if (_workspace == null)
                return;
            if (dialog.RequestCustomTool)
            {
                OnAddCommand(this, null);
                return;
            }
            if (!confirmed || dialog.Result == null)
                return;

            var profile = dialog.Result;
            var same = _workspace.Profiles.FirstOrDefault(p => string.Equals(p.Label, profile.Label, StringComparison.OrdinalIgnoreCase));
            if (same != null && !Dialogs.Confirm(Host, $"Đã có profile \"{profile.Label}\". Ghi đè bằng cấu hình này?", false))
                return;

            string problem = null;
            var saved = Mutate(() =>
            {
                if (same != null)
                    profile.Id = same.Id;
                var index = same == null ? -1 : _workspace.Profiles.IndexOf(same);
                if (same != null)
                    _workspace.Profiles.Remove(same);
                problem = WorkspaceEditor.CreateProfile(_workspace, profile);
                // Ghi đè mà tạo lỗi thì trả bản cũ về, tránh mất profile.
                if (problem != null && same != null)
                    _workspace.Profiles.Insert(Math.Max(0, index), same);
            });
            if (problem != null)
            {
                Dialogs.Error(Host, "Chưa lưu được profile:\n" + problem);
                return;
            }
            if (!saved)
                return;

            Refresh();
            if (dialog.RunAfter)
                RunProfile(new ProfileRow(_workspace, profile));
        }

        /// <summary>Sửa cặp tham số của profile đã lưu. Profile cũ chỉ có chuỗi args thì được tách thành cặp trước khi sửa.</summary>
        private void OnEditPairs(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ProfileRow row))
                return;
            var profile = row.Profile;
            var custom = ProfileKind.IsCustom(profile.Kind);
            var suggestions = custom ? new CapabilityEntry[0] : PairEditor.SuggestionsFor(profile.Kind, profile.ActionId ?? "");
            var initial = profile.HasPairs
                ? profile.Pairs
                : PairLine.Parse(profile.ExtraArgs, key =>
                {
                    var known = suggestions.FirstOrDefault(s => s.Name == key);
                    return known == null || known.TakesValue; // cờ ngoài bảng quét: token kế tiếp là giá trị
                });

            var dialog = new PairsSheet(profile.Label, initial, suggestions,
                profile.HasPairs ? null : "Tham số cũ được tách thành các cặp; lưu để chuyển hẳn sang dạng cặp.");
            OpenSheet(dialog, ok => { if (ok) FinishEditPairs(profile, dialog.Result); });
        }

        private void FinishEditPairs(Profile profile, List<ParamPair> result)
        {
            if (_workspace == null)
                return;
            var oldPairs = profile.Pairs;
            var oldVals = new Dictionary<string, string>(profile.Vals ?? new Dictionary<string, string>());
            var newPairs = result.Count > 0 ? result : null;
            var saved = Mutate(() =>
            {
                profile.Pairs = newPairs;
                profile.Vals = new Dictionary<string, string>(oldVals);
                profile.Vals.Remove(ProfileKind.ArgsKey);   // cặp là nguồn duy nhất, tránh nối hai lần
            }, () =>
            {
                profile.Pairs = oldPairs;
                profile.Vals = oldVals;
            });
            if (!saved)
                return;
            BuildProfiles(profile.Kind);
            Refresh();
        }

        private void OnRenameProfile(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ProfileRow row))
                return;
            var prompt = new PromptWindow("Đổi tên profile", "Tên profile *", row.Profile.Label) { Owner = Host };
            if (prompt.ShowDialog() != true)
                return;

            var old = row.Profile.Label;
            if (!Mutate(() => row.Profile.Label = prompt.Value, () => row.Profile.Label = old))
                return;
            BuildProfiles(row.Profile.Kind);
            Refresh();
        }

        /// <summary>Mở danh sách gợi ý của một ô tham số (nút ▾).</summary>
        private void OnPickOptions(object sender, RoutedEventArgs e) => ParamOptionMenu.Open((Button)sender);

        /// <summary>Nhân bản profile: bản sao có Id mới, cùng lệnh và tham số; đổi tên sau.</summary>
        private void OnDuplicateProfile(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ProfileRow row))
                return;
            var source = row.Profile;
            var copy = new Profile
            {
                Id = WorkspaceStore.NewId(),
                Label = source.Label + " (bản sao)",
                Kind = source.Kind,
                ActionId = source.ActionId,
                VariantId = source.VariantId,
                RefId = source.RefId,
                Vals = new Dictionary<string, string>(source.Vals ?? new Dictionary<string, string>()),
                Pairs = source.HasPairs ? source.Pairs.Select(x => x.Clone()).ToList() : null,
            };
            if (!Mutate(() => _workspace.Profiles.Add(copy), () => _workspace.Profiles.Remove(copy)))
                return;
            BuildProfiles(copy.Kind);
            Refresh();
        }

        private void OnDeleteProfile(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ProfileRow row))
                return;
            var usedBy = _workspace.Stacks.Count(s => s.Steps.Any(st => string.Equals(st.ProfileId, row.Profile.Id, StringComparison.OrdinalIgnoreCase)));
            var message = $"Xoá profile \"{row.Profile.Label}\"?";
            if (usedBy > 0)
                message += $"\n\n{usedBy} workflow đang dùng profile này; các bước đó sẽ chạy với tham số mặc định.";
            if (!Dialogs.Confirm(Host, message, usedBy > 0))
                return;

            if (row.RunningTaskId != null)
                _tasks.Stop(row.RunningTaskId);
            if (!Mutate(() =>
            {
                _workspace.Profiles.Remove(row.Profile);
                foreach (var step in _workspace.Stacks.SelectMany(s => s.Steps).Where(st => string.Equals(st.ProfileId, row.Profile.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    step.ProfileId = null;
                    // Profile library/custom: giữ tham chiếu để bước vẫn trỏ đúng tool/lệnh riêng.
                    if (ProfileKind.IsRef(row.Profile.Kind))
                        step.ActionId = row.Profile.RefId;
                }
            }))
                return;
            BuildProfiles(row.Profile.Kind);
            Refresh();
        }

        // ---- Lệnh riêng ----

        private void OnAddCommand(object sender, RoutedEventArgs e)
        {
            var dialog = new CommandEditWindow(_workspace) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var command = dialog.Result;
            command.Id = WorkspaceStore.NewId();
            if (!Mutate(() => _workspace.Customs.Add(command)))
                return;
            BuildCustoms();
            Refresh();
        }

        private void OnEditCommand(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CustomRow row))
                return;
            var dialog = new CommandEditWindow(_workspace, row.Command) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var old = row.Command;
            var index = _workspace.Customs.IndexOf(old);
            if (index < 0)
                return;
            if (!Mutate(() => _workspace.Customs[index] = dialog.Result, () => _workspace.Customs[index] = old))
                return;
            // Lệnh đang chạy giữ nguyên bản cũ tới khi chạy lại; chỉ báo để không gây hiểu nhầm.
            BuildCustoms();
            Refresh();
        }

        private void OnDeleteCommand(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CustomRow row))
                return;
            var usedBy = _workspace.Stacks.Count(s => s.Steps.Any(st =>
                string.Equals(st.Kind, TaskKey.Custom, StringComparison.OrdinalIgnoreCase)
                && string.Equals(st.ActionId, row.Command.Id, StringComparison.OrdinalIgnoreCase)));
            var message = $"Xoá lệnh \"{row.Command.Label}\"?";
            if (usedBy > 0)
                message += $"\n\n{usedBy} workflow đang dùng lệnh này; các bước đó sẽ không chạy được.";
            if (row.IsRunning)
                message += "\n\nLệnh đang chạy ngầm; xoá sẽ dừng nó.";
            if (!Dialogs.Confirm(Host, message, row.IsRunning || usedBy > 0))
                return;

            if (row.RunningTaskId != null)
                _tasks.Stop(row.RunningTaskId);
            if (!Mutate(() => _workspace.Customs.Remove(row.Command)))
                return;
            BuildCustoms();
            Refresh();
        }

        // ---- Lưu ----

        /// <summary>
        /// Áp thay đổi rồi lưu. Lưu lỗi thì khôi phục danh sách như cũ (và <paramref name="revert"/> nếu có, cho thay đổi
        /// thuộc tính của một mục) để bộ nhớ không lệch với đĩa.
        /// </summary>
        private bool Mutate(Action change, Action revert = null)
        {
            var bindings = _workspace.Bindings.ToList();
            var customs = _workspace.Customs.ToList();
            var profiles = _workspace.Profiles.ToList();
            var stacks = _workspace.Stacks.ToList();
            var libraries = _workspace.LibraryBindings.ToList();
            var stackProfiles = _workspace.Stacks.SelectMany(s => s.Steps).Select(st => Tuple.Create(st, st.ProfileId)).ToList();

            change();
            if (_save())
                return true;

            _workspace.Bindings = bindings;
            _workspace.Customs = customs;
            _workspace.Profiles = profiles;
            _workspace.Stacks = stacks;
            _workspace.LibraryBindings = libraries;
            foreach (var pair in stackProfiles)
                pair.Item1.ProfileId = pair.Item2;
            revert?.Invoke();
            return false;
        }
    }
}
