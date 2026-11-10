using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>
    /// Form thêm/sửa tool hai mức: phần cơ bản (đường dẫn hoặc lệnh, tên, mô tả, tag) luôn hiện, thả file hoặc thư mục vào là tự nhận;
    /// phần "Nâng cao" gập lại (tham số, nguồn lệnh, lệnh trợ giúp, cách chia sẻ, bộ cài). Lỗi hiện ngay cạnh ô sai, không bật hộp thoại.
    /// </summary>
    public partial class ToolEditWindow : Window
    {
        private readonly ToolEntry _entry;
        private readonly bool _isNew;

        /// <summary>Đường dẫn không tồn tại mà người dùng đã được nhắc và bấm Lưu lần nữa: coi như đồng ý lưu.</summary>
        private string _acceptedMissingPath;
        private string _acceptedMissingSetup;

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
            SuffixFileBox.Text = entry.SuffixSource?.File ?? "";
            SuffixPathBox.Text = entry.SuffixSource?.JsonPath ?? "";
            SuffixTemplateBox.Text = entry.SuffixSource?.Template ?? "";
            HelpTemplateBox.Text = entry.HelpTemplate ?? "";

            // Đang sửa tool đã cấu hình phần nâng cao thì mở sẵn, để không giấu thứ đang có hiệu lực.
            Advanced.IsExpanded = HasAdvancedValues(entry);

            Loaded += (s, e) => (string.IsNullOrEmpty(PathBox.Text) ? PathBox : NameBox).Focus();
        }

        /// <summary>Tool có giá trị nào ở phần "Nâng cao" khác mặc định không.</summary>
        internal static bool HasAdvancedValues(ToolEntry entry) =>
            !string.IsNullOrWhiteSpace(entry.Args) || entry.SuffixSource != null || !string.IsNullOrWhiteSpace(entry.HelpTemplate)
            || (!entry.IsCommand && entry.Pack != PackMode.Folder);

        // ---- Chọn / thả đường dẫn ----

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

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>Thả file hoặc thư mục vào cửa sổ: điền đường dẫn, tên, cách chia sẻ gợi ý và nói rõ đã nhận loại gì.</summary>
        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                SetPath(files[0]);
            e.Handled = true;
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

        /// <summary>Có lệnh thì tool không chia sẻ file nên khoá lựa chọn cách chia sẻ.</summary>
        private void OnCommandChanged(object sender, TextChangedEventArgs e)
        {
            var isCommand = NullIfEmpty(CommandBox.Text) != null;
            PackCombo.IsEnabled = !isCommand;
            if (isCommand)
                SetupPanel.Visibility = Visibility.Collapsed;
            else
                OnPackChanged(sender, e);
            ClearErrors();
        }

        private void OnFieldEdited(object sender, TextChangedEventArgs e) => ClearErrors();

        private void OnPathChanged(object sender, RoutedEventArgs e)
        {
            var path = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim().Trim('"'));
            if (path.Length > 0 && (File.Exists(path) || Directory.Exists(path)) && NameBox.Text.Trim().Length == 0)
                SetPath(PathBox.Text.Trim().Trim('"'));
        }

        /// <summary>Điền tên và cách chia sẻ gợi ý nếu người dùng chưa tự điền, và nói rõ đã nhận file hay thư mục.</summary>
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
                {
                    ArgsBox.Text = link.Arguments;
                    if (!string.IsNullOrWhiteSpace(link.Arguments))
                        Advanced.IsExpanded = true;
                }
                if (DescriptionBox.Text.Trim().Length == 0)
                    DescriptionBox.Text = link.Description;
            }

            PathBox.Text = path;
            var resolved = Environment.ExpandEnvironmentVariables(path);
            TypeHint.Text = DescribePath(resolved, NullIfEmpty(CommandBox.Text) != null);
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
                return;
            if (NameBox.Text.Trim().Length == 0)
                NameBox.Text = ToolDefaults.SuggestName(resolved);
            if (_isNew)
                PackCombo.SelectedIndex = (int)ToolDefaults.SuggestPack(resolved);
        }

        /// <summary>Câu nói loại tool Toolbox nhận ra từ đường dẫn, để người dùng biết bước tiếp theo.</summary>
        internal static string DescribePath(string resolved, bool hasCommand)
        {
            if (Directory.Exists(resolved))
                return hasCommand
                    ? "Đã nhận thư mục: lệnh ở dưới sẽ chạy trong thư mục này."
                    : "Đã nhận thư mục. Nhập lệnh (vd: npm run dev) để chạy trong thư mục này; không có lệnh thì cần chọn file chạy.";
            if (File.Exists(resolved))
                return "Đã nhận file chạy" + (string.Equals(Path.GetExtension(resolved), ".exe", StringComparison.OrdinalIgnoreCase) ? " (chương trình)." : " (script).");
            return "Chưa thấy đường dẫn này trên máy.";
        }

        // ---- Lỗi tại chỗ ----

        private void ShowError(TextBlock target, string message, Control focus)
        {
            target.Text = message;
            target.Visibility = Visibility.Visible;
            focus?.Focus();
        }

        private void ClearErrors()
        {
            foreach (var block in new[] { PathError, NameError, HelpError, SetupError })
                if (block != null && block.Visibility == Visibility.Visible)
                    block.Visibility = Visibility.Collapsed;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            ClearErrors();
            var path = PathBox.Text.Trim().Trim('"');
            var name = NameBox.Text.Trim();
            var command = NullIfEmpty(CommandBox.Text);
            var failed = false;

            if (name.Length == 0)
            {
                ShowError(NameError, "Cần nhập tên.", NameBox);
                failed = true;
            }
            if (path.Length == 0 && command == null)
            {
                ShowError(PathError, "Cần nhập đường dẫn của file chạy, hoặc nhập lệnh.", PathBox);
                failed = true;
            }

            if (HelpTemplateBox.Text.IndexOf((char)10) >= 0 || HelpTemplateBox.Text.IndexOf((char)13) >= 0)
            {
                Advanced.IsExpanded = true;
                ShowError(HelpError, "Mẫu lệnh lấy trợ giúp phải nằm trên một dòng.", HelpTemplateBox);
                failed = true;
            }

            // Tool lệnh có thể không có đường dẫn: chạy tại thư mục workspace.
            var resolved = Environment.ExpandEnvironmentVariables(path);
            if (path.Length > 0 && !failed)
            {
                if (command != null && !Directory.Exists(resolved) && File.Exists(resolved))
                {
                    ShowError(PathError, "Với lệnh, đường dẫn phải là thư mục chạy lệnh, không phải file.", PathBox);
                    failed = true;
                }
                else if (!File.Exists(resolved) && !Directory.Exists(resolved)
                         && !string.Equals(_acceptedMissingPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    // Cho lưu đường dẫn chưa có (máy khác, ổ chưa cắm) nhưng phải xác nhận bằng một lần bấm Lưu nữa.
                    _acceptedMissingPath = path;
                    ShowError(PathError, "Không tìm thấy: " + resolved + ". Bấm Lưu lần nữa nếu vẫn muốn lưu.", PathBox);
                    failed = true;
                }
            }

            var pack = command == null ? (PackMode)Math.Max(0, PackCombo.SelectedIndex) : PackMode.None;
            var setup = SetupBox.Text.Trim().Trim('"');
            if (pack == PackMode.Setup)
            {
                var resolvedSetup = Environment.ExpandEnvironmentVariables(setup);
                if (setup.Length == 0)
                {
                    Advanced.IsExpanded = true;
                    ShowError(SetupError, "Chế độ \"Kèm bộ cài\" cần chọn file bộ cài.", SetupBox);
                    failed = true;
                }
                else if (!File.Exists(resolvedSetup) && !string.Equals(_acceptedMissingSetup, setup, StringComparison.OrdinalIgnoreCase))
                {
                    _acceptedMissingSetup = setup;
                    Advanced.IsExpanded = true;
                    ShowError(SetupError, "Không tìm thấy bộ cài: " + resolvedSetup + ". Bấm Lưu lần nữa nếu vẫn muốn lưu.", SetupBox);
                    failed = true;
                }
            }
            if (failed)
                return;

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
            _entry.SuffixSource = BuildSuffixSource();
            _entry.HelpTemplate = NullIfEmpty(HelpTemplateBox.Text);
            if (command != null)
                _entry.Store = StoreMode.Link;
            DialogResult = true;
        }

        /// <summary>Nguồn lệnh từ ô nhập; giữ nguyên nguồn dạng pattern/values (chỉnh trong tools.json) nếu có.</summary>
        private SuffixSource BuildSuffixSource()
        {
            var current = _entry.SuffixSource;
            var advanced = current != null && (!string.IsNullOrEmpty(current.Pattern) || (current.Values != null && current.Values.Count > 0));
            if (advanced)
                return current;
            var file = NullIfEmpty(SuffixFileBox.Text);
            var jsonPath = NullIfEmpty(SuffixPathBox.Text);
            if (file == null || jsonPath == null)
                return null;
            return new SuffixSource { File = file, JsonPath = jsonPath, Template = NullIfEmpty(SuffixTemplateBox.Text) };
        }

        private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
