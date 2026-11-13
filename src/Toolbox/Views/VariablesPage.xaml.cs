using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>
    /// Tab Biến của workspace, theo Library › Variable groups của ADO: danh sách nhóm ở trên, editor (Thuộc tính + Biến) ở dưới.
    /// Editor sửa trên bản nháp (<see cref="_rows"/>); chỉ khi Lưu mới dựng <see cref="VariableGroup"/> mới và thay vào workspace,
    /// nên huỷ nháp không đụng dữ liệu đã lưu. Secret đã lưu không bao giờ giải mã ra UI: chỉ hiện "********" và cho ghi đè.
    /// </summary>
    public partial class VariablesPage : UserControl
    {
        public const string TabId = ":vars";

        /// <summary>Một dòng trong danh sách nhóm.</summary>
        public sealed class GroupItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public int Count { get; set; }
            public string UsedText { get; set; }
            public string ModifiedText { get; set; }
            public bool IsSelected { get; set; }
            public Visibility NeedsInputVisibility { get; set; } = Visibility.Collapsed;
            public Visibility AgentOffVisibility { get; set; } = Visibility.Collapsed;
        }

        /// <summary>Một biến trong bản nháp. Secret: <see cref="Enc"/> là bản đã lưu, <see cref="Pending"/> là giá trị vừa gõ chưa lưu.</summary>
        private sealed class VarRow
        {
            public string Name = "";
            public bool Secret;
            public string Value = "";
            public string Enc;
            public string Pending;
            /// <summary>Đang bấm "Đổi giá trị" trên secret đã lưu.</summary>
            public bool Replacing;
            public bool Reveal;
            public StackPanel Root;
            public TextBox NameBox;
            public ContentControl ValueHost;
            public Button LockButton;
            public TextBlock Warn;
        }

        private readonly List<VarRow> _rows = new List<VarRow>();
        private Workspace _workspace;
        private Func<Action, Action, bool> _mutate;
        /// <summary>Nhóm đã lưu đang sửa; null khi nhóm mới chưa lưu.</summary>
        private VariableGroup _current;
        private bool _editing;
        private bool _dirty;
        private bool _loading;
        /// <summary>Đã bấm Lưu một lần: từ đó mới hiện lỗi "tên nhóm trống".</summary>
        private bool _attempted;

        public VariablesPage()
        {
            InitializeComponent();
        }

        private Window Host => Window.GetWindow(this);

        // ---- vào / ra trang ----

        /// <param name="mutate">Áp thay đổi lên workspace rồi lưu; lưu lỗi thì tự hoàn tác (WorkspaceView.Mutate).</param>
        public void Load(Workspace workspace, Func<Action, Action, bool> mutate)
        {
            var same = _workspace != null && string.Equals(_workspace.Id, workspace.Id, StringComparison.OrdinalIgnoreCase);
            _workspace = workspace;
            _mutate = mutate;
            if (!same)
                CloseEditor();
            else if (_editing && _current != null)
            {
                // Gộp từ đĩa có thể thay đối tượng nhóm: trỏ lại theo id; nhóm đã bị xoá ở nơi khác thì đóng editor.
                _current = VariableGroups.Find(workspace, _current.Id);
                if (_current == null)
                    CloseEditor();
            }
            RenderList();
        }

        /// <summary>Mở nhóm theo id (từ chip "Mở nhóm" ở tab Chạy). Hỏi trước nếu nhóm đang sửa dở.</summary>
        public void Select(string groupId)
        {
            var group = VariableGroups.Find(_workspace, groupId);
            if (group == null || !ConfirmLeave())
                return;
            Open(group, false, false);
        }

        /// <summary>
        /// Gọi trước khi rời trang hoặc đổi nhóm. Còn thay đổi chưa lưu thì hỏi: lưu, bỏ, hoặc ở lại.
        /// False nghĩa là người dùng chọn ở lại (hoặc lưu lỗi): bên gọi không được đi tiếp.
        /// </summary>
        public bool ConfirmLeave()
        {
            if (!_editing || !_dirty)
                return true;
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "(nhóm mới)" : NameBox.Text.Trim();
            var answer = Dialogs.Choose(Host, $"Nhóm biến \"{name}\" có thay đổi chưa lưu.\n\nYes: lưu rồi tiếp tục\nNo: bỏ thay đổi\nCancel: ở lại chỉnh tiếp");
            if (answer == null)
                return false;
            if (answer == true)
                return Save();
            if (_current != null)
                Open(_current, false, false);
            else
                CloseEditor();
            return true;
        }

        private void CloseEditor()
        {
            _editing = false;
            _current = null;
            _dirty = false;
            _attempted = false;
            _rows.Clear();
            VariableRows.Children.Clear();
            EditorCard.Visibility = Visibility.Collapsed;
        }

        // ---- danh sách ----

        private void RenderList()
        {
            if (_workspace == null)
                return;
            var all = _workspace.VariableGroups;
            var query = Core.Text.Fold((FilterBox.Text ?? "").Trim());
            var items = all
                .Where(g => query.Length == 0 || Core.Text.Fold(g.Label ?? "").Contains(query) || Core.Text.Fold(g.Description ?? "").Contains(query))
                .OrderBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(g =>
                {
                    var used = VariableGroups.UsedBy(_workspace, g.Id).Count;
                    return new GroupItem
                    {
                        Id = g.Id,
                        Name = g.Label,
                        Description = g.Description,
                        Count = g.Variables.Count,
                        UsedText = used == 0 ? "—" : used + " lệnh",
                        ModifiedText = FormatModified(g.ModifiedAt),
                        IsSelected = _editing && _current != null && string.Equals(_current.Id, g.Id, StringComparison.OrdinalIgnoreCase),
                        NeedsInputVisibility = g.Variables.Any(v => v.NeedsInput) ? Visibility.Visible : Visibility.Collapsed,
                        AgentOffVisibility = g.AllowAgent ? Visibility.Collapsed : Visibility.Visible,
                    };
                })
                .ToList();
            GroupList.ItemsSource = null;
            GroupList.ItemsSource = items;
            EmptyHint.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ListCard.Visibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoMatch.Visibility = all.Count > 0 && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string FormatModified(string iso)
        {
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc))
                return "—";
            var t = utc.ToLocalTime();
            if (t.Date == DateTime.Today)
                return "hôm nay " + t.ToString("HH:mm", CultureInfo.InvariantCulture);
            return t.Year == DateTime.Today.Year
                ? t.ToString("dd/MM", CultureInfo.InvariantCulture)
                : t.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private void OnFilter(object sender, EventArgs e) => RenderList();

        private void OnPickGroup(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is GroupItem item)
                Select(item.Id);
        }

        private void OnNewGroup(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeave())
                return;
            Open(null, true, false);
        }

        // ---- editor ----

        /// <param name="source">Nhóm để nạp vào editor; null với nhóm mới trống.</param>
        /// <param name="isNew">True: lưu sẽ tạo nhóm mới (nhóm mới hoặc bản nhân bản).</param>
        /// <param name="dirty">True với bản nhân bản: chưa lưu nên coi là có thay đổi.</param>
        private void Open(VariableGroup source, bool isNew, bool dirty)
        {
            _loading = true;
            try
            {
                _current = isNew ? null : source;
                _editing = true;
                _attempted = false;
                _rows.Clear();
                VariableRows.Children.Clear();
                NameBox.Text = source?.Label ?? "";
                DescriptionBox.Text = source?.Description ?? "";
                AgentBox.IsChecked = source?.AllowAgent ?? true;
                foreach (var v in source?.Variables ?? new List<GroupVariable>())
                    BuildRow(new VarRow { Name = v.Name ?? "", Secret = v.Secret, Value = v.Secret ? "" : v.Value ?? "", Enc = v.Secret ? v.Enc : null }, false);
                if (source == null)
                    BuildRow(new VarRow(), false);
            }
            finally
            {
                _loading = false;
            }
            EditorCard.Visibility = Visibility.Visible;
            CloneButton.IsEnabled = DeleteButton.IsEnabled = _current != null;
            DeleteButton.Content = _current == null ? "Bỏ nháp" : "Xoá";
            SetDirty(dirty);
            RefreshHeader();
            RefreshNotes();
            RenderList();
            if (isNew && source == null)
                NameBox.Focus();
        }

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            DirtyText.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            // Nhóm mới luôn lưu được (kể cả chưa gõ gì, để báo lỗi thiếu tên); nhóm đã lưu chỉ khi có thay đổi.
            SaveButton.IsEnabled = dirty || _current == null;
        }

        private void RefreshHeader()
        {
            EditorTitle.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? "(nhóm mới)" : NameBox.Text.Trim();
            var users = _current == null ? new List<Profile>() : VariableGroups.UsedBy(_workspace, _current.Id);
            UsedByText.Text = _current == null
                ? "Nhóm mới: lưu xong mới gắn được vào lệnh đã ghim."
                : users.Count == 0
                    ? "Chưa có lệnh đã ghim nào dùng nhóm này. Gắn ở tab Chạy: menu ⋯ của lệnh › Nhóm biến."
                    : "Đang dùng bởi: " + string.Join(", ", users.Select(p => p.Label)) + ".";
        }

        /// <summary>Thay đổi một thuộc tính (tên, mô tả, cho agent dùng).</summary>
        private void OnPropertyChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || !_editing)
                return;
            RefreshHeader();
            Touch();
        }

        private void Touch()
        {
            if (_loading || !_editing)
                return;
            SetDirty(true);
            RefreshNotes();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _editing)
            {
                Save();
                e.Handled = true;
            }
        }

        // ---- dòng biến ----

        private void OnAddVariable(object sender, RoutedEventArgs e)
        {
            if (!_editing)
                return;
            var row = new VarRow();
            BuildRow(row, true);
            Touch();
        }

        private void BuildRow(VarRow row, bool focusName)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var width in new[] { new GridLength(2, GridUnitType.Star), new GridLength(8), new GridLength(3, GridUnitType.Star), new GridLength(8), new GridLength(36), new GridLength(36) })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

            row.NameBox = new TextBox { Text = row.Name, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            AutomationProperties.SetName(row.NameBox, "Tên biến");
            row.NameBox.TextChanged += (s, e) =>
            {
                row.Name = row.NameBox.Text;
                UpdateWarn(row);
                Touch();
            };
            Grid.SetColumn(row.NameBox, 0);

            row.ValueHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
            Grid.SetColumn(row.ValueHost, 2);

            row.LockButton = new Button { Style = (Style)FindResource("GhostButton"), Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0) };
            row.LockButton.Click += (s, e) => ToggleSecret(row);
            Grid.SetColumn(row.LockButton, 4);

            var remove = new Button { Style = (Style)FindResource("GhostButton"), Padding = new Thickness(0), ToolTip = "Xoá biến này", Content = Glyph("", null) };
            AutomationProperties.SetName(remove, "Xoá biến");
            remove.Click += (s, e) => RemoveRow(row);
            Grid.SetColumn(remove, 5);

            grid.Children.Add(row.NameBox);
            grid.Children.Add(row.ValueHost);
            grid.Children.Add(row.LockButton);
            grid.Children.Add(remove);

            row.Warn = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0), Visibility = Visibility.Collapsed };
            row.Warn.SetResourceReference(TextBlock.ForegroundProperty, "WarnInk");

            row.Root = new StackPanel();
            row.Root.Children.Add(grid);
            row.Root.Children.Add(row.Warn);

            RenderValue(row, false);
            UpdateLock(row);
            UpdateWarn(row);
            _rows.Add(row);
            VariableRows.Children.Add(row.Root);
            NoVariables.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (focusName)
                row.NameBox.Focus();
        }

        private void RemoveRow(VarRow row)
        {
            _rows.Remove(row);
            VariableRows.Children.Remove(row.Root);
            NoVariables.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Touch();
        }

        private static TextBlock Glyph(string glyph, string brushKey)
        {
            var text = new TextBlock { Text = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            text.SetResourceReference(TextBlock.StyleProperty, "Icon");
            if (brushKey != null)
                text.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return text;
        }

        /// <summary>Bấm 🔒. Thường → secret: giá trị đang có thành giá trị chờ mã hoá. Secret → thường: xoá giá trị cũ, giống ADO.</summary>
        private void ToggleSecret(VarRow row)
        {
            if (!row.Secret)
            {
                row.Secret = true;
                row.Pending = string.IsNullOrEmpty(row.Value) ? null : row.Value;
                row.Value = "";
                row.Enc = null;
            }
            else
            {
                row.Secret = false;
                row.Value = "";
                row.Enc = null;
                row.Pending = null;
            }
            row.Replacing = false;
            row.Reveal = false;
            RenderValue(row, false);
            UpdateLock(row);
            Touch();
        }

        private static void UpdateLock(VarRow row)
        {
            row.LockButton.Content = Glyph(row.Secret ? "" : "", row.Secret ? "Accent" : null);
            row.LockButton.ToolTip = row.Secret
                ? "Secret: bấm để đổi về biến thường (xoá giá trị đã lưu)"
                : "Bấm để đổi thành secret: mã hoá khi lưu, không xem lại được";
            AutomationProperties.SetName(row.LockButton, row.Secret ? "Bỏ secret" : "Đặt làm secret");
        }

        private static void UpdateWarn(VarRow row)
        {
            var warn = VariableGroups.WarningFor(row.Name);
            row.Warn.Text = warn ?? "";
            row.Warn.Visibility = warn == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Dựng ô giá trị theo trạng thái: thường (ô chữ), secret đã lưu ("********" + Đổi giá trị), secret đang nhập (ô mật khẩu + xem).</summary>
        private void RenderValue(VarRow row, bool focus)
        {
            if (!row.Secret)
            {
                var box = new TextBox { Text = row.Value ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
                AutomationProperties.SetName(box, "Giá trị");
                box.TextChanged += (s, e) =>
                {
                    row.Value = box.Text;
                    Touch();
                };
                row.ValueHost.Content = box;
                if (focus)
                    box.Focus();
                return;
            }

            if (row.Enc != null && row.Pending == null && !row.Replacing)
            {
                // Đã lưu: không có đường xem lại, chỉ ghi đè.
                var dock = new DockPanel();
                var change = new Button { Content = "Đổi giá trị", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(8, 0, 0, 0) };
                AutomationProperties.SetName(change, "Đổi giá trị secret");
                change.Click += (s, e) =>
                {
                    row.Replacing = true;
                    RenderValue(row, true);
                };
                DockPanel.SetDock(change, Dock.Right);
                var mask = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(9, 7, 9, 7) };
                mask.SetResourceReference(Border.BackgroundProperty, "WindowBg");
                mask.SetResourceReference(Border.BorderBrushProperty, "Line");
                var stars = new TextBlock { Text = "********", FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
                stars.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                AutomationProperties.SetName(stars, "Secret đã lưu, không xem lại được");
                mask.Child = stars;
                dock.Children.Add(change);
                dock.Children.Add(mask);
                row.ValueHost.Content = dock;
                return;
            }

            var panel = new DockPanel();
            var reveal = new Button { Style = (Style)FindResource("GhostButton"), Padding = new Thickness(6, 4, 6, 4), Content = Glyph(row.Reveal ? "" : "", null) };
            reveal.ToolTip = row.Reveal ? "Ẩn giá trị đang gõ" : "Xem giá trị đang gõ (chỉ trước khi lưu)";
            AutomationProperties.SetName(reveal, row.Reveal ? "Ẩn giá trị" : "Xem giá trị đang gõ");
            reveal.Click += (s, e) =>
            {
                row.Reveal = !row.Reveal;
                RenderValue(row, true);
            };
            DockPanel.SetDock(reveal, Dock.Right);
            panel.Children.Add(reveal);

            if (row.Enc != null)
            {
                var cancel = new Button { Content = "Huỷ", Style = (Style)FindResource("GhostButton"), ToolTip = "Giữ giá trị đã lưu" };
                cancel.Click += (s, e) =>
                {
                    row.Replacing = false;
                    row.Pending = null;
                    row.Reveal = false;
                    RenderValue(row, false);
                };
                DockPanel.SetDock(cancel, Dock.Right);
                panel.Children.Add(cancel);
            }

            var hint = new TextBlock
            {
                Text = row.Enc != null ? "(để trống: giữ giá trị cũ)" : "(Cần nhập)",
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Visibility = string.IsNullOrEmpty(row.Pending) ? Visibility.Visible : Visibility.Collapsed,
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");

            FrameworkElement field;
            if (row.Reveal)
            {
                var text = new TextBox { Text = row.Pending ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
                text.TextChanged += (s, e) =>
                {
                    row.Pending = text.Text;
                    hint.Visibility = string.IsNullOrEmpty(row.Pending) ? Visibility.Visible : Visibility.Collapsed;
                    Touch();
                };
                field = text;
            }
            else
            {
                var password = new PasswordBox { Password = row.Pending ?? "" };
                password.PasswordChanged += (s, e) =>
                {
                    row.Pending = password.Password;
                    hint.Visibility = string.IsNullOrEmpty(row.Pending) ? Visibility.Visible : Visibility.Collapsed;
                    Touch();
                };
                field = password;
            }
            AutomationProperties.SetName(field, "Giá trị secret");
            var holder = new Grid();
            holder.Children.Add(field);
            holder.Children.Add(hint);
            panel.Children.Add(holder);
            row.ValueHost.Content = panel;
            if (focus)
                field.Focus();
        }

        // ---- kiểm tra và ghi chú ----

        /// <summary>Nhóm tạm chỉ có tên và tên biến, để kiểm tra trực tiếp mà không mã hoá secret.</summary>
        private VariableGroup Shell() => new VariableGroup
        {
            Label = (NameBox.Text ?? "").Trim(),
            Variables = _rows.Select(r => new GroupVariable { Name = (r.Name ?? "").Trim() }).ToList(),
        };

        private List<string> CurrentErrors()
        {
            var shell = Shell();
            var errors = VariableGroups.Validate(shell);
            // "Tên nhóm trống" chỉ báo sau lần Lưu đầu, để nhóm mới vừa mở không đỏ sẵn.
            if (!_attempted)
                errors.RemoveAll(e => e.StartsWith("Tên nhóm", StringComparison.Ordinal));
            if (shell.Label.Length > 0 && _workspace.VariableGroups.Any(g => !ReferenceEquals(g, _current)
                    && !(_current != null && string.Equals(g.Id, _current.Id, StringComparison.OrdinalIgnoreCase))
                    && string.Equals(g.Label, shell.Label, StringComparison.CurrentCultureIgnoreCase)))
                errors.Add($"Đã có nhóm tên \"{shell.Label}\" trong workspace này.");
            return errors;
        }

        private void RefreshNotes()
        {
            if (!_editing)
                return;
            var errors = CurrentErrors();
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            var needs = _rows.Where(r => r.Secret && r.Enc == null && string.IsNullOrEmpty(r.Pending) && !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => "Biến \"" + r.Name.Trim() + "\" là secret chưa có giá trị: lệnh dùng nhóm này sẽ báo lỗi cho tới khi bạn nhập.")
                .ToList();
            WarnText.Text = string.Join("\n", needs);
            WarnText.Visibility = needs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---- lưu, nhân bản, xoá ----

        private void OnSave(object sender, RoutedEventArgs e) => Save();

        /// <summary>Lưu bản nháp. True khi đã lưu (hoặc không có gì để lưu).</summary>
        private bool Save()
        {
            if (!_editing)
                return true;
            _attempted = true;
            // Dòng trống hoàn toàn (do bấm Thêm rồi bỏ) không phải lỗi.
            foreach (var blank in _rows.Where(r => string.IsNullOrWhiteSpace(r.Name) && !r.Secret && string.IsNullOrEmpty(r.Value)).ToList())
            {
                _rows.Remove(blank);
                VariableRows.Children.Remove(blank.Root);
            }
            NoVariables.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var errors = CurrentErrors();
            if (errors.Count > 0)
            {
                RefreshNotes();
                return false;
            }

            var group = new VariableGroup
            {
                Id = _current?.Id ?? WorkspaceStore.NewId(),
                Label = NameBox.Text.Trim(),
                Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                AllowAgent = AgentBox.IsChecked == true,
                ModifiedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            foreach (var row in _rows)
            {
                var variable = new GroupVariable { Name = row.Name.Trim() };
                if (row.Secret)
                {
                    variable.Secret = true;
                    if (!string.IsNullOrEmpty(row.Pending))
                    {
                        try
                        {
                            VariableGroups.SetSecret(variable, row.Pending);
                        }
                        catch (CryptographicException ex)
                        {
                            ErrorText.Text = $"Không mã hoá được secret \"{variable.Name}\": {ex.Message}";
                            ErrorText.Visibility = Visibility.Visible;
                            return false;
                        }
                    }
                    else
                        variable.Enc = row.Enc;   // để trống = giữ giá trị đã lưu; chưa có thì vẫn là "Cần nhập"
                }
                else
                    variable.Value = row.Value ?? "";
                group.Variables.Add(variable);
            }

            var saved = _mutate(() =>
            {
                var list = _workspace.VariableGroups;
                var index = list.FindIndex(g => string.Equals(g.Id, group.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    list[index] = group;
                else
                    list.Add(group);
            }, null);
            if (!saved)
                return false;

            // Nạp lại từ bản đã lưu: giá trị secret vừa gõ biến mất khỏi UI, chỉ còn "********".
            Open(group, false, false);
            Dialogs.Success($"Đã lưu nhóm biến \"{group.Label}\".");
            return true;
        }

        private void OnClone(object sender, RoutedEventArgs e)
        {
            if (_current == null || !ConfirmLeave())
                return;
            var source = VariableGroups.Find(_workspace, _current?.Id);
            if (source == null)
                return;
            var copy = new VariableGroup
            {
                Label = source.Label + " (bản sao)",
                Description = source.Description,
                AllowAgent = source.AllowAgent,
                // Secret chép nguyên bản đã mã hoá (cùng DPAPI, cùng entropy): không cần giải mã ra bộ nhớ.
                Variables = source.Variables.Select(v => new GroupVariable { Name = v.Name, Value = v.Value, Secret = v.Secret, Enc = v.Enc }).ToList(),
            };
            Open(copy, true, true);
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (_current == null)
            {
                // Nhóm mới chưa lưu: bỏ nháp.
                if (_dirty && !Dialogs.Confirm(Host, "Bỏ nhóm biến đang soạn?"))
                    return;
                CloseEditor();
                RenderList();
                return;
            }

            var group = _current;
            var users = VariableGroups.UsedBy(_workspace, group.Id);
            if (users.Count > 0)
            {
                Dialogs.Fatal(Host, $"Chưa xoá được nhóm \"{group.Label}\": {users.Count} lệnh đã ghim đang dùng.\n\n"
                    + string.Join("\n", users.Select(p => "• " + p.Label))
                    + "\n\nBỏ liên kết ở các lệnh đó (menu ⋯ › Nhóm biến › Bỏ liên kết) rồi xoá lại.");
                return;
            }
            if (!Dialogs.Confirm(Host, $"Xoá nhóm biến \"{group.Label}\"?\n\nCác giá trị, kể cả secret đã mã hoá, bị xoá và không khôi phục được.", true))
                return;
            if (!_mutate(() => _workspace.VariableGroups.RemoveAll(g => string.Equals(g.Id, group.Id, StringComparison.OrdinalIgnoreCase)), null))
                return;
            CloseEditor();
            RenderList();
            Dialogs.Success($"Đã xoá nhóm biến \"{group.Label}\".");
        }
    }
}
