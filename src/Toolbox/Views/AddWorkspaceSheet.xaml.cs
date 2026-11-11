using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>
    /// Thêm workspace từ một thư mục: sau khi chọn thư mục hiện "Tìm thấy trong repo" (tool, thư mục chạy, số lệnh),
    /// tích sẵn, một nút "Thêm và gắn". Không nhận ra gì thì vẫn thêm được (gắn tool, thêm lệnh riêng hoặc nhờ AI thiết lập ở trang workspace).
    /// </summary>
    public partial class AddWorkspaceSheet : SheetBase
    {
        private readonly HashSet<string> _existing;
        private bool _nameTouched;
        private bool _settingName;

        public override double SheetWidth => 560;

        /// <param name="existingPaths">Đường dẫn các workspace đã có, để chặn thêm trùng thư mục.</param>
        public AddWorkspaceSheet(IEnumerable<string> existingPaths)
        {
            InitializeComponent();
            SheetTitle = "Thêm workspace";
            _existing = new HashSet<string>(existingPaths.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            Loaded += (s, e) => PathBox.Focus();
        }

        /// <summary>Workspace mới (chưa có Id/màu, <see cref="WorkspaceStore.Add"/> cấp). Có khi sheet xác nhận.</summary>
        public Workspace Result { get; private set; }

        public sealed class DetectionOption
        {
            public DetectionOption(Detection detection, string folder)
            {
                Detection = detection;
                Title = ToolPresets.Find(detection.Kind)?.Name ?? detection.Kind;
                Reason = detection.Reason;
                IsChecked = true;
                var preview = new Workspace
                {
                    Path = folder,
                    Bindings = new List<ToolBinding> { new ToolBinding { ToolId = detection.Kind, Cwd = detection.Cwd } },
                };
                try
                {
                    Commands = QuickCommands.For(preview).Count;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    Commands = 0;
                }
            }

            public Detection Detection { get; }
            public string Title { get; }
            public string Reason { get; }
            public int Commands { get; }
            public bool IsChecked { get; set; }

            /// <summary>Phần nối sau tên tool: số lệnh và thư mục chạy.</summary>
            public string Summary =>
                (Commands > 0 ? "  ·  " + Commands + " lệnh" : "")
                + (string.IsNullOrEmpty(Detection.Cwd) ? "" : "  ·  chạy ở " + Detection.Cwd + "\\");
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
                SetHint("Chọn thư mục repo, Toolbox tự tìm tool và lệnh chạy được.", error: false);
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

            var found = WorkspaceDetector.Detect(folder).Select(d => new DetectionOption(d, folder)).ToList();
            DetectionList.ItemsSource = found;
            NoDetection.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoDetection.Text = "Không nhận ra tool nào. Vẫn thêm được; gắn tool, thêm lệnh riêng hoặc nhờ AI thiết lập ở trang workspace.";
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
            var workspace = new Workspace { Name = NameBox.Text.Trim(), Path = PathBox.Text.Trim() };
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
