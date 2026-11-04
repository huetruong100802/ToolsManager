using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;
using Toolbox.Views;

namespace Toolbox
{
    /// <summary>Gắn thêm một tool vào workspace, hoặc đổi thư mục chạy của tool đã gắn.</summary>
    public partial class AddToolWindow : Window
    {
        private readonly Workspace _workspace;
        private readonly bool _editing;
        private readonly IReadOnlyList<Detection> _detected;
        private bool _cwdTouched;
        private bool _settingCwd;

        /// <param name="existing">Có giá trị thì là sửa thư mục chạy của binding này (không đổi được tool).</param>
        public AddToolWindow(Workspace workspace, ToolBinding existing = null)
        {
            InitializeComponent();
            _workspace = workspace;
            _editing = existing != null;
            _detected = workspace.PathExists ? WorkspaceDetector.Detect(workspace.ResolvedPath) : new List<Detection>();

            IEnumerable<ToolDef> kinds = _editing
                ? ToolCatalog.All.Where(t => t.Kind == existing.Kind)
                : ToolCatalog.All.Where(t => workspace.Binding(t.Kind) == null);
            KindCombo.ItemsSource = kinds.ToList();

            if (_editing)
            {
                Title = "Đổi thư mục chạy";
                OkButton.Content = "Lưu";
                KindCombo.IsEnabled = false;
                _cwdTouched = true;
                SetCwd(existing.Cwd);
            }
            KindCombo.SelectedIndex = KindCombo.Items.Count > 0 ? 0 : -1;
            if (KindCombo.Items.Count == 0)
            {
                KindHint.Text = "Workspace đã gắn đủ mọi tool có sẵn.";
                OkButton.IsEnabled = false;
            }
        }

        public ToolBinding Result { get; private set; }

        private ToolDef SelectedTool => KindCombo.SelectedItem as ToolDef;

        private void OnKindChanged(object sender, SelectionChangedEventArgs e)
        {
            var tool = SelectedTool;
            if (tool == null)
                return;

            var hit = _detected.FirstOrDefault(d => d.Kind == tool.Kind);
            KindHint.Text = hit != null ? "Nhận diện được: " + hit.Reason : $"Áp dụng cho dự án có {tool.Rule}.";
            if (!_cwdTouched)
                SetCwd(hit?.Cwd);
            UpdateCwdHint();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục chạy của tool" })
            {
                var current = _workspace.ResolveCwd(CwdBox.Text.Trim());
                dialog.SelectedPath = Directory.Exists(current) ? current : _workspace.ResolvedPath;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                _cwdTouched = true;
                CwdBox.Text = WorkspacePaths.ToRelative(_workspace.ResolvedPath, dialog.SelectedPath);
            }
        }

        private void OnCwdChanged(object sender, TextChangedEventArgs e)
        {
            if (!_settingCwd)
                _cwdTouched = true;
            UpdateCwdHint();
        }

        private void SetCwd(string cwd)
        {
            _settingCwd = true;
            CwdBox.Text = cwd ?? "";
            _settingCwd = false;
        }

        private void UpdateCwdHint()
        {
            var ok = CwdExists(out var full);
            CwdHint.Text = ok ? "Chạy tại: " + full : "Không tìm thấy thư mục: " + full;
            CwdHint.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Muted" : "Danger");
            OkButton.IsEnabled = SelectedTool != null && ok;
        }

        private bool CwdExists(out string full)
        {
            try
            {
                full = _workspace.ResolveCwd(CwdBox.Text.Trim());
                return Directory.Exists(full);
            }
            catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.NotSupportedException || ex is PathTooLongException)
            {
                full = CwdBox.Text.Trim();
                return false;
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var tool = SelectedTool;
            if (tool == null || !CwdExists(out _))
                return;
            Result = new ToolBinding { Kind = tool.Kind, Cwd = CwdBox.Text.Trim() };
            DialogResult = true;
        }
    }
}
