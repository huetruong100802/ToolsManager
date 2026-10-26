using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>Chọn một app đã cài (từ shortcut Start Menu) để thêm vào danh sách.</summary>
    public partial class AppPickerWindow : Window
    {
        private List<Row> _rows;

        public AppPickerWindow(IEnumerable<ToolEntry> existing)
        {
            InitializeComponent();
            var registered = new HashSet<string>(existing.Select(t => FullPathOrSelf(t.ResolvedPath)), StringComparer.OrdinalIgnoreCase);
            Loaded += async (s, e) =>
            {
                SearchBox.Focus();
                try
                {
                    _rows = await Task.Run(() => Shortcuts.ListStartMenu()
                        .Select(i => new Row(i, registered.Contains(FullPathOrSelf(i.TargetPath))))
                        .ToList());
                }
                catch (COMException ex)
                {
                    StatusText.Text = "Không đọc được Start Menu: " + ex.Message;
                    return;
                }
                ApplyFilter();
            };
        }

        public ShortcutInfo Selected { get; private set; }

        private void OnSearchChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (_rows == null)
                return; // còn đang đọc Start Menu
            var query = Text.Fold(SearchBox.Text.Trim());
            var visible = query.Length == 0 ? _rows : _rows.Where(r => r.Haystack.Contains(query)).ToList();
            AppList.ItemsSource = visible;
            StatusText.Text = visible.Count > 0 ? "" : _rows.Count == 0 ? "Không tìm thấy app nào trong Start Menu." : "Không có app khớp.";
            StatusText.Visibility = visible.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e) => AddButton.IsEnabled = AppList.SelectedItem != null;

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Chỉ khi double-click trúng một dòng, không phải thanh cuộn.
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(AppList, source) != null)
                OnAdd(sender, e);
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (!(AppList.SelectedItem is Row row))
                return;
            Selected = row.Info;
            DialogResult = true;
        }

        private static string FullPathOrSelf(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }

        public sealed class Row
        {
            public Row(ShortcutInfo info, bool isAdded)
            {
                Info = info;
                IsAdded = isAdded;
                Haystack = Text.Fold(info.Name + " " + info.TargetPath);
                Icon = LoadIcon(info.TargetPath);
            }

            public ShortcutInfo Info { get; }
            public bool IsAdded { get; }
            public string Haystack { get; }
            public ImageSource Icon { get; }

            public Visibility AddedVisibility => IsAdded ? Visibility.Visible : Visibility.Collapsed;

            // Chạy trên thread nền nên phải Freeze để UI thread dùng được.
            private static ImageSource LoadIcon(string path)
            {
                try
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                    {
                        if (icon == null)
                            return null;
                        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        return source;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is COMException || ex is UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }
}
