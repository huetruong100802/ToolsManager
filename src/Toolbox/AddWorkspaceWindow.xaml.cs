using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    public partial class AddWorkspaceWindow : Window
    {
        private readonly HashSet<string> _existing;
        private bool _nameTouched;
        private bool _settingName;

        /// <param name="existingPaths">Đường dẫn các workspace đã có, để chặn thêm trùng thư mục.</param>
        public AddWorkspaceWindow(IEnumerable<string> existingPaths)
        {
            InitializeComponent();
            _existing = new HashSet<string>(existingPaths.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            Loaded += (s, e) => PathBox.Focus();
        }

        /// <summary>Workspace mới (chưa có Id/màu — <see cref="WorkspaceStore.Add"/> cấp). Có khi hộp thoại trả về true.</summary>
        public Workspace Result { get; private set; }

        public sealed class DetectionOption
        {
            public DetectionOption(Detection detection)
            {
                Detection = detection;
                Title = ToolPresets.Find(detection.Kind)?.Name ?? detection.Kind;
                Reason = detection.Reason;
                IsChecked = true;
            }

            public Detection Detection { get; }
            public string Title { get; }
            public string Reason { get; }
            public bool IsChecked { get; set; }
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục workspace" })
            {
                if (Directory.Exists(PathBox.Text))
                    dialog.SelectedPath = PathBox.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PathBox.Text = dialog.SelectedPath;
            }
        }

        private void OnPathChanged(object sender, TextChangedEventArgs e)
        {
            var text = PathBox.Text.Trim();
            DetectionList.ItemsSource = null;
            NoDetection.Visibility = Visibility.Visible;

            if (text.Length == 0)
            {
                SetHint("Toolbox đọc thư mục này để đoán các tool trong catalog.json mà dự án dùng.", error: false);
                NoDetection.Text = "Chọn thư mục để Toolbox dò tool.";
                UpdateAddEnabled();
                return;
            }

            string folder;
            try
            {
                folder = Environment.ExpandEnvironmentVariables(text);
                if (!Directory.Exists(folder))
                {
                    SetHint("Không tìm thấy thư mục này.", error: true);
                    NoDetection.Text = "Chưa dò được vì thư mục không tồn tại.";
                    UpdateAddEnabled();
                    return;
                }
            }
            catch (ArgumentException)
            {
                SetHint("Đường dẫn không hợp lệ.", error: true);
                UpdateAddEnabled();
                return;
            }

            if (_existing.Contains(Normalize(folder)))
                SetHint("Thư mục này đã là một workspace.", error: true);
            else
                SetHint("Đã tìm thấy thư mục.", error: false);

            if (!_nameTouched)
            {
                _settingName = true;
                NameBox.Text = WorkspaceDetector.SuggestName(folder);
                _settingName = false;
            }

            var found = WorkspaceDetector.Detect(folder).Select(d => new DetectionOption(d)).ToList();
            DetectionList.ItemsSource = found;
            NoDetection.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoDetection.Text = "Không nhận ra tool nào. Vẫn thêm được; gắn tool và lệnh riêng ở trang workspace.";
            UpdateAddEnabled();
        }

        private void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (!_settingName)
                _nameTouched = true;
            UpdateAddEnabled();
        }

        private void SetHint(string text, bool error)
        {
            PathHint.Text = text;
            PathHint.Foreground = (System.Windows.Media.Brush)FindResource(error ? "Danger" : "Muted");
        }

        private void UpdateAddEnabled()
        {
            var text = PathBox.Text.Trim();
            var ok = false;
            try
            {
                var folder = Environment.ExpandEnvironmentVariables(text);
                ok = text.Length > 0 && Directory.Exists(folder)
                     && !_existing.Contains(Normalize(folder))
                     && NameBox.Text.Trim().Length > 0;
            }
            catch (ArgumentException)
            {
            }
            AddButton.IsEnabled = ok;
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text.Trim();
            var workspace = new Workspace { Name = NameBox.Text.Trim(), Path = path };
            var options = DetectionList.ItemsSource as IEnumerable<DetectionOption>;
            if (options != null)
            {
                foreach (var option in options.Where(o => o.IsChecked))
                    workspace.Bindings.Add(new ToolBinding { ToolId = option.Detection.Kind, Cwd = option.Detection.Cwd });
            }
            Result = workspace;
            DialogResult = true;
        }

        private static string Normalize(string path)
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
