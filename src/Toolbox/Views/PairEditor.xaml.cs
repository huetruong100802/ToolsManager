using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Một dòng của trình sửa cặp: key (gợi ý từ bảng khả năng), value, cờ hỏi lúc chạy.</summary>
    public sealed class PairRow : INotifyPropertyChanged
    {
        private readonly PairEditor _owner;
        private string _key = "";
        private string _value = "";
        private bool _ask;

        internal PairRow(PairEditor owner, ParamPair pair)
        {
            _owner = owner;
            _key = pair?.Key ?? "";
            _value = pair?.Value ?? "";
            _ask = pair?.Ask ?? false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IReadOnlyList<string> Names => _owner.Names;

        public string Key
        {
            get => _key;
            set
            {
                value = value ?? "";
                if (_key == value)
                    return;
                _key = value;
                Raise(nameof(Key));
                Raise(nameof(ValueVisibility));
                Raise(nameof(Hint));
                Raise(nameof(ValueHint));
                _owner.NotifyChanged();
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                value = value ?? "";
                if (_value == value)
                    return;
                _value = value;
                Raise(nameof(Value));
                _owner.NotifyChanged();
            }
        }

        public bool Ask
        {
            get => _ask;
            set
            {
                if (_ask == value)
                    return;
                _ask = value;
                Raise(nameof(Ask));
                Raise(nameof(ValueEnabled));
                _owner.NotifyChanged();
            }
        }

        /// <summary>Cờ đã biết là công tắc (không nhận giá trị) thì ẩn ô giá trị; key chưa biết thì vẫn cho nhập.</summary>
        public Visibility ValueVisibility
        {
            get
            {
                var key = (_key ?? "").Trim();
                if (PairLine.IsSeparator(key))
                    return Visibility.Collapsed;
                var entry = _owner.Find(key);
                return entry != null && !entry.TakesValue && (_value ?? "").Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public bool ValueEnabled => !_ask;

        /// <summary>Hiện nút ▾ khi dòng có gợi ý giá trị: lệnh con/giá trị cho phép/đường dẫn (key trống) hoặc giá trị cho phép của cờ.</summary>
        public Visibility OptionsVisibility
        {
            get
            {
                var key = (_key ?? "").Trim();
                if (key.Length == 0)
                    return _owner.ChoiceFor(this).Any ? Visibility.Visible : Visibility.Collapsed;
                var choices = _owner.Find(key)?.Choices;
                return choices != null && choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        internal void RefreshOptions() => Raise(nameof(OptionsVisibility));

        public string Hint
        {
            get
            {
                var key = (_key ?? "").Trim();
                if (key.Length == 0)
                    return "Để trống key: giá trị là đối số vị trí (vd. all, tên migration).";
                var entry = _owner.Find(key);
                if (entry == null)
                    return PairLine.IsSeparator(key) ? "Dấu tách: các cặp sau thuộc về app đang chạy." : "Key tự gõ (chưa có trong bảng quét).";
                return string.IsNullOrWhiteSpace(entry.Description) ? entry.Name : entry.Name + " — " + entry.Description;
            }
        }

        public string ValueHint
        {
            get
            {
                var entry = _owner.Find((_key ?? "").Trim());
                return entry != null && entry.TakesValue ? "Giá trị của " + entry.Name : "Giá trị (bỏ trống nếu là công tắc)";
            }
        }

        public ParamPair ToPair() => new ParamPair((_key ?? "").Trim(), (_value ?? "").Trim(), _ask);

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Trình sửa danh sách cặp tham số: dòng <c>[key ▾] [giá trị] [Hỏi] ▲ ▼ ✕</c> và nút ＋ để thêm.
    /// Gợi ý key lấy từ bảng khả năng (quét --help) của action; vẫn gõ tự do được. Không biết tên tool nào.
    /// </summary>
    public partial class PairEditor : UserControl
    {
        private readonly ObservableCollection<PairRow> _rows = new ObservableCollection<PairRow>();
        private IReadOnlyList<CapabilityEntry> _suggestions = new CapabilityEntry[0];
        private List<string> _names = new List<string>();
        private bool _loading;

        public PairEditor()
        {
            InitializeComponent();
            Rows.ItemsSource = _rows;
        }

        /// <summary>Báo mỗi khi nội dung đổi (thêm, xoá, sửa, đổi thứ tự).</summary>
        public event EventHandler Changed;

        internal IReadOnlyList<string> Names => _names;

        private ToolCapabilities _caps;
        private string _actionId = "";
        private string _workingDirectory = "";

        /// <summary>
        /// Nguồn gợi ý cho dòng không có key: bảng khả năng của tool (lệnh con và mục "Arguments:" đã quét), id action của thẻ
        /// và thư mục chạy (để liệt kê file). Không đặt thì dòng key trống không có nút gợi ý.
        /// </summary>
        public void SetPositionalSource(ToolCapabilities data, string actionId, string workingDirectory)
        {
            _caps = data;
            _actionId = actionId ?? "";
            _workingDirectory = workingDirectory ?? "";
            foreach (var row in _rows)
                row.RefreshOptions();
        }

        internal PositionalChoice ChoiceFor(PairRow row)
        {
            var index = _rows.IndexOf(row);
            if (_caps == null || index < 0)
                return PositionalChoice.None;
            var before = _rows.Take(index).Select(r => r.ToPair()).Where(p => p.Key.Length > 0 || p.Value.Length > 0 || p.Ask);
            return PositionalOptions.Resolve(_caps, _actionId, before);
        }

        private void OnOptions(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            var key = row.Key.Trim();
            var values = new List<string>();
            var path = false;
            var directoryOnly = false;
            if (key.Length == 0)
            {
                var choice = ChoiceFor(row);
                values.AddRange(choice.Values);
                path = choice.IsPath;
                directoryOnly = choice.DirectoryOnly;
            }
            else if (Find(key)?.Choices != null)
            {
                values.AddRange(Find(key).Choices);
            }
            if (path)
                values.AddRange(PositionalOptions.PathCandidates(_workingDirectory, directoryOnly));

            Func<string> browse = path && !directoryOnly ? (Func<string>)BrowseFile : null;
            ParamOptionMenu.Open((Button)sender, values.Distinct().ToList(), picked => row.Value = picked, browse);
        }

        private string BrowseFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (!string.IsNullOrEmpty(_workingDirectory) && System.IO.Directory.Exists(_workingDirectory))
                dialog.InitialDirectory = _workingDirectory;
            return dialog.ShowDialog() == true
                ? (string.IsNullOrEmpty(_workingDirectory) ? dialog.FileName : PositionalOptions.Relative(_workingDirectory, dialog.FileName))
                : null;
        }

        /// <summary>Gợi ý key (cờ) của action đang chọn. Đặt lại thì danh sách thả xuống của mọi dòng đổi theo.</summary>
        public void SetSuggestions(IEnumerable<CapabilityEntry> entries)
        {
            _suggestions = (entries ?? Enumerable.Empty<CapabilityEntry>())
                .Where(e => e != null && CapabilityStore.IsFlag(e.Name))
                .GroupBy(e => e.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();
            _names = _suggestions.Select(e => e.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            Reload(GetPairs());
        }

        /// <summary>Cờ đã quét của một action (bảng khả năng của tool); rỗng nếu chưa quét hoặc file hỏng.</summary>
        public static IReadOnlyList<CapabilityEntry> SuggestionsFor(string toolKind, string actionId) =>
            SuggestionsFor(toolKind, actionId, out _);

        /// <summary>
        /// Như trên, nhưng nếu action chưa quét thì dùng cờ chung của tool (đã quét ở lệnh gốc) để lệnh nào cũng có gợi ý.
        /// Action đã có mục riêng thì mục riêng ghi đè hoàn toàn cờ chung.
        /// <paramref name="hasOwn"/>: action đã có mục quét/nhập riêng.
        /// </summary>
        public static IReadOnlyList<CapabilityEntry> SuggestionsFor(string toolKind, string actionId, out bool hasOwn)
        {
            hasOwn = false;
            try
            {
                var data = CapabilityStore.Load(CapabilityStore.DefaultDirectory, toolKind);
                bool Is(CapabilityEntry en, string a) => string.Equals(en.Action ?? "", a, StringComparison.OrdinalIgnoreCase);
                var own = data.Entries.Where(en => Is(en, actionId ?? "")).ToList();
                hasOwn = own.Count > 0;
                // Đã quét đúng lệnh này thì dùng riêng nó, không trộn cờ chung (cờ chung như --info/--version không hợp lệ ở lệnh con).
                if (hasOwn || string.IsNullOrEmpty(actionId))
                    return own;
                return own.Concat(data.Entries.Where(en => Is(en, "") && CapabilityStore.IsFlag(en.Name))).ToList();
            }
            catch (System.IO.InvalidDataException)
            {
                return new CapabilityEntry[0];
            }
        }

        /// <summary>Bảng khả năng của tool để tính gợi ý đối số vị trí; null nếu file hỏng.</summary>
        public static ToolCapabilities LoadCapabilities(string toolKind)
        {
            try
            {
                return CapabilityStore.Load(CapabilityStore.DefaultDirectory, toolKind);
            }
            catch (System.IO.InvalidDataException)
            {
                return null;
            }
        }

        /// <summary>Báo người dùng muốn quét help của lệnh này (nút "Quét gợi ý").</summary>
        public event EventHandler ScanRequested;

        /// <summary>Hiện nút quét khi tool có lệnh trợ giúp; <paramref name="hasOwn"/> đã có dữ liệu thì nút đổi thành quét lại.</summary>
        public void EnableScan(bool available, bool hasOwn)
        {
            ScanButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            ScanButton.Content = hasOwn ? "Quét lại gợi ý" : "Quét gợi ý";
        }

        public void SetScanning(bool scanning, string message)
        {
            ScanButton.IsEnabled = !scanning;
            ScanStatus.Text = scanning ? "Đang chạy lệnh trợ giúp…" : message ?? "";
            ScanStatus.Visibility = ScanStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnScan(object sender, RoutedEventArgs e) => ScanRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Dropdown key có tìm kiếm: gõ thì danh sách thu lại theo tên hoặc mô tả (khớp bất kỳ đâu, không phân biệt hoa thường/dấu).
        /// Mỗi ô có view riêng vì danh sách tên dùng chung cho mọi dòng. Đóng dropdown thì bỏ lọc để lần mở sau thấy đủ.
        /// </summary>
        private void OnKeyComboLoaded(object sender, RoutedEventArgs e)
        {
            var combo = (ComboBox)sender;
            if (combo.Tag != null || !(combo.ItemsSource is IEnumerable<string> names))
                return;
            var view = new System.Windows.Data.ListCollectionView(names.ToList());
            combo.Tag = view;
            // Đổi ItemsSource sang một view có mục hiện hành (mục đầu) sẽ làm ô tự chọn mục đó, rồi Text ghi ngược vào Key
            // của dòng và ghi đè dữ liệu. Tắt đồng bộ mục hiện hành, bỏ mục hiện hành, và trả lại đúng chữ vốn có.
            var original = combo.Text;
            combo.IsSynchronizedWithCurrentItem = false;
            view.MoveCurrentTo(null);
            combo.ItemsSource = view;
            combo.SelectedIndex = -1;
            if (combo.Text != original)
                combo.Text = original;

            var filtering = false;
            combo.DropDownClosed += (s, a) => Apply(null);
            combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, a) =>
            {
                // Chỉ lọc khi người dùng đang gõ; chọn từ danh sách thì Text đổi theo mục nên không lọc.
                // Đọc chữ từ chính ô nhập: lúc sự kiện nổi lên, combo.Text có thể chưa kịp cập nhật.
                var typed = (a.OriginalSource as TextBox)?.Text ?? combo.Text;
                if (filtering || combo.SelectedItem != null && (string)combo.SelectedItem == typed)
                    return;
                Apply(typed);
                if (view.Count > 0 && !combo.IsDropDownOpen && combo.IsKeyboardFocusWithin)
                {
                    combo.IsDropDownOpen = true;
                    // Mở dropdown làm ô nhập bị bôi đen; trả con trỏ về cuối để gõ tiếp không ghi đè.
                    combo.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox box)
                        {
                            box.SelectionStart = box.Text.Length;
                            box.SelectionLength = 0;
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }), true);

            void Apply(string query)
            {
                var text = query ?? combo.Text;
                filtering = true;
                try
                {
                    var folded = Core.Text.Fold((query ?? "").Trim());
                    view.Filter = folded.Length == 0 ? (Predicate<object>)null : o =>
                    {
                        var name = (string)o;
                        return Core.Text.Fold(name + " " + (Find(name)?.Description ?? "")).Contains(folded);
                    };
                    view.Refresh();
                    // Lọc làm mục đang chọn biến mất có thể xoá chữ trong ô; trả lại đúng chữ người dùng đã gõ.
                    if (combo.Text != text)
                        combo.Text = text;
                }
                finally
                {
                    filtering = false;
                }
            }
        }

        private bool TakesValueOrUnknown(string key)
        {
            var entry = Find(key);
            return entry == null || entry.TakesValue;
        }

        internal CapabilityEntry Find(string key) =>
            key.Length == 0 ? null : _suggestions.FirstOrDefault(e => string.Equals(e.Name, key, StringComparison.Ordinal));

        /// <summary>Nạp danh sách cặp (thay hết).</summary>
        public void Load(IEnumerable<ParamPair> pairs) => Reload(pairs);

        private void Reload(IEnumerable<ParamPair> pairs)
        {
            var list = (pairs ?? Enumerable.Empty<ParamPair>()).ToList();
            _loading = true;
            try
            {
                _rows.Clear();
                foreach (var pair in list)
                    _rows.Add(new PairRow(this, pair));
            }
            finally
            {
                _loading = false;
            }
            NotifyChanged();
        }

        /// <summary>Các cặp hiện có, bỏ dòng trống hoàn toàn (không key, không giá trị, không hỏi).</summary>
        public List<ParamPair> GetPairs() =>
            _rows.Select(r => r.ToPair()).Where(p => p.Key.Length > 0 || p.Value.Length > 0 || p.Ask).ToList();

        internal void NotifyChanged()
        {
            if (_loading)
                return;
            var error = PairLine.Check(GetPairs());
            ErrorText.Text = error;
            ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            foreach (var row in _rows)
                row.RefreshOptions();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Lỗi hiện tại của danh sách (key sai, giá trị có ký tự cấm); null nếu hợp lệ.</summary>
        public string Error => PairLine.Check(GetPairs());

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            _rows.Add(new PairRow(this, null));
            NotifyChanged();
        }

        private static PairRow RowOf(object sender) => (PairRow)((FrameworkElement)sender).DataContext;

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            _rows.Remove(RowOf(sender));
            NotifyChanged();
        }

        private void OnUp(object sender, RoutedEventArgs e) => Move(RowOf(sender), -1);

        private void OnDown(object sender, RoutedEventArgs e) => Move(RowOf(sender), 1);

        private void Move(PairRow row, int delta)
        {
            var index = _rows.IndexOf(row);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _rows.Count)
                return;
            _rows.Move(index, target);
            NotifyChanged();
        }

        // ---- dán cả dòng ----

        private void OnPasteToggle(object sender, RoutedEventArgs e)
        {
            PastePanel.Visibility = PastePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (PastePanel.Visibility == Visibility.Visible)
                PasteBox.Focus();
        }

        private void OnPasteKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnPasteApply(sender, e);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Tách dòng dán rồi nối vào cuối danh sách. Cờ đã quét thì theo <c>TakesValue</c>; cờ không có trong bảng quét
        /// (help không liệt kê, vd. <c>--project</c> của dotnet ef) coi là có giá trị khi theo sau nó là một token không bắt đầu bằng <c>-</c>.
        /// </summary>
        private void OnPasteApply(object sender, RoutedEventArgs e)
        {
            var parsed = PairLine.Parse(PasteBox.Text, TakesValueOrUnknown);
            if (parsed.Count == 0)
                return;
            foreach (var pair in parsed)
                _rows.Add(new PairRow(this, pair));
            PasteBox.Text = "";
            PastePanel.Visibility = Visibility.Collapsed;
            NotifyChanged();
        }
    }
}
