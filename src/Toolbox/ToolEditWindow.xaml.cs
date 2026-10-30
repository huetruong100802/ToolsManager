using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox
{
    public partial class ToolEditWindow : Window
    {
        private readonly ToolEntry _entry;
        private readonly bool _isNew;

        /// <summary>Sửa trực tiếp <paramref name="entry"/> khi bấm Lưu.</summary>
        public ToolEditWindow(ToolEntry entry, bool isNew)
        {
            InitializeComponent();
            _entry = entry;
            _isNew = isNew;
            Title = isNew ? "Thêm tool" : "Sửa tool";

            PathBox.Text = entry.Path ?? "";
            CommandBox.Text = entry.Command ?? "";
            NameBox.Text = entry.Name ?? "";
            ArgsBox.Text = entry.Args ?? "";
            DescriptionBox.Text = entry.Description ?? "";
            TagsBox.Text = string.Join(", ", entry.Tags);
            SetupBox.Text = entry.Setup ?? "";
            PackCombo.SelectedIndex = (int)entry.Pack;

            Loaded += (s, e) => (string.IsNullOrEmpty(PathBox.Text) ? PathBox : NameBox).Focus();
        }

        private void OnBrowseFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Chọn file chạy của tool",
                Filter = "Tool (*.exe;*.bat;*.cmd;*.ps1;*.py;*.lnk)|*.exe;*.bat;*.cmd;*.ps1;*.py;*.lnk|Tất cả file|*.*",
            };
            if (dialog.ShowDialog(this) == true)
                SetPath(dialog.FileName);
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục của tool" })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    SetPath(dialog.SelectedPath);
            }
        }

        private void OnBrowseSetup(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Chọn file bộ cài",
                Filter = "Bộ cài (*.exe;*.msi)|*.exe;*.msi|Tất cả file|*.*",
            };
            if (dialog.ShowDialog(this) == true)
                SetupBox.Text = dialog.FileName;
        }

        private void OnPackChanged(object sender, RoutedEventArgs e)
        {
            SetupPanel.Visibility = PackCombo.SelectedIndex == (int)PackMode.Setup ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Có lệnh thì tool không đóng gói nên khoá lựa chọn pack.</summary>
        private void OnCommandChanged(object sender, TextChangedEventArgs e)
        {
            var isCommand = NullIfEmpty(CommandBox.Text) != null;
            PackCombo.IsEnabled = !isCommand;
            if (isCommand)
                SetupPanel.Visibility = Visibility.Collapsed;
            else
                OnPackChanged(sender, e);
        }

        private void OnPathChanged(object sender, RoutedEventArgs e)
        {
            var path = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim().Trim('"'));
            if (path.Length > 0 && (File.Exists(path) || Directory.Exists(path)) && NameBox.Text.Trim().Length == 0)
                SetPath(PathBox.Text.Trim().Trim('"'));
        }

        /// <summary>Điền tên và cách đóng gói gợi ý nếu người dùng chưa tự điền.</summary>
        private void SetPath(string path)
        {
            // Chọn shortcut → dùng file thật mà shortcut trỏ tới, kèm tham số/mô tả của shortcut.
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)
                && Shortcuts.Resolve(Environment.ExpandEnvironmentVariables(path)) is ShortcutInfo link)
            {
                path = link.TargetPath;
                if (NameBox.Text.Trim().Length == 0)
                    NameBox.Text = link.Name;
                if (ArgsBox.Text.Trim().Length == 0)
                    ArgsBox.Text = link.Arguments;
                if (DescriptionBox.Text.Trim().Length == 0)
                    DescriptionBox.Text = link.Description;
            }

            PathBox.Text = path;
            var resolved = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
                return;
            if (NameBox.Text.Trim().Length == 0)
                NameBox.Text = ToolDefaults.SuggestName(resolved);
            if (_isNew)
                PackCombo.SelectedIndex = (int)ToolDefaults.SuggestPack(resolved);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text.Trim().Trim('"');
            var name = NameBox.Text.Trim();
            if (path.Length == 0 || name.Length == 0)
            {
                Dialogs.Error(this, "Cần nhập đường dẫn và tên.");
                return;
            }

            var resolved = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(resolved) && !Directory.Exists(resolved)
                && !Dialogs.Confirm(this, $"Không tìm thấy:\n{resolved}\n\nVẫn lưu?", warning: true))
                return;

            var command = NullIfEmpty(CommandBox.Text);
            if (command != null && !Directory.Exists(resolved))
            {
                Dialogs.Error(this, "Với lệnh, đường dẫn phải là thư mục chạy lệnh.");
                return;
            }

            var pack = command == null ? (PackMode)Math.Max(0, PackCombo.SelectedIndex) : PackMode.None;
            var setup = SetupBox.Text.Trim().Trim('"');
            if (pack == PackMode.Setup)
            {
                if (setup.Length == 0)
                {
                    Dialogs.Error(this, "Chế độ \"Kèm bộ cài\" cần chọn file bộ cài.");
                    return;
                }
                var resolvedSetup = Environment.ExpandEnvironmentVariables(setup);
                if (!File.Exists(resolvedSetup)
                    && !Dialogs.Confirm(this, $"Không tìm thấy bộ cài:\n{resolvedSetup}\n\nVẫn lưu?", warning: true))
                    return;
            }

            _entry.Path = path;
            _entry.Name = name;
            _entry.Args = NullIfEmpty(ArgsBox.Text);
            _entry.Description = NullIfEmpty(DescriptionBox.Text);
            _entry.Tags = TagsBox.Text.Split(',')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _entry.Pack = pack;
            _entry.Setup = setup.Length == 0 ? null : setup;
            _entry.Command = command;
            if (command != null)
                _entry.Store = StoreMode.Link;
            DialogResult = true;
        }

        private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
