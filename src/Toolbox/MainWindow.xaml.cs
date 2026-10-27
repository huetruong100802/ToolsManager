using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox
{
    public partial class MainWindow : Window
    {
        private const string AllTags = "(Tất cả tag)";

        private readonly ObservableCollection<ToolItem> _items = new ObservableCollection<ToolItem>();
        private readonly ICollectionView _view;
        private readonly string _fileToOpen;
        private readonly Installer _installer = new Installer();
        private ToolStore _store;
        private HashSet<ToolEntry> _matches;

        public MainWindow(string fileToOpen = null)
        {
            InitializeComponent();
            _fileToOpen = fileToOpen;
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = o => _matches == null || _matches.Contains(((ToolItem)o).Tool);
            ToolList.ItemsSource = _view;
            Loaded += OnLoaded;
            // Quay lại cửa sổ (vd sau khi cài app, xoá/di chuyển file) → cập nhật trạng thái "không tìm thấy".
            Activated += (s, e) => RefreshStatus();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _store = ToolStore.Load(ToolStore.DefaultPath);
            }
            catch (InvalidDataException ex)
            {
                // Không ghi đè file hỏng; để người dùng tự sửa hoặc xoá.
                Dialogs.Error(this, ex.Message + "\n\nHãy sửa hoặc xoá file này rồi mở lại Toolbox.");
                Close();
                return;
            }

            Reload();
            InstallBanner.Visibility = _installer.IsInstalledCopy(CurrentExe) ? Visibility.Collapsed : Visibility.Visible;
            if (_fileToOpen != null)
                ImportPackage(_fileToOpen);
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
                Dialogs.Error(this, "Không cài được: " + ex.Message + "\n\nNếu Toolbox đã cài đang mở, hãy đóng nó rồi thử lại.");
                return;
            }

            InstallBanner.Visibility = Visibility.Collapsed;
            if (Dialogs.Confirm(this, $"Đã cài vào:\n{installed}\n\nMở Toolbox từ Start Menu hoặc double-click file .toolbox.\nChuyển sang bản đã cài ngay bây giờ?"))
            {
                System.Diagnostics.Process.Start(installed)?.Dispose();
                Close();
            }
        }

        // ---------- Danh sách ----------

        private void Reload()
        {
            foreach (var item in _items)
                item.PropertyChanged -= OnItemChanged;
            _items.Clear();
            foreach (var tool in _store.Tools.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
                AddItem(tool);

            var selectedTag = TagFilter.SelectedItem as string;
            TagFilter.ItemsSource = new[] { AllTags }.Concat(_store.AllTags()).ToList();
            TagFilter.SelectedItem = TagFilter.Items.Contains(selectedTag) ? selectedTag : AllTags;

            ApplyFilter();
        }

        private void AddItem(ToolEntry tool)
        {
            var item = new ToolItem(tool);
            item.PropertyChanged += OnItemChanged;
            _items.Add(item);
        }

        private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (_store == null)
                return;
            var tag = TagFilter.SelectedItem as string;
            _matches = new HashSet<ToolEntry>(_store.Search(SearchBox.Text, tag == AllTags ? null : tag));
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
            PackButton.Content = checkedCount > 0 ? $"Đóng gói ({checkedCount})" : "Đóng gói";
            PackButton.IsEnabled = checkedCount > 0;
            RemoveButton.IsEnabled = checkedCount > 0;
            var visible = _view.Cast<ToolItem>().ToList();
            SelectAll.IsChecked = visible.Count > 0 && visible.All(i => i.IsChecked);
            EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
                Dialogs.Error(this, "Không lưu được danh sách: " + ex.Message);
                return false;
            }
        }

        // ---------- Thêm / sửa / xoá / chạy ----------

        private void OnAdd(object sender, RoutedEventArgs e) => AddTool(new ToolEntry());

        private void OnPickApp(object sender, RoutedEventArgs e)
        {
            var dialog = new AppPickerWindow(_store.Tools) { Owner = this };
            if (dialog.ShowDialog() == true)
                AddTool(ToolDefaults.FromShortcut(dialog.Selected));
        }

        private void AddTool(ToolEntry draft)
        {
            var dialog = new ToolEditWindow(draft, isNew: true) { Owner = this };
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
            var dialog = new ToolEditWindow(copy, isNew: false) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            _store.Update(copy);
            if (Save())
                Reload();
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            var items = CheckedItems();
            var names = string.Join("\n", items.Select(i => "• " + i.Tool.Name));
            if (!Dialogs.Confirm(this, $"Xoá {items.Count} tool khỏi danh sách?\n\n{names}\n\nFile của tool không bị xoá."))
                return;
            foreach (var item in items)
                _store.Remove(item.Tool.Id);
            if (Save())
                Reload();
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
            try
            {
                Runner.Start(item.Tool);
                StatusText.Text = $"Đã mở \"{item.Tool.Name}\".";
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is System.ComponentModel.Win32Exception)
            {
                Dialogs.Error(this, ex.Message);
            }
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
                Dialogs.Error(this, "Không mở được bộ cài: " + ex.Message);
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
                Dialogs.Error(this, ex.Message);
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
                Dialogs.Error(this, "Không đóng gói được: " + ex.Message);
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
            if (!Dialogs.Confirm(this, summary.ToString(), previews.Any(p => p.IsBroadFolder)))
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Lưu gói",
                Filter = "Gói Toolbox (*.toolbox)|*.toolbox",
                FileName = (tools.Count == 1 ? tools[0].Id : "toolbox-" + DateTime.Now.ToString("yyyyMMdd")) + Packer.FileExtension,
            };
            if (dialog.ShowDialog(this) != true)
                return;
            var zipPath = EnsurePackageExtension(dialog.FileName);

            try
            {
                await RunBusy(() => Packer.Pack(tools, zipPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(this, "Không đóng gói được: " + ex.Message);
                return;
            }

            StatusText.Text = $"Đã đóng gói {tools.Count} tool → {zipPath}";
            if (Dialogs.Confirm(this, $"Đã tạo gói ({Format.Size(new FileInfo(zipPath).Length)}):\n{zipPath}\n\nMở thư mục chứa gói?"))
                Runner.OpenLocation(new ToolEntry { Name = "gói", Path = zipPath });
        }

        private static string EnsurePackageExtension(string path)
        {
            return path.EndsWith(Packer.FileExtension, StringComparison.OrdinalIgnoreCase) ? path : path + Packer.FileExtension;
        }

        // ---------- Nhập gói ----------

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Chọn gói cần nhập",
                Filter = "Gói Toolbox (*.toolbox;*.zip)|*.toolbox;*.zip",
            };
            if (dialog.ShowDialog(this) == true)
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
                Dialogs.Error(this, "Không mở được gói: " + ex.Message);
                return;
            }

            var dialog = new ImportWindow(manifest, _store) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            IList<ToolEntry> imported;
            try
            {
                imported = await RunBusy(() => PackageImporter.Import(_store, zipPath, dialog.Destination, dialog.Choices));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(this, "Không nhập được gói: " + ex.Message);
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

        private void OnConnectAgent(object sender, RoutedEventArgs e)
        {
            var window = new AgentWindow(_installer) { Owner = this };
            window.ShowDialog();
            if (window.InstalledNow)
                InstallBanner.Visibility = Visibility.Collapsed;
        }

        private void OnDeskMcp(object sender, RoutedEventArgs e) => new DeskMcpWindow { Owner = this }.ShowDialog();

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
