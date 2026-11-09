using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>
    /// Bảng hậu tố của một tool: mọi thứ nối được vào sau lệnh gốc, gồm lệnh con (<c>serve</c>) và cờ (<c>--port</c>).
    /// Không có danh sách ghi sẵn trong code: hậu tố đến từ trợ giúp của tool (Quét lại) hoặc từ bạn (Thêm).
    /// Bấm một lệnh con để xem và quét cờ riêng của nó. Đánh dấu « Cổng » ở cờ đặt cổng của lệnh con đó.
    /// Mỗi thay đổi lưu ngay vào <c>capabilities\&lt;kind&gt;.json</c> rồi gọi <paramref name="changed"/> để tab làm mới.
    /// </summary>
    public partial class CapabilitiesWindow : Window
    {
        private readonly ToolDef _tool;
        private readonly string _cwd;
        private readonly string _directory;
        private readonly Action _changed;
        private readonly HashSet<string> _open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _scanning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ToolCapabilities _data;

        public CapabilitiesWindow(ToolDef tool, string workingDirectory, Action changed)
        {
            InitializeComponent();
            _tool = tool;
            _cwd = workingDirectory;
            _changed = changed;
            _directory = CapabilityStore.DefaultDirectory;
            Search.TextChanged += OnSearchChanged;
            TitleText.Text = "Khả năng · " + tool.Name + (tool.Command == null ? "" : " · " + tool.Command);
            AddLabel.Text = tool.Command == null ? "" : "Thêm hậu tố sau " + tool.Command;

            try
            {
                _data = CapabilityStore.Load(_directory, tool.Kind);
            }
            catch (InvalidDataException ex)
            {
                StatusText.Text = ex.Message + " Sửa hoặc xoá file đó rồi mở lại.";
            }

            if (_data == null || tool.Command == null)
            {
                if (_data != null)
                    StatusText.Text = "Lệnh của tool này lấy từ file (ví dụ script trong package.json), không có gì để quét.";
                RescanButton.IsEnabled = false;
                ImportButton.IsEnabled = false;
                NameBox.IsEnabled = false;
                TakesBox.IsEnabled = false;
                DescBox.IsEnabled = false;
            }
            RenderRows();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void RenderRows()
        {
            RowsPanel.Children.Clear();
            if (_data == null)
            {
                RescanButton.Visibility = Visibility.Collapsed;
                return;
            }

            RescanButton.Visibility = _tool.Command == null ? Visibility.Collapsed : Visibility.Visible;
            RescanButton.IsEnabled = _tool.Command != null && !_scanning.Contains("");
            if (_tool.Command != null)
                StatusText.Text = StatusFor("");

            var entries = EntriesOf("");
            if (entries.Count == 0)
            {
                RowsPanel.Children.Add(Note(_tool.Command == null
                    ? "Không có hậu tố nào."
                    : "Chưa có hậu tố nào. Bấm Quét lại để đọc từ trợ giúp của tool, hoặc thêm tay ở chân cửa sổ."));
                return;
            }

            // Có chữ tìm: lệnh hiện nếu chính nó hoặc cờ riêng của nó khớp; lệnh chỉ khớp qua cờ thì tự mở để thấy cờ đó.
            var query = Core.Text.Fold((Search.Text ?? "").Trim());
            var shown = 0;
            RowsPanel.Children.Add(HeaderRow());
            foreach (var entry in entries)
            {
                var self = Matches(entry, query);
                var viaFlag = !self && !CapabilityStore.IsFlag(entry.Name) && EntriesOf(entry.Name).Any(f => Matches(f, query));
                if (!self && !viaFlag)
                    continue;
                shown++;
                RowsPanel.Children.Add(EntryRow("", entry));
                if (!CapabilityStore.IsFlag(entry.Name) && (_open.Contains(entry.Name) || viaFlag))
                    RowsPanel.Children.Add(SubcommandBlock(entry, self ? "" : query));
            }
            if (shown == 0)
                RowsPanel.Children.Add(Note("Không có mục nào khớp \"" + Search.Text.Trim() + "\"."));
        }

        private static bool Matches(CapabilityEntry entry, string foldedQuery) =>
            foldedQuery.Length == 0 || Core.Text.Fold((entry.Name ?? "") + " " + (entry.Description ?? "")).Contains(foldedQuery);

        private void OnSearchChanged(object sender, EventArgs e)
        {
            if (_data != null)
                RenderRows();
        }

        /// <summary>Hậu tố của một phạm vi: lệnh con trước, cờ sau.</summary>
        private List<CapabilityEntry> EntriesOf(string scope) =>
            _data.Entries
                .Where(en => string.Equals(en.Action ?? "", scope, StringComparison.OrdinalIgnoreCase))
                .OrderBy(en => CapabilityStore.IsFlag(en.Name) ? 1 : 0)
                .ThenBy(en => en.Source == "scan" ? 0 : 1)
                .ThenBy(en => en.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private UIElement HeaderRow()
        {
            var grid = Grid5();
            grid.Margin = new Thickness(0, 10, 0, 4);
            Place(grid, Caption("Cổng"), 0);
            Place(grid, Caption("Hậu tố"), 1);
            Place(grid, Caption("Giá trị"), 2);
            Place(grid, Caption("Mô tả"), 3);
            return grid;
        }

        private UIElement EntryRow(string scope, CapabilityEntry entry)
        {
            var isFlag = CapabilityStore.IsFlag(entry.Name);
            var grid = Grid5();
            grid.Margin = new Thickness(0, 6, 0, 6);

            if (isFlag)
            {
                var port = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = scope.Length == 0 ? "Cờ này đặt cổng cho cả tool." : "Cờ này đặt cổng cho " + scope + ". Mỗi lệnh chỉ có một cờ cổng." };
                port.IsChecked = entry.Role == CapabilityStore.PortRole;
                port.Checked += (s, e) => Persist(() => CapabilityStore.SetRole(_data, scope, entry.Name, CapabilityStore.PortRole));
                port.Unchecked += (s, e) => Persist(() => CapabilityStore.SetRole(_data, scope, entry.Name, ""));
                Place(grid, port, 0);

                var takes = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Cờ có đi kèm giá trị không. Đoán từ trợ giúp; sửa nếu sai." };
                takes.IsChecked = entry.TakesValue;
                takes.Checked += (s, e) => Persist(() => entry.TakesValue = true);
                takes.Unchecked += (s, e) => Persist(() => entry.TakesValue = false);
                Place(grid, takes, 2);
            }

            var name = new TextBlock { Text = entry.Name, FontFamily = new FontFamily("Consolas"), FontSize = 13,
                FontWeight = isFlag ? FontWeights.Normal : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (entry.Source == "manual")
                name.ToolTip = "Nhập tay";
            Place(grid, name, 1);

            var description = new TextBlock
            {
                Text = (entry.Source == "manual" ? "[tay] " : "") + (entry.Description ?? ""),
                Foreground = (Brush)FindResource("Muted"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.IsNullOrEmpty(entry.Excerpt) ? entry.Description : entry.Excerpt,
            };
            Place(grid, description, 3);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (!isFlag)
            {
                var isOpen = _open.Contains(entry.Name);
                var flags = new Button { Content = isOpen ? "Cờ ▾" : "Cờ ▸", Style = (Style)FindResource("GhostButton"),
                    Padding = new Thickness(10, 2, 10, 2), ToolTip = "Xem và quét cờ của " + entry.Name };
                flags.Click += (s, e) =>
                {
                    if (!_open.Remove(entry.Name))
                        _open.Add(entry.Name);
                    RenderRows();
                };
                buttons.Children.Add(flags);
            }
            var remove = new Button { Content = "Xoá", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Bỏ hậu tố này khỏi bảng" };
            remove.Click += (s, e) => Persist(() =>
            {
                CapabilityStore.Remove(_data, scope, entry.Name);
                if (!isFlag)
                {
                    // Lệnh con bị bỏ thì cờ riêng và lần quét của nó cũng không còn nghĩa.
                    _data.Entries.RemoveAll(x => string.Equals(x.Action, entry.Name, StringComparison.OrdinalIgnoreCase));
                    _data.Scans.RemoveAll(x => string.Equals(x.Action, entry.Name, StringComparison.OrdinalIgnoreCase));
                    _open.Remove(entry.Name);
                }
            });
            buttons.Children.Add(remove);
            Place(grid, buttons, 4);

            return new Border
            {
                BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
            };
        }

        /// <summary>Cờ riêng của một lệnh con: trạng thái quét, nút quét, các cờ, và chỗ thêm cờ tay.</summary>
        private UIElement SubcommandBlock(CapabilityEntry sub, string flagQuery = "")
        {
            var scope = sub.Name;
            var panel = new StackPanel { Margin = new Thickness(28, 4, 0, 10) };

            var head = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var scan = new Button { Content = "Quét cờ", MinWidth = 90, Style = (Style)FindResource("GhostButton"),
                IsEnabled = !_scanning.Contains(scope), ToolTip = "Chạy " + _tool.HelpCommand(scope) };
            scan.Click += async (s, e) => await ScanAsync(scope);
            DockPanel.SetDock(scan, Dock.Right);
            head.Children.Add(scan);
            head.Children.Add(new TextBlock { Text = StatusFor(scope), Foreground = (Brush)FindResource("Muted"), FontSize = 12,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(head);

            var flags = EntriesOf(scope).Where(f => Matches(f, flagQuery)).ToList();
            if (flags.Count == 0)
                panel.Children.Add(Note("Chưa có cờ nào của " + scope + "."));
            foreach (var flag in flags)
                panel.Children.Add(EntryRow(scope, flag));

            var add = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var addButton = new Button { Content = "Thêm", MinWidth = 80, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(addButton, Dock.Right);
            var takes = new CheckBox { Content = "Có giá trị", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(takes, Dock.Right);
            var name = new TextBox { FontFamily = new FontFamily("Consolas"), ToolTip = "Cờ của " + scope + ", ví dụ --port" };
            addButton.Click += (s, e) =>
            {
                try
                {
                    CapabilityStore.AddManual(_data, scope, name.Text, takes.IsChecked == true, null);
                }
                catch (ArgumentException ex)
                {
                    Dialogs.Error(this, ex.Message);
                    return;
                }
                Persist(null);
            };
            add.Children.Add(addButton);
            add.Children.Add(takes);
            add.Children.Add(name);
            panel.Children.Add(add);
            return panel;
        }

        private async Task ScanAsync(string scope)
        {
            var command = _tool.HelpCommand(scope);
            if (command == null || _data == null || !_scanning.Add(scope))
                return;
            RenderRows();
            var outcome = await Task.Run(() => CapabilityScanner.Run(command, _cwd, CapabilityScanner.DefaultTimeoutMs, includeSubcommands: scope.Length == 0));
            _scanning.Remove(scope);
            try
            {
                var fresh = CapabilityStore.Load(_directory, _tool.Kind);
                CapabilityStore.ApplyScan(fresh, scope, outcome, DateTime.Now);
                CapabilityStore.Save(_directory, fresh);
                _data = fresh;
            }
            catch (InvalidDataException ex)
            {
                Dialogs.Error(this, ex.Message);
            }
            _changed?.Invoke();
            RenderRows();
        }

        private async void OnRescan(object sender, RoutedEventArgs e) => await ScanAsync("");

        /// <summary>Nhập lệnh và cờ từ file hoặc danh sách dán; mục đã có thì bỏ qua, mục nhập được giữ qua các lần quét lại.</summary>
        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (_data == null)
                return;
            var dialog = new CapabilityImportWindow(_cwd) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            var added = 0;
            Persist(() =>
            {
                foreach (var item in dialog.Imported)
                    if (CapabilityStore.AddImported(_data, item.Action ?? "", item.Name, item.TakesValue, item.Description))
                        added++;
            });
            StatusText.Text = $"Đã nhập {added} mục" + (dialog.Imported.Count > added ? $", {dialog.Imported.Count - added} mục đã có nên bỏ qua." : ".");
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (_data == null)
                return;
            try
            {
                CapabilityStore.AddManual(_data, "", NameBox.Text, TakesBox.IsChecked == true, DescBox.Text);
            }
            catch (ArgumentException ex)
            {
                Dialogs.Error(this, ex.Message);
                return;
            }
            NameBox.Clear();
            DescBox.Clear();
            TakesBox.IsChecked = false;
            Persist(null);
        }

        /// <summary>Áp một thay đổi (nếu có), lưu bảng, báo tab, vẽ lại. Lỗi ghi file hiện cho bạn.</summary>
        private void Persist(Action change)
        {
            if (_data == null)
                return;
            change?.Invoke();
            try
            {
                CapabilityStore.Save(_directory, _data);
            }
            catch (IOException ex)
            {
                Dialogs.Error(this, "Không ghi được bảng khả năng: " + ex.Message);
            }
            _changed?.Invoke();
            RenderRows();
        }

        private string StatusFor(string scope)
        {
            if (_scanning.Contains(scope))
                return "Đang quét…";
            var record = CapabilityStore.LastScan(_data, scope);
            if (record == null)
                return "Chưa quét. Bấm Quét để đọc từ " + _tool.HelpCommand(scope) + ".";
            if (record.Error != null)
                return "Lần quét gần nhất lỗi: " + record.Error;
            var when = DateTime.TryParse(record.ScannedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) : record.ScannedAt;
            return $"Quét lúc {when} · lệnh {record.Command}";
        }

        private static Grid Grid5()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        private static void Place(Grid grid, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private TextBlock Caption(string text) => new TextBlock
        {
            Text = text, FontSize = 11, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center,
        };

        private TextBlock Note(string text) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
            Foreground = (Brush)FindResource("Muted"), FontSize = 12,
        };
    }
}
