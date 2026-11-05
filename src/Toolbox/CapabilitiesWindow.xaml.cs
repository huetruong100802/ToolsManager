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
    /// Bảng khả năng của một tool. Chọn lệnh ở đầu, mỗi cờ một dòng: đánh dấu « Cổng » ở cờ đặt cổng, công tắc « Nhận giá trị »,
    /// xoá. Quét lại đọc trợ giúp của tool. Thêm cờ tay ở chân. Mỗi thay đổi lưu ngay vào <c>capabilities\&lt;kind&gt;.json</c>
    /// rồi gọi <paramref name="changed"/> để tab làm mới.
    /// </summary>
    public partial class CapabilitiesWindow : Window
    {
        private readonly ToolDef _tool;
        private readonly string _cwd;
        private readonly string _directory;
        private readonly Action _changed;
        private ToolCapabilities _data;

        public CapabilitiesWindow(ToolDef tool, string workingDirectory, Action changed)
        {
            InitializeComponent();
            _tool = tool;
            _cwd = workingDirectory;
            _changed = changed;
            _directory = CapabilityStore.DefaultDirectory;
            TitleText.Text = "Khả năng · " + tool.Name;

            try
            {
                _data = CapabilityStore.Load(_directory, tool.Kind);
            }
            catch (InvalidDataException ex)
            {
                StatusText.Text = ex.Message + " Sửa hoặc xoá file đó rồi mở lại.";
            }

            if (tool.Actions.Count == 0)
            {
                StatusText.Text = "Lệnh của tool này lấy từ file (ví dụ script trong package.json), không có gì để quét.";
                ActionPicker.IsEnabled = false;
            }
            else
            {
                ActionPicker.ItemsSource = tool.Actions;
                ActionPicker.SelectedIndex = 0;
            }
            if (_data == null)
            {
                RescanButton.IsEnabled = false;
                NameBox.IsEnabled = false;
                TakesBox.IsEnabled = false;
                DescBox.IsEnabled = false;
            }
            RenderRows();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private ActionDef Chosen => ActionPicker.SelectedItem as ActionDef;

        private ScanDef ScanFor(ActionDef action) =>
            _tool.Scans.FirstOrDefault(s => string.Equals(s.ActionId, action.Id, StringComparison.OrdinalIgnoreCase));

        private void OnActionChanged(object sender, SelectionChangedEventArgs e) => RenderRows();

        private void RenderRows()
        {
            RowsPanel.Children.Clear();
            var action = Chosen;
            if (action == null || _data == null)
            {
                RescanButton.Visibility = Visibility.Collapsed;
                return;
            }

            var scan = ScanFor(action);
            RescanButton.Visibility = scan == null ? Visibility.Collapsed : Visibility.Visible;
            RescanButton.IsEnabled = scan != null;
            StatusText.Text = StatusFor(action, scan);

            var entries = _data.Entries
                .Where(en => string.Equals(en.Action, action.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(en => en.Source == "scan" ? 0 : 1)
                .ThenBy(en => en.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (entries.Count == 0)
            {
                RowsPanel.Children.Add(Note(scan != null
                    ? "Chưa có cờ nào. Bấm Quét lại, hoặc thêm cờ tay ở chân cửa sổ."
                    : "Chưa có cờ nào. Thêm cờ tay ở chân cửa sổ."));
                return;
            }

            RowsPanel.Children.Add(HeaderRow());
            foreach (var entry in entries)
                RowsPanel.Children.Add(EntryRow(action, entry));
        }

        private UIElement HeaderRow()
        {
            var grid = Grid4();
            grid.Margin = new Thickness(0, 10, 0, 4);
            Place(grid, Caption("Cổng"), 0);
            Place(grid, Caption("Cờ"), 1);
            Place(grid, Caption("Giá trị"), 2);
            Place(grid, Caption("Mô tả"), 3);
            return grid;
        }

        private UIElement EntryRow(ActionDef action, CapabilityEntry entry)
        {
            var grid = Grid4();
            grid.Margin = new Thickness(0, 6, 0, 6);

            var port = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Cờ này đặt cổng. Mỗi lệnh chỉ có một cờ cổng." };
            port.IsChecked = entry.Role == CapabilityStore.PortRole;
            port.Checked += (s, e) => Persist(() => CapabilityStore.SetRole(_data, action.Id, entry.Name, CapabilityStore.PortRole));
            port.Unchecked += (s, e) => Persist(() => CapabilityStore.SetRole(_data, action.Id, entry.Name, ""));
            Place(grid, port, 0);

            var name = new TextBlock { Text = entry.Name, FontFamily = new FontFamily("Consolas"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center };
            if (entry.Source == "manual")
                name.ToolTip = "Nhập tay";
            Place(grid, name, 1);

            var takes = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Cờ có đi kèm giá trị không. Đoán từ trợ giúp; sửa nếu sai." };
            takes.IsChecked = entry.TakesValue;
            takes.Checked += (s, e) => Persist(() => entry.TakesValue = true);
            takes.Unchecked += (s, e) => Persist(() => entry.TakesValue = false);
            Place(grid, takes, 2);

            var description = new TextBlock
            {
                Text = (entry.Source == "manual" ? "[tay] " : "") + (entry.Description ?? ""),
                Foreground = (Brush)FindResource("Muted"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.IsNullOrEmpty(entry.Excerpt) ? entry.Description : entry.Excerpt,
            };
            Place(grid, description, 3);

            var remove = new Button { Content = "Xoá", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Bỏ cờ này khỏi bảng" };
            remove.Click += (s, e) => Persist(() => CapabilityStore.Remove(_data, action.Id, entry.Name));
            Place(grid, remove, 4);

            return new Border
            {
                BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
            };
        }

        private async void OnRescan(object sender, RoutedEventArgs e)
        {
            var action = Chosen;
            var scan = action == null ? null : ScanFor(action);
            if (action == null || scan == null || _data == null)
                return;

            RescanButton.IsEnabled = false;
            StatusText.Text = "Đang quét…";
            var outcome = await Task.Run(() => CapabilityScanner.Run(scan.Command, _cwd));
            try
            {
                var fresh = CapabilityStore.Load(_directory, _tool.Kind);
                CapabilityStore.ApplyScan(fresh, action.Id, outcome, DateTime.Now);
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

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            var action = Chosen;
            if (action == null || _data == null)
                return;
            try
            {
                CapabilityStore.AddManual(_data, action.Id, NameBox.Text, TakesBox.IsChecked == true, DescBox.Text);
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

        private string StatusFor(ActionDef action, ScanDef scan)
        {
            if (scan == null)
                return "Lệnh này không có trợ giúp để quét; thêm cờ tay.";
            var record = CapabilityStore.LastScan(_data, action.Id);
            if (record == null)
                return "Chưa quét. Bấm Quét lại để đọc cờ từ trợ giúp.";
            if (record.Error != null)
                return "Lần quét gần nhất lỗi: " + record.Error;
            var when = DateTime.TryParse(record.ScannedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) : record.ScannedAt;
            return $"Quét lúc {when} · lệnh {record.Command}";
        }

        private static Grid Grid4()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
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
