using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;
using Toolbox.Views;

namespace Toolbox
{
    /// <summary>Thêm hoặc sửa một lệnh riêng của workspace.</summary>
    public partial class CommandEditWindow : Window
    {
        private readonly Workspace _workspace;
        private readonly CustomCommand _existing;

        /// <param name="existing">Có giá trị thì là sửa (giữ nguyên Id); null là thêm mới.</param>
        public CommandEditWindow(Workspace workspace, CustomCommand existing = null)
        {
            InitializeComponent();
            _workspace = workspace;
            _existing = existing;
            if (existing != null)
            {
                Title = "Sửa lệnh riêng";
                LabelBox.Text = existing.Label;
                CommandBox.Text = existing.CommandLine;
                CwdBox.Text = existing.Cwd ?? "";
                WindowCheck.IsChecked = existing.Window;
            }
            Loaded += (s, e) => LabelBox.Focus();
            UpdateState();
        }

        /// <summary>Lệnh sau khi lưu; Id giữ nguyên khi sửa, rỗng khi thêm mới (người gọi cấp).</summary>
        public CustomCommand Result { get; private set; }

        private void OnChanged(object sender, TextChangedEventArgs e)
        {
            // TextChanged bắn trong InitializeComponent khi các ô khác chưa dựng xong.
            if (OkButton != null && CwdHint != null)
                UpdateState();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục chạy lệnh" })
            {
                var current = TryResolve(out var full) ? full : null;
                dialog.SelectedPath = current != null && Directory.Exists(current) ? current : _workspace.ResolvedPath;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                CwdBox.Text = WorkspacePaths.ToRelative(_workspace.ResolvedPath, dialog.SelectedPath);
            }
        }

        private void UpdateState()
        {
            var cwdOk = TryResolve(out var full) && Directory.Exists(full);
            CwdHint.Text = cwdOk ? "Chạy tại: " + full : "Không tìm thấy thư mục: " + full;
            CwdHint.Foreground = (System.Windows.Media.Brush)FindResource(cwdOk ? "Muted" : "Danger");
            OkButton.IsEnabled = cwdOk && LabelBox.Text.Trim().Length > 0 && CommandBox.Text.Trim().Length > 0;
        }

        private bool TryResolve(out string full)
        {
            try
            {
                full = _workspace.ResolveCwd(CwdBox.Text.Trim());
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                full = CwdBox.Text.Trim();
                return false;
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Result = new CustomCommand
            {
                Id = _existing?.Id,
                Label = LabelBox.Text.Trim(),
                CommandLine = CommandBox.Text.Trim(),
                Cwd = CwdBox.Text.Trim(),
                Window = WindowCheck.IsChecked == true,
            };
            DialogResult = true;
        }
    }
}
