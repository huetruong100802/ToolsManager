using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Toolbox.Core.Workspaces;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox.Views
{
    public partial class LibraryView : UserControl
    {
        private readonly ObservableCollection<ToolItem> _items = new ObservableCollection<ToolItem>();
        private readonly ObservableCollection<TagOption> _tagOptions = new ObservableCollection<TagOption>();
        private readonly ObservableCollection<TagOption> _activeTags = new ObservableCollection<TagOption>();
        private readonly HashSet<string> _selectedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ICollectionView _view;
        private bool _hooked;
        private readonly Installer _installer = new Installer();
        private readonly RunTracker _runs = new RunTracker();
        private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private ToolStore _store;
        private HashSet<ToolEntry> _matches;

        public LibraryView()
        {
            InitializeComponent();
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = o => _matches == null || _matches.Contains(((ToolItem)o).Tool);
            ToolList.ItemsSource = _view;
            TagChips.ItemsSource = _tagOptions;
            ActiveTags.ItemsSource = _activeTags;
            Loaded += OnLoaded;
            EmptyHint.AddAction("App đã cài…", () => OnPickApp(null, null));
            EmptyHint.AddAction("Thêm tool", () => OnAdd(null, null));
            // Exited/Changed đến từ luồng khác: chuyển về luồng giao diện rồi mới đụng vào item.
            _runs.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshRuns));
            _clock.Tick += (s, e) =>
            {
                foreach (var item in _items)
                    item.Tick();
            };
            _clock.Start();
        }

        /// <summary>File .toolbox truyền qua dòng lệnh; nhập ngay khi trang tải xong.</summary>
        public string FileToOpen { get; set; }

        /// <summary>Tool trong thư viện (cho chỉ mục tìm toàn app). Rỗng nếu chưa nạp được.</summary>
        public IReadOnlyList<ToolEntry> Tools => _store?.Tools ?? (IReadOnlyList<ToolEntry>)new List<ToolEntry>();

        /// <summary>Bảng lệnh mở một tool: lọc danh sách về đúng tool đó và đưa con trỏ tới đó.</summary>
        public void FocusTool(string toolId)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.Tool.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;
            SearchBox.Text = item.Tool.Name;
            ToolList.SelectedItem = item;
            ToolList.ScrollIntoView(item);
        }

        /// <summary>Cửa sổ chủ, dùng làm owner cho hộp thoại.</summary>
        private Window Host => Window.GetWindow(this);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_hooked)
            {
                _hooked = true;
                var host = Host;
                // Quay lại cửa sổ (vd sau khi cài app, xoá/di chuyển file) → cập nhật trạng thái "không tìm thấy".
                host.Activated += (s, a) => RefreshStatus();
                host.Closed += (s, a) => _clock.Stop();
            }

            try
            {
                _store = ToolStore.Load(ToolStore.DefaultPath);
            }
            catch (InvalidDataException ex)
            {
                // Không ghi đè file hỏng; để người dùng tự sửa hoặc xoá.
                Dialogs.Fatal(Host, ex.Message + "\n\nHãy sửa hoặc xoá file này rồi mở lại Toolbox.");
                Host.Close();
                return;
            }

            Reload();
            InstallBanner.Visibility = _installer.IsInstalledCopy(CurrentExe) ? Visibility.Collapsed : Visibility.Visible;
            if (FileToOpen != null)
                ImportPackage(FileToOpen);
        }

        // ---------- Cài đặt ----------

        private static string CurrentExe => System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

        private void OnInstall(object sender, RoutedEventArgs e)
        {
            string installed;
            try
            {
                installed = _installer.Install(CurrentExe);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException)
            {
                Dialogs.Error(Host, "Không cài được: " + ex.Message + "\n\nNếu Toolbox đã cài đang mở, hãy đóng nó rồi thử lại.");
                return;
            }

            InstallBanner.Visibility = Visibility.Collapsed;
            if (Dialogs.Confirm(Host, $"Đã cài vào:\n{installed}\n\nMở Toolbox từ Start Menu hoặc double-click file .toolbox.\nChuyển sang bản đã cài ngay bây giờ?"))
            {
                System.Diagnostics.Process.Start(installed)?.Dispose();
                Host.Close();
            }
        }

        // ---------- Danh sách ----------

        private void Reload()
        {
            // Giữ các tool đang được tick qua lần nạp lại (vd. agent vừa đổi tools.json khi người dùng đang chọn).
            var checkedIds = new HashSet<string>(_items.Where(i => i.IsChecked).Select(i => i.Tool.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var item in _items)
                item.PropertyChanged -= OnItemChanged;
            _items.Clear();
            foreach (var tool in _store.Tools.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                AddItem(tool);
                if (checkedIds.Contains(tool.Id))
                    _items[_items.Count - 1].IsChecked = true;
            }

            BuildTagChips();
            ApplyFilter();
        }

        /// <summary>Nạp thay đổi của agent (tools.json đổi ở ngoài) vào danh sách; giữ ô tìm, tag và các tool đang tick.</summary>
        public bool SyncFromDisk()
        {
            if (_store == null)
                return false;
            try
            {
                if (!_store.Sync())
                    return false;
                Reload();
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // File đang bị ghi dở hoặc hỏng: giữ danh sách hiện có, lần đổi kế tiếp sẽ nạp lại.
            }
            return false;
        }

        /// <summary>Dựng lại chip tag từ kho, kèm số tool mỗi tag; giữ các tag đang chọn nếu vẫn còn tồn tại.</summary>
        private void BuildTagChips()
        {
            foreach (var option in _tagOptions)
                option.PropertyChanged -= OnTagChanged;
            _tagOptions.Clear();

            var counts = _store.Tools
                .SelectMany(t => t.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var name in _store.AllTags())
            {
                var option = new TagOption(name, counts[name]) { IsSelected = _selectedTags.Contains(name) };
                option.PropertyChanged += OnTagChanged;
                _tagOptions.Add(option);
            }

            _selectedTags.IntersectWith(_tagOptions.Where(o => o.IsSelected).Select(o => o.Name));
            TagToggle.Visibility = _tagOptions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_tagOptions.Count == 0)
                TagToggle.IsChecked = false;
        }

        private void OnTagPanelToggled(object sender, RoutedEventArgs e) =>
            TagPanel.Visibility = TagToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        private void OnActiveTagRemoved(object sender, RoutedEventArgs e) =>
            ((TagOption)((FrameworkElement)sender).DataContext).IsSelected = false;

        /// <summary>Cập nhật phần tóm tắt tag đang lọc: badge trên nút Tag, dãy pill và nút Bỏ hết.</summary>
        private void UpdateTagSummary()
        {
            var selected = _tagOptions.Where(o => o.IsSelected).ToList();
            _activeTags.Clear();
            foreach (var option in selected)
                _activeTags.Add(option);

            TagBadgeText.Text = selected.Count.ToString();
            TagBadge.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ActiveTagsRow.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnTagChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TagOption.IsSelected))
                return;
            var option = (TagOption)sender;
            if (option.IsSelected)
                _selectedTags.Add(option.Name);
            else
                _selectedTags.Remove(option.Name);
            ApplyFilter();
        }

        private void OnClearTags(object sender, RoutedEventArgs e)
        {
            _selectedTags.Clear();
            foreach (var option in _tagOptions)
                option.IsSelected = false;
            ApplyFilter();
        }

        private void OnClearFilters(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            OnClearTags(sender, e);
            SearchBox.Focus();
        }

        /// <summary>
        /// Phím tắt: Ctrl+F nhảy vào ô tìm (Ctrl+K là bảng lệnh toàn app, xử lý ở cửa sổ chính); Esc trong ô tìm xoá chữ; Enter trên dòng đang chọn thì chạy
        /// (tương đương double-click, để dùng được hoàn toàn bằng bàn phím).
        /// </summary>
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
            {
                SearchBox.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.OriginalSource is System.Windows.Controls.ListBoxItem
                     && ToolList.SelectedItem is ToolItem item && !item.CanInstall)
            {
                Run(item);
                e.Handled = true;
            }
        }

        private void AddItem(ToolEntry tool)
        {
            var item = new ToolItem(tool);
            item.ApplyRun(_runs.Current(tool.Id), _runs.Last(tool.Id)); // Reload không làm mất trạng thái đang chạy
            item.PropertyChanged += OnItemChanged;
            _items.Add(item);
        }

        private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (_store == null)
                return;
            _matches = new HashSet<ToolEntry>(_store.Search(SearchBox.Text, _selectedTags));
            UpdateTagSummary();
            _view.Refresh();
            UpdateFooter();
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolItem.IsChecked))
                UpdateFooter();
        }

        private void UpdateFooter()
        {
            var checkedCount = CheckedItems().Count;
            PackButton.Content = checkedCount > 0 ? $"Chia sẻ ({checkedCount})" : "Chia sẻ";
            PackButton.IsEnabled = checkedCount > 0;
            RemoveButton.IsEnabled = checkedCount > 0;
            var visible = _view.Cast<ToolItem>().ToList();
            SelectAll.IsChecked = visible.Count > 0 && visible.All(i => i.IsChecked);
            EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsHint.Visibility = _items.Count > 0 && visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var toInstall = _items.Count(i => i.Tool.CanInstall);
            var missing = _items.Count(i => !i.Tool.PathExists) - toInstall;
            StatusText.Text = $"{_items.Count} tool" + (missing > 0 ? $" · {missing} tool không tìm thấy file" : "")
                + (toInstall > 0 ? $" · {toInstall} app chưa cài" : "")
                + (checkedCount > 0 ? $" · đã chọn {checkedCount}" : "");
        }

        private void RefreshStatus()
        {
            if (_store == null)
                return;
            var changed = false;
            foreach (var item in _items)
                changed |= item.Refresh();
            // Chỉ vẽ lại footer khi có thay đổi, để không xoá thông báo trạng thái mỗi lần cửa sổ được kích hoạt.
            if (changed)
                UpdateFooter();
        }

        private List<ToolItem> CheckedItems() => _items.Where(i => i.IsChecked).ToList();

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            var value = SelectAll.IsChecked == true;
            foreach (ToolItem item in _view)
                item.IsChecked = value;
        }

        private bool Save()
        {
            try
            {
                _store.Save();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không lưu được danh sách: " + ex.Message);
                return false;
            }
        }

        // ---------- Thêm / sửa / xoá / chạy ----------

        private void OnAdd(object sender, RoutedEventArgs e) => AddTool(new ToolEntry());

        private void OnPickApp(object sender, RoutedEventArgs e)
        {
            var dialog = new AppPickerWindow(_store.Tools) { Owner = Host };
            if (dialog.ShowDialog() == true)
                AddTool(ToolDefaults.FromShortcut(dialog.Selected));
        }

        private void AddTool(ToolEntry draft)
        {
            var dialog = new ToolEditWindow(draft, isNew: true) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            _store.Add(draft);
            if (Save())
            {
                Reload();
                StatusText.Text = $"Đã thêm \"{draft.Name}\".";
            }
        }

        private void OnEdit(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            var copy = item.Tool.Clone();
            var dialog = new ToolEditWindow(copy, isNew: false) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                _store.Update(copy);
            }
            catch (KeyNotFoundException)
            {
                // Agent (hoặc người khác) đã xoá tool trong lúc đang sửa.
                Dialogs.Error(Host, $"Tool \"{copy.Name}\" vừa bị xoá khỏi danh sách ở nơi khác nên không lưu được chỉnh sửa.");
                Reload();
                return;
            }
            if (Save())
                Reload();
        }

        /// <summary>Chọn workspace cho tool này. Tool vẫn nằm trong thư viện; workspace được tick sẽ thấy tool để chạy.</summary>
        private void OnBind(object sender, RoutedEventArgs e)
        {
            var tool = ItemOf(sender).Tool;
            WorkspaceStore store;
            try
            {
                store = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, ex.Message);
                return;
            }
            if (store.Workspaces.Count == 0)
            {
                Dialogs.Error(Host, "Chưa có workspace nào. Thêm workspace ở cột bên trái trước, rồi quay lại đây để gắn tool.");
                return;
            }

            var dialog = new CheckListWindow(
                $"Gắn \"{tool.Name}\" vào workspace",
                "Tick các workspace sẽ dùng tool này. Bỏ tick để gỡ khỏi workspace đó. Tool vẫn nằm trong thư viện.",
                "Lưu",
                store.Workspaces.Select(w => new CheckItem(w.Id, w.Name, w.ResolvedPath, w.FindLibrary(tool.Id) != null)),
                "")
            { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            foreach (var item in dialog.Items)
            {
                var workspace = store.Find(item.Key);
                if (workspace == null)
                    continue;
                if (item.IsChecked)
                    workspace.BindLibrary(tool);
                else
                    workspace.UnbindLibrary(tool.Id);
            }
            try
            {
                store.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không lưu được workspace: " + ex.Message);
                return;
            }
            StatusText.Text = $"Đã cập nhật workspace của \"{tool.Name}\".";
        }

        /// <summary>Tool đã xoá khỏi thư viện thì gỡ khỏi mọi workspace để không còn tham chiếu treo.</summary>
        private void UnbindFromWorkspaces(IEnumerable<string> toolIds)
        {
            try
            {
                var store = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
                var changed = toolIds.Sum(id => store.UnbindToolEverywhere(id));
                if (changed > 0)
                    store.Save();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Đã xoá khỏi thư viện nhưng chưa gỡ được khỏi workspace: " + ex.Message);
            }
        }

        /// <summary>
        /// Xoá tool đã tick. Xoá khỏi danh sách là thao tác đảo được nên làm ngay rồi hiện thông báo có nút Hoàn tác (8 giây);
        /// chỉ hỏi trước khi phải xoá cả bản chép trong thư viện Toolbox (không khôi phục được).
        /// </summary>
        private void OnRemove(object sender, RoutedEventArgs e)
        {
            var items = CheckedItems();
            var tools = items.Select(i => i.Tool).ToList();
            if (tools.Count == 0)
                return;

            var deleteCopies = false;
            var withCopies = tools.Count(t => Library.HasCopy(_store, t.Id));
            if (withCopies > 0)
            {
                var names = string.Join("\n", tools.Select(t => "• " + t.Name));
                var answer = Dialogs.Choose(Host, $"Xoá {tools.Count} tool khỏi danh sách?\n\n{names}\n\n{withCopies} tool có bản chép trong thư viện Toolbox."
                    + "\n\nYes: xoá luôn bản chép trong thư viện (không khôi phục được)."
                    + "\nNo: chỉ bỏ khỏi danh sách, giữ bản chép để khôi phục sau."
                    + "\nCancel: không làm gì.");
                if (answer == null)
                    return;
                deleteCopies = answer.Value;
            }

            var removed = tools.Select(t => t.Clone()).ToList();
            foreach (var tool in tools)
                _store.Remove(tool.Id);
            // Lưu danh sách trước: nếu lưu lỗi thì không xoá bản chép của tool vẫn còn trong tools.json.
            if (!Save())
                return;
            var ids = tools.Select(t => t.Id).ToList();
            var bound = CaptureBindings(ids);
            UnbindFromWorkspaces(ids);
            if (deleteCopies)
                DeleteCopies(tools);
            Reload();

            var label = removed.Count == 1 ? $"Đã xoá \"{removed[0].Name}\"" : $"Đã xoá {removed.Count} tool";
            if (deleteCopies)
                Dialogs.Success(label + ".");
            else
                Dialogs.Success(label + ".", "Hoàn tác", () => RestoreTools(removed, bound));
        }

        /// <summary>Chỗ gắn của các tool trước khi gỡ khỏi workspace, để hoàn tác gắn lại đúng chỗ.</summary>
        private List<KeyValuePair<string, ToolBinding>> CaptureBindings(IEnumerable<string> toolIds)
        {
            var result = new List<KeyValuePair<string, ToolBinding>>();
            try
            {
                var store = WorkspaceStore.Peek(WorkspaceStore.DefaultPath);
                foreach (var workspace in store.Workspaces)
                    foreach (var binding in workspace.Bindings.Where(b => toolIds.Contains(b.ToolId, StringComparer.OrdinalIgnoreCase)))
                        result.Add(new KeyValuePair<string, ToolBinding>(workspace.Id, new ToolBinding { ToolId = binding.ToolId, Cwd = binding.Cwd, Args = binding.Args }));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return result;
        }

        /// <summary>Hoàn tác xoá: đưa tool trở lại danh sách và gắn lại vào các workspace cũ (trừ chỗ đã gắn lại bằng tay).</summary>
        private void RestoreTools(List<ToolEntry> tools, List<KeyValuePair<string, ToolBinding>> bound)
        {
            try
            {
                foreach (var tool in tools)
                    if (_store.Find(tool.Id) == null)
                        _store.Add(tool);
                if (!Save())
                    return;
                if (bound.Count > 0)
                    WorkspaceStore.Update(WorkspaceStore.DefaultPath, store =>
                    {
                        var any = false;
                        foreach (var pair in bound)
                        {
                            var workspace = store.Find(pair.Key);
                            if (workspace == null || workspace.Binding(pair.Value.ToolId) != null)
                                continue;
                            workspace.Bindings.Add(pair.Value);
                            any = true;
                        }
                        return any;
                    });
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(Host, "Không hoàn tác được: " + ex.Message);
                return;
            }
            Reload();
            Dialogs.Success(tools.Count == 1 ? $"Đã khôi phục \"{tools[0].Name}\"." : $"Đã khôi phục {tools.Count} tool.");
        }

        /// <summary>Xoá bản chép sau khi danh sách đã lưu. Lỗi của một tool không chặn các tool còn lại.</summary>
        private void DeleteCopies(List<ToolEntry> tools)
        {
            var failed = new List<string>();
            foreach (var tool in tools)
            {
                try
                {
                    Library.DeleteCopy(_store, tool.Id);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    failed.Add($"• {tool.Name}: {ex.Message}");
                }
            }
            if (failed.Count > 0)
                Dialogs.Error(Host, "Đã bỏ khỏi danh sách nhưng không xoá được một số bản chép (có thể đang được chạy):\n\n" + string.Join("\n", failed));
        }

        private void OnRun(object sender, RoutedEventArgs e) => Run(ItemOf(sender));

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Chỉ chạy khi double-click vào dòng, không phải vào nút/checkbox.
            if (e.OriginalSource is DependencyObject source && FindParent<ButtonBase>(source) == null
                && ToolList.SelectedItem is ToolItem item)
                Run(item);
        }

        private void Run(ToolItem item)
        {
            // Double-click / Enter lúc đang chạy không mở thêm bản thứ hai ngoài ý muốn.
            if (item.IsRunning)
            {
                StatusText.Text = $"\"{item.Tool.Name}\" đang chạy. Bấm Dừng nếu muốn tắt.";
                return;
            }

            try
            {
                _runs.Start(item.Tool);
                UsageStore.Shared.Record(UsageStore.Key("tool", item.Tool.Id));
                StatusText.Text = $"Đã mở \"{item.Tool.Name}\".";
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is System.ComponentModel.Win32Exception)
            {
                Dialogs.Error(Host, ex.Message);
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            _runs.Stop(item.Tool.Id);
            StatusText.Text = $"Đang dừng \"{item.Tool.Name}\"…";
        }

        /// <summary>Đẩy trạng thái chạy từ RunTracker xuống từng dòng và cập nhật số tool đang chạy ở chân cửa sổ.</summary>
        private void RefreshRuns()
        {
            foreach (var item in _items)
                item.ApplyRun(_runs.Current(item.Tool.Id), _runs.Last(item.Tool.Id));

            var running = _runs.RunningCount;
            RunningText.Text = $"● {running} tool đang chạy";
            RunningText.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnInstallSetup(object sender, RoutedEventArgs e)
        {
            var tool = ItemOf(sender).Tool;
            System.Diagnostics.Process process;
            try
            {
                process = Runner.StartSetup(tool);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return; // bấm "No" ở hộp UAC
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
            {
                Dialogs.Error(Host, "Không mở được bộ cài: " + ex.Message);
                return;
            }

            StatusText.Text = $"Đang cài \"{tool.Name}\"…";
            if (process != null)
                using (process)
                    await Task.Run(() => process.WaitForExit());

            RefreshStatus();
            StatusText.Text = tool.PathExists
                ? $"Đã cài xong \"{tool.Name}\"."
                : $"Chưa thấy \"{tool.Name}\" ở {tool.ResolvedPath}. Nếu đã cài vào chỗ khác, bấm Sửa để chỉnh đường dẫn.";
        }

        private const int ErrorCancelled = 1223;

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Runner.OpenLocation(ItemOf(sender).Tool);
            }
            catch (FileNotFoundException ex)
            {
                Dialogs.Error(Host, ex.Message);
            }
        }

        // ---------- Đóng gói ----------

        private async void OnPack(object sender, RoutedEventArgs e)
        {
            var tools = CheckedItems().Select(i => i.Tool).ToList();

            List<PackPreview> previews;
            try
            {
                previews = await RunBusy(() => tools.Select(Packer.Preview).ToList());
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không đóng gói được: " + ex.Message);
                return;
            }

            var summary = new StringBuilder();
            foreach (var p in previews)
            {
                summary.Append("• ").Append(p.Tool.Name).Append(" — ");
                summary.AppendLine(p.SourceRoot == null ? "chỉ gửi thông tin"
                    : p.Tool.Pack == PackMode.Setup ? $"kèm bộ cài {Path.GetFileName(p.Tool.ResolvedSetup)}, {Format.Size(p.TotalBytes)}"
                    : $"{p.FileCount} file, {Format.Size(p.TotalBytes)}");
                if (p.IsBroadFolder)
                    summary.AppendLine($"    ⚠ lấy cả thư mục rộng: {p.SourceRoot} — nên sửa tool, chọn \"Chỉ file\"");
            }
            summary.AppendLine().Append("Tổng: ").Append(Format.Size(previews.Sum(p => p.TotalBytes))).Append(". Tiếp tục?");
            if (!Dialogs.Confirm(Host, summary.ToString(), previews.Any(p => p.IsBroadFolder)))
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Lưu gói",
                Filter = "Gói Toolbox (*.toolbox)|*.toolbox",
                FileName = (tools.Count == 1 ? tools[0].Id : "toolbox-" + DateTime.Now.ToString("yyyyMMdd")) + Packer.FileExtension,
            };
            if (dialog.ShowDialog(Host) != true)
                return;
            var zipPath = EnsurePackageExtension(dialog.FileName);

            try
            {
                await RunBusy(() => Packer.Pack(tools, zipPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(Host, "Không đóng gói được: " + ex.Message);
                return;
            }

            StatusText.Text = $"Đã đóng gói {tools.Count} tool → {zipPath}";
            if (Dialogs.Confirm(Host, $"Đã tạo gói ({Format.Size(new FileInfo(zipPath).Length)}):\n{zipPath}\n\nMở thư mục chứa gói?"))
                Runner.OpenLocation(new ToolEntry { Name = "gói", Path = zipPath });
        }

        private static string EnsurePackageExtension(string path)
        {
            return path.EndsWith(Packer.FileExtension, StringComparison.OrdinalIgnoreCase) ? path : path + Packer.FileExtension;
        }

        // ---------- Nhận gói ----------

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Chọn gói cần nhập",
                Filter = "Gói Toolbox (*.toolbox;*.zip)|*.toolbox;*.zip",
            };
            if (dialog.ShowDialog(Host) == true)
                ImportPackage(dialog.FileName);
        }

        private async void ImportPackage(string zipPath)
        {
            PackManifest manifest;
            try
            {
                manifest = Packer.ReadManifest(zipPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không mở được gói: " + ex.Message);
                return;
            }

            var dialog = new ImportWindow(manifest, _store) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;

            IList<ToolEntry> imported;
            try
            {
                imported = await RunBusy(() => PackageImporter.Import(_store, zipPath, dialog.Destination, dialog.Choices));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(Host, "Không nhập được gói: " + ex.Message);
                return;
            }

            if (Save())
            {
                Reload();
                StatusText.Text = $"Đã nhập {imported.Count} tool từ {Path.GetFileName(zipPath)}.";
            }
        }

        // ---------- Kéo thả ----------

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths))
                return;

            foreach (var path in paths)
            {
                if ((path.EndsWith(Packer.FileExtension, StringComparison.OrdinalIgnoreCase)
                     || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) && IsPackage(path))
                    ImportPackage(path);
                else
                    AddTool(ToolDefaults.FromPath(path));
            }
        }

        private static bool IsPackage(string path)
        {
            try
            {
                Packer.ReadManifest(path);
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                return false;
            }
        }

        // ---------- Agent ----------

        /// <summary>Mở cửa sổ Kết nối agent từ trang Agent.</summary>
        public void OpenAgentWindow() => OnConnectAgent(null, null);

        /// <summary>Mở hộp chọn app đã cài để thêm tool (cho bước làm quen ở Trang chủ).</summary>
        public void StartPickApp() => OnPickApp(null, null);

        private void OnConnectAgent(object sender, RoutedEventArgs e)
        {
            var window = new AgentWindow(_installer) { Owner = Host };
            window.ShowDialog();
            if (window.InstalledNow)
                InstallBanner.Visibility = Visibility.Collapsed;
        }

        // ---------- Tiện ích ----------

        private async Task<T> RunBusy<T>(Func<T> work)
        {
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                return await Task.Run(work);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                IsEnabled = true;
            }
        }

        private Task RunBusy(Action work) => RunBusy(() => { work(); return true; });

        private static ToolItem ItemOf(object sender) => (ToolItem)((FrameworkElement)sender).Tag;

        private static T FindParent<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null && !(node is T))
                node = node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            return node as T;
        }
    }
}
