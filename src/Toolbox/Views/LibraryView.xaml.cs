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
        /// <summary>Mọi tool của thư viện, theo thứ tự trong kho.</summary>
        private List<ToolItem> _all = new List<ToolItem>();

        /// <summary>Các dòng đang hiện sau khi lọc và sắp xếp.</summary>
        private readonly BulkCollection<ToolItem> _shown = new BulkCollection<ToolItem>();
        private readonly ObservableCollection<TagOption> _tagOptions = new ObservableCollection<TagOption>();
        private readonly ObservableCollection<TagOption> _activeTags = new ObservableCollection<TagOption>();
        private readonly HashSet<string> _selectedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _hooked;
        private readonly Installer _installer = new Installer();
        private readonly RunTracker _runs = new RunTracker();
        private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>Gõ liên tục thì chỉ lọc một lần sau khi ngừng gõ một nhịp, để ô tìm không giật khi thư viện lớn.</summary>
        private readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        private ToolStore _store;
        private int _checkedCount;
        private bool _bulkCheck;
        private bool _onlyProblems;
        private LibrarySort _sort = LibrarySort.Name;
        private int _probeVersion;
        private DateTime _probedAt = DateTime.MinValue;
        private HashSet<string> _missingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LibraryView()
        {
            InitializeComponent();
            ToolList.ItemsSource = _shown;
            TagChips.ItemsSource = _tagOptions;
            ActiveTags.ItemsSource = _activeTags;
            SortBox.ItemsSource = SortOption.All;
            Loaded += OnLoaded;
            EmptyHint.AddAction("App đã cài…", () => OnPickApp(null, null));
            EmptyHint.AddAction("Thêm tool", () => OnAdd(null, null));
            // Exited/Changed đến từ luồng khác: chuyển về luồng giao diện rồi mới đụng vào item.
            _runs.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshRuns));
            _clock.Tick += (s, e) =>
            {
                foreach (var item in _all)
                    item.Tick();
            };
            _searchDelay.Tick += (s, e) =>
            {
                _searchDelay.Stop();
                ApplyFilter();
                if (_shown.Count > 0)
                    ToolList.ScrollIntoView(_shown[0]);
            };
        }

        /// <summary>File .toolbox truyền qua dòng lệnh; nhập ngay khi trang tải xong.</summary>
        public string FileToOpen { get; set; }

        /// <summary>Tool trong thư viện (cho chỉ mục tìm toàn app). Rỗng nếu chưa nạp được.</summary>
        public IReadOnlyList<ToolEntry> Tools => _store?.Tools ?? (IReadOnlyList<ToolEntry>)new List<ToolEntry>();

        /// <summary>Kết quả kiểm file vừa đổi (tool mất đường dẫn, app vừa cài xong): Trang chủ dựng lại danh sách việc cần làm.</summary>
        public event Action StatusChanged;

        /// <summary>
        /// Đường dẫn này đã được kiểm ở nền và không còn. Chưa kiểm thì trả về false, để Trang chủ không phải tự đọc đĩa cho cả thư viện
        /// trên luồng giao diện.
        /// </summary>
        public bool IsKnownMissing(string resolvedPath) => _missingPaths.Contains(resolvedPath ?? "");

        /// <summary>Số dòng đang hiện sau khi lọc (để test).</summary>
        public int ShownCount => _shown.Count;

        /// <summary>Bảng lệnh mở một tool: lọc danh sách về đúng tool đó và đưa con trỏ tới đó.</summary>
        public void FocusTool(string toolId)
        {
            var item = _all.FirstOrDefault(i => string.Equals(i.Tool.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;
            _onlyProblems = false;
            _selectedTags.Clear();
            foreach (var option in _tagOptions)
                option.IsSelected = false;
            SearchBox.Text = item.Tool.Name;
            _searchDelay.Stop();
            ApplyFilter();
            ToolList.SelectedItem = item;
            ToolList.ScrollIntoView(item);
        }

        /// <summary>Trang chủ bấm "Xem trong Thư viện" ở dòng gom các tool mất đường dẫn: chỉ hiện các tool đó.</summary>
        public void ShowProblems()
        {
            SearchBox.Text = "";
            _onlyProblems = true;
            _searchDelay.Stop();
            ApplyFilter();
        }

        /// <summary>Cửa sổ chủ, dùng làm owner cho hộp thoại.</summary>
        private Window Host => Window.GetWindow(this);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_store != null)
                return; // Loaded bắn lại mỗi lần trang được gắn lại vào cây giao diện; chỉ nạp kho một lần
            if (!_hooked)
            {
                _hooked = true;
                var host = Host;
                // Quay lại cửa sổ (vd sau khi cài app, xoá/di chuyển file) → cập nhật trạng thái "không tìm thấy".
                host.Activated += (s, a) => RefreshStatus();
                host.Closed += (s, a) => _clock.Stop();
            }

            var settings = Settings.Load();
            _sort = SortOption.Parse(settings.LibrarySort);
            SortBox.SelectedItem = SortOption.All.First(o => o.Sort == _sort);
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
            InstallBanner.Visibility = settings.InstallBannerDismissed || _installer.IsInstalledCopy(CurrentExe)
                ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(FileToOpen))
                ImportPackage(FileToOpen);
        }

        // ---------- Cài đặt ----------

        private static string CurrentExe => System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

        private void OnDismissInstallBanner(object sender, RoutedEventArgs e)
        {
            Settings.Update(s => s.InstallBannerDismissed = true);
            InstallBanner.Visibility = Visibility.Collapsed;
        }

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

        /// <summary>
        /// Đưa danh sách trong kho lên màn hình. Dòng của tool đã có được dùng lại (giữ tick, trạng thái chạy, icon, kết quả kiểm file),
        /// nên thêm, sửa hay ghim một tool không phải dựng lại cả nghìn dòng.
        /// </summary>
        private void Reload()
        {
            var old = new Dictionary<string, ToolItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _all)
                old[item.Tool.Id ?? ""] = item;

            var next = new List<ToolItem>(_store.Tools.Count);
            foreach (var tool in _store.Tools)
            {
                if (old.TryGetValue(tool.Id ?? "", out var item))
                {
                    old.Remove(tool.Id ?? "");
                    item.Rebind(tool);
                }
                else
                {
                    item = new ToolItem(tool);
                    item.ApplyRun(_runs.Current(tool.Id), _runs.Last(tool.Id));
                    item.PropertyChanged += OnItemChanged;
                }
                next.Add(item);
            }
            foreach (var gone in old.Values)
                gone.PropertyChanged -= OnItemChanged;

            _all = next;
            _checkedCount = _all.Count(i => i.IsChecked);
            BuildTagChips();
            ApplyFilter();
            StartProbe();
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

        // ---------- Kiểm file ở nền ----------

        /// <summary>
        /// Kiểm từng tool còn file không ở luồng nền rồi đẩy kết quả lên các dòng. Luồng giao diện không bao giờ chờ đĩa,
        /// kể cả khi thư viện có cả nghìn tool hoặc có tool nằm trên ổ mạng đang rớt.
        /// </summary>
        private async void StartProbe()
        {
            var version = ++_probeVersion;
            _probedAt = DateTime.UtcNow;
            var items = _all.ToArray();
            if (items.Length == 0)
            {
                ApplyProbe(items, new (bool, bool)[0]);
                return;
            }

            (bool Exists, bool CanInstall)[] states;
            try
            {
                states = await Task.Run(() =>
                {
                    var result = new (bool, bool)[items.Length];
                    Parallel.For(0, items.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => result[i] = ToolItem.Probe(items[i].Tool));
                    return result;
                });
            }
            catch (Exception)
            {
                return; // kiểm không xong thì giữ trạng thái đang hiện, lần kích hoạt cửa sổ sau sẽ kiểm lại
            }
            // Danh sách đã nạp lại trong lúc kiểm: lượt kiểm mới hơn sẽ đẩy kết quả.
            if (version == _probeVersion)
                ApplyProbe(items, states);
        }

        private void ApplyProbe(ToolItem[] items, (bool Exists, bool CanInstall)[] states)
        {
            var changed = false;
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < items.Length; i++)
            {
                changed |= items[i].ApplyProbe(states[i].Exists, states[i].CanInstall);
                if (!states[i].Exists)
                    missing.Add(items[i].Tool.ResolvedPath);
            }
            var missingChanged = !missing.SetEquals(_missingPaths);
            _missingPaths = missing;
            // Chỉ vẽ lại footer khi có thay đổi, để không xoá thông báo trạng thái mỗi lần cửa sổ được kích hoạt.
            if (changed)
            {
                if (_onlyProblems)
                    ApplyFilter();
                else
                    UpdateFooter();
            }
            if (changed || missingChanged)
                StatusChanged?.Invoke();
        }

        /// <summary>Kiểm lại file khi quay lại cửa sổ; không kiểm dồn nếu vừa kiểm xong.</summary>
        private void RefreshStatus()
        {
            if (_store != null && DateTime.UtcNow - _probedAt > TimeSpan.FromSeconds(2))
                StartProbe();
        }

        // ---------- Tag ----------

        /// <summary>Dựng lại chip tag từ kho, kèm số tool mỗi tag; giữ các tag đang chọn nếu vẫn còn tồn tại.</summary>
        private void BuildTagChips()
        {
            var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in _store.Tools)
                foreach (var tag in tool.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                    counts[tag] = counts.TryGetValue(tag, out var n) ? n + 1 : 1;

            // Tag và số đếm không đổi (trường hợp thường gặp: ghim, sửa mô tả) thì giữ nguyên chip, khỏi dựng lại.
            if (!counts.Select(p => p.Key + "\n" + p.Value).SequenceEqual(_tagOptions.Select(o => o.Name + "\n" + o.Count)))
            {
                foreach (var option in _tagOptions)
                    option.PropertyChanged -= OnTagChanged;
                _tagOptions.Clear();
                foreach (var pair in counts)
                {
                    var option = new TagOption(pair.Key, pair.Value) { IsSelected = _selectedTags.Contains(pair.Key) };
                    option.PropertyChanged += OnTagChanged;
                    _tagOptions.Add(option);
                }
                _selectedTags.IntersectWith(_tagOptions.Where(o => o.IsSelected).Select(o => o.Name));
            }

            TagToggle.Visibility = _tagOptions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_tagOptions.Count == 0)
                TagToggle.IsChecked = false;
        }

        private void OnTagPanelToggled(object sender, RoutedEventArgs e) =>
            TagPanel.Visibility = TagToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        private void OnActiveTagRemoved(object sender, RoutedEventArgs e) =>
            ((TagOption)((FrameworkElement)sender).DataContext).IsSelected = false;

        /// <summary>Cập nhật phần tóm tắt bộ lọc đang bật: badge trên nút Tag, dãy pill và nút Bỏ hết.</summary>
        private void UpdateTagSummary()
        {
            var selected = _tagOptions.Where(o => o.IsSelected).ToList();
            if (!selected.SequenceEqual(_activeTags))
            {
                _activeTags.Clear();
                foreach (var option in selected)
                    _activeTags.Add(option);
            }

            TagBadgeText.Text = selected.Count.ToString();
            TagBadge.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProblemPill.Visibility = _onlyProblems ? Visibility.Visible : Visibility.Collapsed;
            ActiveTagsRow.Visibility = selected.Count > 0 || _onlyProblems ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool _clearingTags;

        private void OnTagChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TagOption.IsSelected))
                return;
            var option = (TagOption)sender;
            if (option.IsSelected)
                _selectedTags.Add(option.Name);
            else
                _selectedTags.Remove(option.Name);
            if (!_clearingTags)
                ApplyFilter();
        }

        private void OnClearTags(object sender, RoutedEventArgs e)
        {
            _clearingTags = true;
            _selectedTags.Clear();
            foreach (var option in _tagOptions)
                option.IsSelected = false;
            _clearingTags = false;
            _onlyProblems = false;
            ApplyFilter();
        }

        private void OnClearFilters(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            _searchDelay.Stop();
            OnClearTags(sender, e);
            SearchBox.Focus();
        }

        private void OnProblemPillClick(object sender, RoutedEventArgs e)
        {
            _onlyProblems = false;
            ApplyFilter();
        }

        /// <summary>Bấm dòng "n tool không tìm thấy file" ở chân trang: bật hoặc tắt lọc chỉ các tool đó.</summary>
        private void OnToggleProblems(object sender, RoutedEventArgs e)
        {
            _onlyProblems = !_onlyProblems;
            ApplyFilter();
        }

        private void OnSortChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SortBox.SelectedItem is SortOption option) || option.Sort == _sort)
                return;
            _sort = option.Sort;
            Settings.Update(s => s.LibrarySort = SortOption.Name(_sort));
            ApplyFilter();
            if (_shown.Count > 0)
                ToolList.ScrollIntoView(_shown[0]);
        }

        /// <summary>
        /// Phím tắt: Ctrl+F nhảy vào ô tìm (Ctrl+K là bảng lệnh toàn app, xử lý ở cửa sổ chính); Esc trong ô tìm xoá chữ; Enter trên dòng đang chọn thì chạy
        /// (tương đương double-click, để dùng được hoàn toàn bằng bàn phím). Mũi tên xuống trong ô tìm đưa con trỏ vào danh sách.
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
            else if (e.Key == Key.Down && SearchBox.IsKeyboardFocused && _shown.Count > 0)
            {
                FlushSearch();
                if (_shown.Count == 0)
                    return;
                if (ToolList.SelectedItem == null)
                    ToolList.SelectedItem = _shown[0];
                ToolList.UpdateLayout();
                ToolList.ScrollIntoView(ToolList.SelectedItem);
                (ToolList.ItemContainerGenerator.ContainerFromItem(ToolList.SelectedItem) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused)
            {
                // Enter trong ô tìm: chạy luôn nếu chỉ còn đúng một kết quả, không thì chọn dòng đầu.
                FlushSearch();
                if (_shown.Count == 1 && !_shown[0].CanInstall)
                    Run(_shown[0]);
                else if (_shown.Count > 0)
                    ToolList.SelectedItem = _shown[0];
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.OriginalSource is System.Windows.Controls.ListBoxItem
                     && ToolList.SelectedItem is ToolItem item && !item.CanInstall)
            {
                Run(item);
                e.Handled = true;
            }
        }

        /// <summary>Áp ngay bộ lọc đang chờ (người dùng gõ xong rồi bấm phím hành động trước khi hết nhịp chờ).</summary>
        private void FlushSearch()
        {
            if (!_searchDelay.IsEnabled)
                return;
            _searchDelay.Stop();
            ApplyFilter();
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        }

        private void ApplyFilter()
        {
            if (_store == null)
                return;
            var terms = Core.Text.Fold(SearchBox.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tags = new HashSet<string>(_selectedTags.Select(Core.Text.Fold));
            var matches = _all.Where(i => i.Matches(terms, tags) && (!_onlyProblems || i.HasProblem));
            _shown.ResetTo(Sorted(matches).ToList());
            UpdateTagSummary();
            UpdateFooter();
        }

        private IEnumerable<ToolItem> Sorted(IEnumerable<ToolItem> items)
        {
            var byName = StringComparer.CurrentCultureIgnoreCase;
            switch (_sort)
            {
                case LibrarySort.Recent:
                    return items.OrderByDescending(i => i.LastUsed ?? DateTime.MinValue).ThenBy(i => i.Tool.Name, byName);
                case LibrarySort.Pinned:
                    return items.OrderByDescending(i => i.Tool.Pinned).ThenBy(i => i.Tool.Name, byName);
                default:
                    return items.OrderBy(i => i.Tool.Name, byName);
            }
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ToolItem.IsChecked))
                return;
            _checkedCount += ((ToolItem)sender).IsChecked ? 1 : -1;
            if (!_bulkCheck)
                UpdateFooter();
        }

        private void UpdateFooter()
        {
            PackButton.Content = _checkedCount > 0 ? $"Chia sẻ ({_checkedCount})" : "Chia sẻ";
            PackButton.IsEnabled = _checkedCount > 0;
            RemoveButton.IsEnabled = _checkedCount > 0;
            SelectAll.IsChecked = _shown.Count > 0 && _shown.All(i => i.IsChecked);
            EmptyHint.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsHint.Visibility = _all.Count > 0 && _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var toInstall = _all.Count(i => i.CanInstall);
            var missing = _all.Count(i => i.HasProblem) - toInstall;
            StatusText.Text = (_shown.Count == _all.Count ? $"{_all.Count} tool" : $"Hiện {_shown.Count}/{_all.Count} tool")
                + (toInstall > 0 ? $" · {toInstall} app chưa cài" : "")
                + (_checkedCount > 0 ? $" · đã chọn {_checkedCount}" : "");
            ProblemsButton.Visibility = missing > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProblemsText.Text = $"{missing} tool không tìm thấy file";
            ProblemsButton.ToolTip = _onlyProblems ? "Đang chỉ hiện các tool này. Bấm để hiện lại tất cả." : "Bấm để chỉ hiện các tool này";
        }

        private List<ToolItem> CheckedItems() => _all.Where(i => i.IsChecked).ToList();

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            var value = SelectAll.IsChecked == true;
            _bulkCheck = true;
            foreach (var item in _shown)
                item.IsChecked = value;
            _bulkCheck = false;
            UpdateFooter();
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

        /// <summary>Ghim hoặc bỏ ghim tool lên Trang chủ.</summary>
        private void OnTogglePin(object sender, RoutedEventArgs e)
        {
            var copy = ItemOf(sender).Tool.Clone();
            copy.Pinned = !copy.Pinned;
            try
            {
                _store.Update(copy);
            }
            catch (KeyNotFoundException)
            {
                Dialogs.Error(Host, $"Tool \"{copy.Name}\" vừa bị xoá khỏi danh sách ở nơi khác.");
                Reload();
                return;
            }
            if (Save())
                Reload();
        }

        /// <summary>Chạy một tool theo id (nút "Mở" của tool ghim ở Trang chủ). False nếu không thấy tool.</summary>
        public bool RunTool(string toolId)
        {
            var item = _all.FirstOrDefault(i => string.Equals(i.Tool.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return false;
            Run(item);
            return true;
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
        private void OnRemove(object sender, RoutedEventArgs e) => RemoveTools(CheckedItems().Select(i => i.Tool).ToList());

        private void RemoveTools(List<ToolEntry> tools)
        {
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
                item.RefreshUsage();
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
            foreach (var item in _all)
                item.ApplyRun(_runs.Current(item.Tool.Id), _runs.Last(item.Tool.Id));

            var running = _runs.RunningCount;
            RunningText.Text = $"● {running} tool đang chạy";
            RunningText.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
            // Đồng hồ thời lượng chỉ chạy khi có tool đang chạy.
            if (running > 0 && !_clock.IsEnabled)
                _clock.Start();
            else if (running == 0)
                _clock.Stop();
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

            StartProbe();
            StatusText.Text = tool.PathExists
                ? $"Đã cài xong \"{tool.Name}\"."
                : $"Chưa thấy \"{tool.Name}\" ở {tool.ResolvedPath}. Nếu đã cài vào chỗ khác, bấm Sửa để chỉnh đường dẫn.";
        }

        private const int ErrorCancelled = 1223;

        /// <summary>Tool mất file (đã di chuyển, đổi tên): chọn lại file hoặc thư mục, giữ nguyên mọi thông tin khác của tool.</summary>
        private void OnRelocate(object sender, RoutedEventArgs e)
        {
            var tool = ItemOf(sender).Tool;
            var old = tool.ResolvedPath;
            var dialog = new OpenFileDialog
            {
                Title = $"Chọn lại file của \"{tool.Name}\"",
                FileName = Path.GetFileName(old),
                CheckFileExists = true,
            };
            // Mở sẵn thư mục gần nhất còn tồn tại quanh đường dẫn cũ, thường file chỉ bị dời đi một hai cấp.
            for (var dir = Path.GetDirectoryName(old); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                    break;
                }
            if (dialog.ShowDialog(Host) != true)
                return;

            var copy = tool.Clone();
            copy.Path = dialog.FileName;
            copy.Store = StoreMode.Link; // đường dẫn mới do người dùng chỉ, không còn là bản chép trong thư viện
            try
            {
                _store.Update(copy);
            }
            catch (KeyNotFoundException)
            {
                Dialogs.Error(Host, $"Tool \"{copy.Name}\" vừa bị xoá khỏi danh sách ở nơi khác.");
                Reload();
                return;
            }
            if (Save())
            {
                Reload();
                Dialogs.Success($"Đã trỏ \"{copy.Name}\" tới {copy.Path}.");
            }
        }

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

        /// <summary>Menu "…" của một dòng tool: hành động phụ nằm đây để dòng chỉ còn một nút chính. Chuột phải vào dòng mở cùng menu này.</summary>
        private void OnRowMenu(object sender, RoutedEventArgs e)
        {
            var menu = BuildRowMenu(ItemOf(sender));
            menu.PlacementTarget = (FrameworkElement)sender;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnListRightClick(object sender, MouseButtonEventArgs e)
        {
            var container = e.OriginalSource is DependencyObject source ? FindParent<ListBoxItem>(source) : null;
            if (!(container?.DataContext is ToolItem item))
                return;
            ToolList.SelectedItem = item;
            var menu = BuildRowMenu(item);
            menu.PlacementTarget = container;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private ContextMenu BuildRowMenu(ToolItem item)
        {
            var menu = new ContextMenu();
            if (item.IsRunning)
                menu.Items.Add(RowMenuEntry("Dừng", OnStop, item, ""));
            else if (item.CanInstall)
                menu.Items.Add(RowMenuEntry("Cài đặt", OnInstallSetup, item, ""));
            else
                menu.Items.Add(RowMenuEntry("Chạy", OnRun, item, "", "Enter"));
            menu.Items.Add(RowMenuEntry(item.Tool.Pinned ? "Bỏ ghim khỏi Trang chủ" : "Ghim lên Trang chủ", OnTogglePin, item, item.Tool.Pinned ? "" : ""));
            menu.Items.Add(new Separator());
            menu.Items.Add(RowMenuEntry("Gắn vào workspace…", OnBind, item, ""));
            menu.Items.Add(RowMenuEntry("Mở thư mục", OnOpenFolder, item, ""));
            menu.Items.Add(RowMenuEntry(item.Tool.IsCommand ? "Sao chép lệnh" : "Sao chép đường dẫn", OnCopyPath, item, ""));
            menu.Items.Add(RowMenuEntry("Sửa…", OnEdit, item, ""));
            menu.Items.Add(new Separator());
            menu.Items.Add(RowMenuEntry("Xoá khỏi danh sách", OnRemoveOne, item, "", "Del"));
            return menu;
        }

        private static MenuItem RowMenuEntry(string header, RoutedEventHandler click, ToolItem item, string glyph = null, string gesture = null)
        {
            var entry = new MenuItem { Header = header, Tag = item, InputGestureText = gesture ?? "" };
            if (glyph != null)
                entry.Icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 13 };
            entry.Click += click;
            return entry;
        }

        private void OnCopyPath(object sender, RoutedEventArgs e)
        {
            var tool = ItemOf(sender).Tool;
            var text = tool.IsCommand ? tool.Command : tool.ResolvedPath;
            try
            {
                Clipboard.SetText(text ?? "");
                StatusText.Text = "Đã sao chép: " + text;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard đang bị app khác giữ: báo nhẹ, không phải lỗi của người dùng.
                Dialogs.Error(Host, "Không sao chép được vì clipboard đang bận. Thử lại.");
            }
        }

        private void OnRemoveOne(object sender, RoutedEventArgs e) => RemoveTools(new List<ToolEntry> { ItemOf(sender).Tool });

        /// <summary>Del trên dòng đang chọn: xoá tool đó khỏi danh sách (có Hoàn tác).</summary>
        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && e.OriginalSource is ListBoxItem && ToolList.SelectedItem is ToolItem item)
            {
                RemoveTools(new List<ToolEntry> { item.Tool });
                e.Handled = true;
            }
            else if (e.Key == Key.Space && e.OriginalSource is ListBoxItem && ToolList.SelectedItem is ToolItem selected)
            {
                selected.IsChecked = !selected.IsChecked;
                e.Handled = true;
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

            var share = new ShareWindow(ShareSummary.Header(tools.Count, 0, 0, 0), previews.Select(ShareSummary.ToolLine)
                .Concat(new[] { "", "Tổng: " + ShareSummary.Format(previews.Sum(p => p.TotalBytes)) }), ShareSummary.Warnings(previews)) { Owner = Host };
            if (share.ShowDialog() != true)
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

        /// <summary>Nhận gói xong: workspace đã nhận profile, workflow, lệnh riêng (có thể vừa được tạo). Cửa sổ chính mở nó để thấy khung "Chưa sẵn sàng".</summary>
        public event Action<string> WorkspaceImported;

        private static List<Workspace> QuietWorkspaces()
        {
            try { return WorkspaceStore.Peek(WorkspaceStore.DefaultPath).Workspaces.ToList(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return new List<Workspace>(); }
        }

        /// <summary>
        /// Gói có phần workspace: tool vào thư viện, còn profile, workflow, lệnh riêng vào workspace đã chọn (hoặc workspace mới tạo từ thư mục).
        /// Xong thì mở workspace đó, nơi khung "Chưa sẵn sàng" nêu thứ máy này còn thiếu.
        /// </summary>
        private async Task ImportIntoWorkspace(string zipPath, ImportWindow dialog)
        {
            WorkspaceImportResult result = null;
            string workspaceId = null;
            try
            {
                await RunBusy(() =>
                {
                    WorkspaceStore.Update(WorkspaceStore.DefaultPath, store =>
                    {
                        Workspace target;
                        if (dialog.TargetWorkspaceId != null)
                            target = store.Find(dialog.TargetWorkspaceId) ?? throw new InvalidDataException("Workspace nhận đã bị gỡ khỏi Toolbox.");
                        else
                            target = store.Add(new Workspace { Name = Path.GetFileName(dialog.NewWorkspaceFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)), Path = dialog.NewWorkspaceFolder });
                        result = PackageImporter.ImportIntoWorkspace(_store, target, zipPath, dialog.Destination, dialog.Choices);
                        workspaceId = target.Id;
                        return true;
                    });
                    return 0;
                });
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(Host, "Không nhập được gói: " + ex.Message);
                return;
            }

            if (!Save())
                return;
            Reload();
            Dialogs.Success("Đã nhận " + ShareSummary.Counts(result.Tools.Count, result.ProfilesAdded, result.StacksAdded, result.CustomsAdded)
                + " từ " + Path.GetFileName(zipPath) + ". Kiểm xem máy này còn thiếu gì.", seconds: 8);
            WorkspaceImported?.Invoke(workspaceId);
        }


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

        /// <summary>Mở màn nhập một file gói (double-click file .toolbox khi Toolbox đang chạy).</summary>
        public void OpenPackage(string zipPath)
        {
            if (_store != null)
                ImportPackage(zipPath);
        }

        private async void ImportPackage(string zipPath)
        {
            PackManifest manifest;
            try
            {
                manifest = Packer.ReadManifest(zipPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Dialogs.Error(Host, "Không mở được gói: " + ex.Message);
                return;
            }

            var dialog = new ImportWindow(manifest, _store, QuietWorkspaces()) { Owner = Host };
            if (dialog.ShowDialog() != true)
                return;
            if (dialog.HasWorkspaceContent)
            {
                await ImportIntoWorkspace(zipPath, dialog);
                return;
            }

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
        public void OpenAgentWindow() => OpenAgentWindowFor(null);

        /// <summary>Mở cửa sổ Kết nối agent cho một workspace (thư mục); null = cửa sổ chung.</summary>
        public void OpenAgentWindowFor(string workspacePath)
        {
            var window = new AgentWindow(_installer, workspacePath) { Owner = Host };
            window.ShowDialog();
            if (window.InstalledNow)
                InstallBanner.Visibility = Visibility.Collapsed;
        }

        /// <summary>F5: chạy tool đang chọn trong danh sách. false nếu không có tool nào chạy được đang chọn.</summary>
        public bool RunSelected()
        {
            if (!(ToolList.SelectedItem is ToolItem item) || item.CanInstall)
                return false;
            Run(item);
            return true;
        }

        /// <summary>Shift+F5: dừng tool đang chọn nếu nó đang chạy.</summary>
        public bool StopSelected()
        {
            if (!(ToolList.SelectedItem is ToolItem item) || !item.IsRunning)
                return false;
            _runs.Stop(item.Tool.Id);
            StatusText.Text = $"Đang dừng \"{item.Tool.Name}\"…";
            return true;
        }

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
