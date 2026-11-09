using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>
    /// Tab Đóng gói của workspace: cây chọn bộ chạy, profile, tool thư viện, lệnh riêng; khung "Gói sẽ có"; nút Đóng gói.
    /// Mục tick tay lưu trong <see cref="_manual"/>; mục được kéo theo (vd. profile của bộ chạy) hiện đã chọn và khoá, kèm lý do.
    /// </summary>
    public partial class PackTab : UserControl
    {
        public const string TabId = ":pack";

        private sealed class Item
        {
            public string Kind;      // stack | profile | tool | custom
            public string Id;
            public string Label;
            public CheckBox Box;
        }

        private readonly WorkspacePackSelection _manual = new WorkspacePackSelection();
        private readonly List<Item> _items = new List<Item>();
        private Workspace _workspace;
        private Func<ToolStore> _library;
        private bool _updating;

        public PackTab()
        {
            InitializeComponent();
        }

        private Window Host => Window.GetWindow(this);

        /// <summary>Người dùng bấm "Nhập gói vào workspace…": WorkspaceView mở hộp thoại và nhập.</summary>
        public event Action ImportRequested;

        private void OnImport(object sender, RoutedEventArgs e) => ImportRequested?.Invoke();

        /// <param name="library">Trả kho tool thư viện (null nếu không đọc được).</param>
        public void Load(Workspace workspace, Func<ToolStore> library)
        {
            _workspace = workspace;
            _library = library;
            _store = null;
            _manual.ToolIds.Clear();
            _manual.ProfileIds.Clear();
            _manual.StackIds.Clear();
            _manual.CustomIds.Clear();
            SearchBox.Text = "";
            PackName.Text = workspace.Name;
            BuildTree();
        }

        // ---- cây chọn ----

        private void BuildTree()
        {
            _items.Clear();
            var libraryStore = SafeLibrary();
            foreach (var s in _workspace.Stacks)
                _items.Add(new Item { Kind = "stack", Id = s.Id, Label = s.Label });
            foreach (var p in _workspace.Profiles)
                _items.Add(new Item { Kind = "profile", Id = p.Id, Label = p.Label });
            // Mọi tool đã đăng ký trong thư viện đều đóng gói được, kể cả chưa gắn vào workspace này (sau gộp "một loại tool"
            // tool gắn nằm trong Bindings; LibraryBindings chỉ còn cho file cũ). Tool gắn mà thư viện không có: vẫn hiện để biết nó thiếu.
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in _workspace.Bindings)
                if (!string.IsNullOrWhiteSpace(b.ToolId) && listed.Add(b.ToolId))
                    _items.Add(new Item { Kind = "tool", Id = b.ToolId, Label = ToolLabel(libraryStore?.Find(b.ToolId), b.ToolId, bound: true) });
            if (libraryStore != null)
                foreach (var t in libraryStore.Tools.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                    if (listed.Add(t.Id))
                        _items.Add(new Item { Kind = "tool", Id = t.Id, Label = ToolLabel(t, t.Id, bound: false) });
            foreach (var c in _workspace.Customs)
                _items.Add(new Item { Kind = "custom", Id = c.Id, Label = c.Label });

            foreach (var item in _items)
            {
                var captured = item;
                item.Box = new CheckBox { Content = item.Label, Margin = new Thickness(0, 3, 0, 3) };
                item.Box.Click += (s, e) => OnToggle(captured);
            }
            Render();
            Recalculate();
        }

        private static string ToolLabel(ToolEntry tool, string id, bool bound)
        {
            var name = tool?.Name ?? ToolCatalog.Find(id)?.Name ?? id;
            var kind = tool == null ? " (không có trong thư viện)" : tool.IsCommand ? " · lệnh, chỉ gửi thông tin" : "";
            return name + kind + (bound ? "" : " · chưa gắn vào workspace");
        }

        private void Render()
        {
            Tree.Children.Clear();
            var query = Core.Text.Fold(SearchBox.Text ?? "");
            AddSection("BỘ CHẠY", "stack", query);
            AddSection("PROFILE", "profile", query);
            AddSection("TOOL THƯ VIỆN", "tool", query);
            AddSection("LỆNH RIÊNG", "custom", query);
            if (Tree.Children.Count == 0)
                Tree.Children.Add(new TextBlock
                {
                    Style = (Style)Application.Current.Resources["Hint"],
                    Text = _items.Count == 0 ? "Workspace chưa có gì để đóng gói." : "Không có mục nào khớp.",
                });
        }

        private void AddSection(string title, string kind, string query)
        {
            var shown = _items.Where(i => i.Kind == kind && (query.Length == 0 || Core.Text.Fold(i.Label ?? "").Contains(query))).ToList();
            if (shown.Count == 0)
                return;
            Tree.Children.Add(new TextBlock
            {
                Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 2),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Faint"],
            });
            foreach (var item in shown)
            {
                // Hộp kiểm dựng một lần; gỡ khỏi cha cũ (nếu có) trước khi đưa vào cây mới.
                (item.Box.Parent as Panel)?.Children.Remove(item.Box);
                Tree.Children.Add(item.Box);
            }
        }

        private void OnSearch(object sender, TextChangedEventArgs e)
        {
            if (_workspace != null)
                Render();
        }

        private List<string> ManualList(string kind)
        {
            switch (kind)
            {
                case "stack": return _manual.StackIds;
                case "profile": return _manual.ProfileIds;
                case "tool": return _manual.ToolIds;
                default: return _manual.CustomIds;
            }
        }

        private void OnToggle(Item item)
        {
            if (_updating)
                return;
            var list = ManualList(item.Kind);
            var on = item.Box.IsChecked == true;
            list.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (on)
                list.Add(item.Id);
            Recalculate();
        }

        private ToolStore _store;

        /// <summary>Kho thư viện đọc một lần mỗi lần mở tab, để lỗi đọc không báo lặp lại ở từng cú tick.</summary>
        private ToolStore SafeLibrary()
        {
            if (_store != null)
                return _store;
            try { return _store = _library?.Invoke(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException) { return null; }
        }

        // ---- khung "Gói sẽ có" ----

        private WorkspacePackPreview _preview;

        private void Recalculate()
        {
            _updating = true;
            try
            {
                _preview = WorkspacePack.Preview(_workspace, SafeLibrary(), Clone(_manual));
                var resolved = _preview.Selection;
                var pulled = new List<string>();
                foreach (var item in _items)
                {
                    var manual = ManualList(item.Kind).Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
                    var inPack = Contains(resolved, item);
                    var why = item.Kind == "stack" ? null : resolved.WhyPulled(item.Kind, item.Id);
                    item.Box.IsChecked = inPack;
                    // Mục chỉ được kéo theo thì khoá: muốn bỏ phải bỏ mục kéo nó vào.
                    item.Box.IsEnabled = manual || !inPack;
                    item.Box.ToolTip = inPack && !manual && why != null ? why : null;
                    if (inPack && !manual && why != null)
                        pulled.Add($"• {item.Label}: {why}");
                }

                Summary.Text = resolved.IsEmpty
                    ? "Chưa chọn gì."
                    : $"{_preview.StackCount} bộ chạy · {_preview.ProfileCount} profile · {_preview.ToolCount} tool · {_preview.CustomCount} lệnh riêng";
                Pulled.Text = pulled.Count == 0 ? "" : "Tự thêm theo:\n" + string.Join("\n", pulled);
                Warnings.Text = _preview.Warnings == null || _preview.Warnings.Count == 0 ? "" : string.Join("\n", _preview.Warnings.Select(w => "⚠ " + w));
            }
            finally
            {
                _updating = false;
            }
            UpdateButton();
        }

        private static bool Contains(WorkspacePackSelection sel, Item item)
        {
            List<string> list;
            switch (item.Kind)
            {
                case "stack": list = sel.StackIds; break;
                case "profile": list = sel.ProfileIds; break;
                case "tool": list = sel.ToolIds; break;
                default: list = sel.CustomIds; break;
            }
            return list.Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        }

        private static WorkspacePackSelection Clone(WorkspacePackSelection s) => new WorkspacePackSelection
        {
            ToolIds = s.ToolIds.ToList(), ProfileIds = s.ProfileIds.ToList(), StackIds = s.StackIds.ToList(), CustomIds = s.CustomIds.ToList(),
        };

        private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateButton();

        private void UpdateButton()
        {
            PackButton.IsEnabled = _preview != null && !_preview.Selection.IsEmpty && !string.IsNullOrWhiteSpace(PackName.Text);
        }

        // ---- đóng gói ----

        private void OnPack(object sender, RoutedEventArgs e)
        {
            if (_preview == null || _preview.Selection.IsEmpty)
                return;
            var name = string.Concat(PackName.Text.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Đóng gói workspace",
                FileName = name + Packer.FileExtension,
                DefaultExt = Packer.FileExtension,
                Filter = $"Gói Toolbox (*{Packer.FileExtension})|*{Packer.FileExtension}",
            };
            if (dialog.ShowDialog(Host) != true)
                return;

            try
            {
                var result = Packer.PackWorkspace(_workspace, SafeLibrary(), Clone(_manual), dialog.FileName);
                MessageBox.Show(Host,
                    $"Đã đóng gói {result.StackCount} bộ chạy, {result.ProfileCount} profile, {result.ToolCount} tool, {result.CustomCount} lệnh riêng.\n{dialog.FileName}",
                    "Toolbox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidDataException)
            {
                Dialogs.Error(Host, "Không đóng gói được: " + ex.Message);
            }
        }
    }
}
