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
        private TabRow _tab;
        private LibraryBinding _libraryBinding;

        // Giữ form đã điền khi chuyển qua lại giữa các trang: workspaceId → "kind|action" → dòng.
        private readonly Dictionary<string, Dictionary<string, ActionRow>> _actionCache =
            new Dictionary<string, Dictionary<string, ActionRow>>(StringComparer.OrdinalIgnoreCase);

        // Tab đang chọn của từng workspace (kind, hoặc "" là Lệnh riêng).
        private readonly Dictionary<string, string> _selectedTab = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<ActionRow> _actions = new List<ActionRow>();
        private List<ProfileRow> _profiles = new List<ProfileRow>();
        private List<CustomRow> _customs = new List<CustomRow>();
        private List<StackRow> _stacks = new List<StackRow>();

        // Lần chạy gần nhất của từng bộ chạy ("workspaceId|stackId"), giữ khi chuyển trang để còn thấy kết quả.
        private readonly Dictionary<string, StackRun> _runs = new Dictionary<string, StackRun>(StringComparer.OrdinalIgnoreCase);
        private StackRunner _runner;

        public WorkspaceView()
        {
            InitializeComponent();
            PackPage.ImportRequested += ImportPackIntoWorkspace;
        }

        private Window Host => Window.GetWindow(this);

        /// <param name="tasks">Tác vụ ngầm dùng chung.</param>
        /// <param name="save">Ghi workspace xuống đĩa; false nếu lỗi (người dùng đã được báo).</param>
        public void Initialize(TaskHost tasks, Func<bool> save)
        {
            _tasks = tasks;
            _save = save;
            _runner = new StackRunner(tasks);
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
                _tabs.Add(new TabRow(ToolCatalog.Find(binding.Kind)?.Name ?? binding.Kind, binding));
            var library = LoadLibraryStore();
            foreach (var binding in ws.LibraryBindings)
                _tabs.Add(TabRow.ForLibrary(LibraryTabTitle(library, binding), binding));
            _tabs.Add(TabRow.Stacks());
            _tabs.Add(new TabRow("Lệnh riêng", null));
            _tabs.Add(new TabRow("Đóng gói", null, PackTab.TabId));

            _selectedTab.TryGetValue(ws.Id, out var remembered);
            var pick = _tabs.FirstOrDefault(t => string.Equals(t.Id, remembered ?? "\u0001", StringComparison.OrdinalIgnoreCase))
                       ?? _tabs[0];
            TabStrip.ItemsSource = null;
            TabStrip.ItemsSource = _tabs;
            Select(pick);
        }

        private void Select(TabRow tab)
        {
            _tab = tab;
            foreach (var t in _tabs)
                t.IsSelected = ReferenceEquals(t, tab);
            _selectedTab[_workspace.Id] = tab.Id;

            var isPack = string.Equals(tab.Id, PackTab.TabId, StringComparison.Ordinal);
            ToolPage.Visibility = isPack || tab.IsRun || tab.IsCustom || tab.IsStacks || tab.IsLibrary ? Visibility.Collapsed : Visibility.Visible;
            PackPage.Visibility = isPack ? Visibility.Visible : Visibility.Collapsed;
            RunPage.Visibility = tab.IsRun ? Visibility.Visible : Visibility.Collapsed;
            LibraryPage.Visibility = tab.IsLibrary ? Visibility.Visible : Visibility.Collapsed;
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
            else if (tab.IsLibrary)
                BuildLibraryPage(tab.LibraryRef);
            else
                BuildToolPage(tab.Binding);
            Refresh();
        }

        private void BuildToolPage(ToolBinding binding)
        {
            var ws = _workspace;
            var tool = ToolCatalog.Find(binding.Kind);
            ToolRule.Text = tool == null
                ? $"Tool \"{binding.Kind}\" không còn trong danh mục."
                : $"{tool.Name} · áp dụng cho dự án có {tool.Rule}";

            var cwd = ws.ResolveCwd(binding.Cwd);
            ToolCwd.Text = Directory.Exists(cwd) ? "Chạy tại: " + cwd : "Không tìm thấy thư mục chạy: " + cwd;
            ToolCwd.Foreground = (System.Windows.Media.Brush)FindResource(Directory.Exists(cwd) ? "Muted" : "Danger");

            var actions = ToolCatalog.ActionsFor(ws, binding);
            if (!_actionCache.TryGetValue(ws.Id, out var cache))
                _actionCache[ws.Id] = cache = new Dictionary<string, ActionRow>(StringComparer.OrdinalIgnoreCase);
            _actions = new List<ActionRow>();
            foreach (var action in actions)
            {
                var key = binding.Kind + "|" + action.Id;
                // Dòng cũ chỉ dùng lại khi action cùng thư mục chạy (npm đổi script thì tạo lại).
                if (!cache.TryGetValue(key, out var row) || !ReferenceEquals(row.Binding, binding) || row.Action.Variants.Count != action.Variants.Count)
                    cache[key] = row = new ActionRow(tool, binding, action, () => _tasks.UsedPorts(), _workspace.ResolveCwd(binding.Cwd));
                _actions.Add(row);
            }
            ActionList.ItemsSource = null;
            ActionList.ItemsSource = _actions;

            NoActions.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoActions.Text = tool?.ActionsFrom != null
                ? "Không đọc được lệnh nào từ " + tool.ActionsFrom.Source.File + " tại thư mục chạy."
                : "Tool này chưa có lệnh.";

            BuildProfiles(binding.Kind);
        }

        /// <summary>Tab Chạy: mọi bộ chạy và profile của workspace (lọc theo ô tìm), dùng lại dòng của các tab khác.</summary>
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
                Select(tab);
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
            if (_workspace == null || !(sender is Button button) || button.ContextMenu == null)
                return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void OnAddProjectTool(object sender, RoutedEventArgs e)
        {
            if (_workspace == null)
                return;
            var dialog = new AddToolWindow(_workspace) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var binding = dialog.Result;
            if (!Mutate(() => _workspace.Bindings.Add(binding)))
                return;
            _selectedTab[_workspace.Id] = binding.Kind;
            Rebuild();
        }

        /// <summary>Gắn tool có sẵn trong thư viện vào workspace này. Tool vẫn nằm trong thư viện, không copy.</summary>
        private void OnAddFromLibrary(object sender, RoutedEventArgs e)
        {
            if (_workspace == null)
                return;
            var library = LoadLibraryStore();
            if (library == null)
                return;

            var candidates = library.Tools
                .Where(t => _workspace.FindLibrary(t.Id) == null)
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var dialog = new CheckListWindow(
                "Thêm tool từ thư viện",
                "Tool được tick chỉ gắn vào workspace này. Tool vẫn nằm trong thư viện và dùng được ở workspace khác.",
                "Thêm",
                candidates.Select(t => new CheckItem(t.Id, t.Name, t.ResolvedPath, isChecked: false)),
                "Mọi tool trong thư viện đã được gắn vào workspace này. Muốn thêm tool mới, đăng ký ở mục Thư viện.")
            { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var chosen = dialog.Items.Where(i => i.IsChecked).Select(i => i.Key).ToList();
            if (chosen.Count == 0)
                return;
            if (!Mutate(() =>
            {
                foreach (var id in chosen)
                {
                    var tool = library.Find(id);
                    if (tool != null)
                        _workspace.BindLibrary(tool);
                }
            }))
                return;
            _selectedTab[_workspace.Id] = TabRow.LibraryPrefix + chosen[0];
            Rebuild();
        }

        private void OnLibraryArgsLost(object sender, RoutedEventArgs e)
        {
            var binding = _libraryBinding;
            if (binding == null)
                return;
            var text = LibraryArgsBox.Text.Trim();
            var next = text.Length == 0 ? null : text;
            var old = binding.Args;
            if (string.Equals(next ?? "", old ?? "", StringComparison.Ordinal))
                return;
            if (!Mutate(() => binding.Args = next, () => binding.Args = old))
            {
                LibraryArgsBox.Text = old ?? "";
                return;
            }
            LibraryStatus.Text = "Đã lưu tham số cho workspace này.";
        }

        private void OnLibraryRun(object sender, RoutedEventArgs e)
        {
            var binding = _libraryBinding;
            var tool = binding == null ? null : FindLibraryTool(binding.ToolId);
            if (tool == null || !tool.PathExists)
            {
                LibraryStatus.Text = "Không chạy được: tool không còn trong thư viện hoặc đường dẫn không tồn tại.";
                return;
            }
            try
            {
                var pid = Runner.Start(tool, binding.EffectiveArgs(tool));
                LibraryStatus.Text = pid.HasValue ? $"Đã mở \"{tool.Name}\" (PID {pid})." : $"Đã mở \"{tool.Name}\".";
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Dialogs.Error(Host, ex.Message);
            }
        }

        private void OnLibraryUnbind(object sender, RoutedEventArgs e)
        {
            var binding = _libraryBinding;
            if (binding == null || _workspace == null)
                return;
            if (!Dialogs.Confirm(Host, $"Gỡ \"{LibraryTitle.Text}\" khỏi workspace này?\n\nTool vẫn còn trong thư viện và các workspace khác không đổi."))
                return;
            var id = binding.ToolId;
            if (Mutate(() => _workspace.UnbindLibrary(id)))
                Rebuild();
        }

        /// <summary>Trang một tool thư viện: đường dẫn, tham số riêng của workspace, nút chạy và gỡ.</summary>
        private void BuildLibraryPage(LibraryBinding binding)
        {
            _libraryBinding = binding;
            var tool = FindLibraryTool(binding.ToolId);
            LibraryTitle.Text = tool?.Name ?? binding.Name ?? binding.ToolId;
            LibraryStatus.Text = "";
            LibraryArgsBox.Text = binding.Args ?? "";
            LibraryArgsBox.IsEnabled = tool != null;

            if (tool == null)
            {
                LibraryPathText.Text = "Tool này đã bị xoá khỏi thư viện. Bấm \"Gỡ khỏi workspace\" để dọn.";
                LibraryMissingText.Visibility = Visibility.Collapsed;
                LibraryArgsHint.Text = "";
            }
            else
            {
                LibraryPathText.Text = (tool.IsCommand ? "Lệnh: " + tool.Command + "\nThư mục: " : "Đường dẫn: ") + tool.ResolvedPath;
                LibraryMissingText.Text = "Không tìm thấy đường dẫn này. Sửa đường dẫn trong thư viện rồi mở lại workspace.";
                LibraryMissingText.Visibility = tool.PathExists ? Visibility.Collapsed : Visibility.Visible;
                LibraryArgsHint.Text = "Để trống thì dùng tham số đăng ký trong thư viện"
                    + (string.IsNullOrWhiteSpace(tool.Args) ? "." : ": " + tool.Args);
            }
            LibraryRunButton.IsEnabled = tool != null && tool.PathExists;
        }

        /// <summary>
        /// Nhập gói vào workspace này: tool vào thư viện (theo lựa chọn ở hộp thoại), còn profile, bộ chạy, lệnh riêng
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
            var summary = $"Đã nhập {result.Tools.Count} tool, {result.ProfilesAdded} profile, {result.StacksAdded} bộ chạy, {result.CustomsAdded} lệnh riêng.";
            if (result.Warnings.Count > 0)
                summary += "\n\nCảnh báo:\n" + string.Join("\n", result.Warnings);
            MessageBox.Show(Host, summary, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private ToolEntry FindLibraryTool(string toolId) => LoadLibraryStore()?.Find(toolId);

        private static string LibraryTabTitle(ToolStore library, LibraryBinding binding)
        {
            var tool = library?.Find(binding.ToolId);
            return tool != null
                ? tool.Name
                : (binding.Name ?? binding.ToolId) + " (không còn trong thư viện)";
        }

        /// <summary>Mở bảng khả năng của tool (cờ đọc từ trợ giúp, cờ cổng, mục nhập tay). Đóng cửa sổ thì vẽ lại tab.</summary>
        private void OnCapabilities(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            var tool = binding == null ? null : ToolCatalog.Find(binding.Kind);
            if (binding == null || tool == null)
                return;
            var kind = binding.Kind;
            var window = new CapabilitiesWindow(tool, _workspace.ResolveCwd(binding.Cwd), () => ClearActionCache(kind)) { Owner = Host };
            window.ShowDialog();
            ClearActionCache(kind);
            Select(_tab);
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
            var dialog = new AddToolWindow(_workspace, binding) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            var old = binding.Cwd;
            if (!Mutate(() => binding.Cwd = dialog.Result.Cwd, () => binding.Cwd = old))
                return;
            // Đổi thư mục chạy có thể đổi danh sách script npm và cổng; tạo lại form của tool này.
            if (_actionCache.TryGetValue(_workspace.Id, out var cache))
                foreach (var key in cache.Keys.Where(k => k.StartsWith(binding.Kind + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    cache.Remove(key);
            Select(_tab);
        }

        private void OnRemoveTool(object sender, RoutedEventArgs e)
        {
            var binding = _tab?.Binding;
            if (binding == null)
                return;

            var tool = ToolCatalog.Find(binding.Kind);
            var name = tool?.Name ?? binding.Kind;
            var running = _tasks.Running()
                .Where(t => IsMine(t.Spec.Key) && string.Equals(TaskKey.Split(t.Spec.Key)[1], binding.Kind, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var profileCount = _workspace.Profiles.Count(p => string.Equals(p.Kind, binding.Kind, StringComparison.OrdinalIgnoreCase));
            var stackSteps = _workspace.Stacks.Sum(s => s.Steps.Count(st => string.Equals(st.Kind, binding.Kind, StringComparison.OrdinalIgnoreCase)));

            var message = $"Gỡ tool \"{name}\" khỏi workspace \"{_workspace.Name}\"?";
            if (profileCount > 0)
                message += $"\n\n{profileCount} profile của tool này cũng bị xoá.";
            if (stackSteps > 0)
                message += $"\n\n{stackSteps} bước trong bộ chạy đang dùng tool này sẽ không chạy được cho tới khi gắn lại.";
            if (running.Count > 0)
                message += $"\n\nĐang có {running.Count} tác vụ của tool này chạy ngầm; gỡ sẽ dừng chúng.";
            if (!Dialogs.Confirm(Host, message, running.Count > 0 || stackSteps > 0))
                return;

            foreach (var task in running)
                _tasks.Stop(task.Id);
            var kind = binding.Kind;
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

        // ---- Chạy / dừng ----

        private void OnRun(object sender, RoutedEventArgs e)
        {
            switch ((sender as FrameworkElement)?.DataContext)
            {
                case ActionRow action:
                    RunAction(action);
                    break;
                case ProfileRow profile:
                    RunProfile(profile);
                    break;
                case CustomRow custom:
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
            if (!CanStartIn(row.Binding.Cwd))
                return;
            if (row.Revalidate())
                return;
            Start(SpecFor(row.Tool, row.Binding, row.Action, row.Variant, row.Values(), null,
                TaskKey.ForAction(_workspace.Id, row.Tool.Kind, row.Action.Id, row.Variant.Id)));
        }

        private void RunProfile(ProfileRow row)
        {
            if (ProfileKind.IsRef(row.Profile.Kind))
            {
                // Tool thư viện / lệnh riêng: không có trong ToolCatalog, resolve qua bộ chạy.
                var resolved = StackResolver.Resolve(_workspace, new StackStep { Kind = row.Profile.Kind, ProfileId = row.Profile.Id }, 0);
                if (resolved.IsBroken)
                {
                    Dialogs.Error(Host, $"Không chạy được profile \"{row.Profile.Label}\":\n" + resolved.Problem);
                    return;
                }
                Start(resolved.Spec);
                return;
            }
            if (row.Problem != null || !CanStartIn(row.Binding.Cwd))
                return;
            var errors = CommandBuilder.Validate(row.Variant, row.Profile.Vals, _tasks.UsedPorts());
            if (errors.Count > 0)
            {
                Dialogs.Error(Host, $"Không chạy được profile \"{row.Profile.Label}\":\n" + string.Join("\n", errors.Values)
                    + "\n\nDừng tác vụ đang giữ cổng, hoặc chọn cổng khác ở thẻ lệnh rồi lưu lại profile.");
                return;
            }
            Start(SpecFor(row.Tool, row.Binding, row.Action, row.Variant, row.Profile.Vals, row.Profile.Label,
                TaskKey.ForProfile(_workspace.Id, row.Profile)));
        }

        private void RunCustom(CustomRow row)
        {
            if (!CanStartIn(row.Command.Cwd))
                return;
            Start(new TaskSpec
            {
                WorkspaceId = _workspace.Id,
                Label = row.Command.Label,
                CommandLine = row.Command.CommandLine,
                Cwd = _workspace.ResolveCwd(row.Command.Cwd),
                Window = row.Command.Window,
                Key = TaskKey.ForCustom(_workspace.Id, row.Command.Id),
            });
        }

        private TaskSpec SpecFor(ToolDef tool, ToolBinding binding, ActionDef action, VariantDef variant,
            IDictionary<string, string> vals, string profileLabel, string key)
        {
            var port = CommandBuilder.PortOf(variant, vals);
            var label = profileLabel ?? StackResolver.ActionLabel(tool, action, variant, port);
            return new TaskSpec
            {
                WorkspaceId = _workspace.Id,
                Label = label,
                CommandLine = CommandBuilder.Build(variant, vals),
                Cwd = _workspace.ResolveCwd(binding.Cwd),
                Port = port,
                Key = key,
            };
        }

        /// <summary>Workspace và thư mục chạy còn tồn tại; không thì báo lỗi rõ ràng thay vì để tiến trình chết im lặng.</summary>
        private bool CanStartIn(string cwd)
        {
            if (!_workspace.PathExists)
            {
                Dialogs.Error(Host, "Không tìm thấy thư mục workspace: " + _workspace.Path + "\nHãy trỏ lại thư mục trước khi chạy.");
                return false;
            }
            var full = _workspace.ResolveCwd(cwd);
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

        // ---- Bộ chạy ----

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

        /// <summary>Dựng bộ chạy mới bằng sơ đồ.</summary>
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

        /// <summary>Sửa bộ chạy bằng sơ đồ; cửa sổ danh sách (Sửa) vẫn là dạng xem thứ hai của cùng dữ liệu.</summary>
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
            var message = $"Xoá bộ chạy \"{row.Title}\"?";
            if (row.CanStop)
                message += "\n\nCác tác vụ đã bật vẫn đang chạy; xoá bộ chạy không dừng chúng.";
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
            if (!((sender as FrameworkElement)?.DataContext is StackRow row) || !row.CanRun || row.Run?.IsActive == true)
                return;
            var run = _runner.Start(_workspace, row.Stack);
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
            var dialog = new NewProfileWindow(_workspace, LoadLibraryStore(), () => _tasks.UsedPorts()) { Owner = Host };
            var confirmed = dialog.ShowDialog() == true;
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
                message += $"\n\n{usedBy} bộ chạy đang dùng profile này; các bước đó sẽ chạy với tham số mặc định.";
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
                message += $"\n\n{usedBy} bộ chạy đang dùng lệnh này; các bước đó sẽ không chạy được.";
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
