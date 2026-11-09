using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Views;

namespace Toolbox
{
    /// <summary>Sửa phần riêng của workspace cho một tool đã gắn: thư mục chạy và tham số.</summary>
    public partial class ToolBindingWindow : Window
    {
        private readonly Workspace _workspace;
        private readonly ToolBinding _binding;
        private readonly ToolEntry _tool;

        public ToolBindingWindow(Workspace workspace, ToolBinding binding, ToolEntry tool)
        {
            InitializeComponent();
            _workspace = workspace;
            _binding = binding;
            _tool = tool;

            ToolNameText.Text = tool?.Name ?? binding.ToolId;
            CwdBox.Text = binding.Cwd ?? "";
            ArgsBox.Text = binding.Args ?? "";
            ArgsHint.Text = "Để trống thì dùng tham số đăng ký trong thư viện"
                + (string.IsNullOrWhiteSpace(tool?.Args) ? "." : ": " + tool.Args);
            UpdateCwdHint();
        }

        /// <summary>Binding sau khi sửa (cùng ToolId); null nếu huỷ.</summary>
        public ToolBinding Result { get; private set; }

        private string Cwd => CwdBox.Text.Trim();

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục chạy của tool" })
            {
                var current = FullCwd();
                dialog.SelectedPath = Directory.Exists(current) ? current : _workspace.ResolvedPath;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                CwdBox.Text = WorkspacePaths.ToRelative(_workspace.ResolvedPath, dialog.SelectedPath);
            }
        }

        private void OnCwdChanged(object sender, TextChangedEventArgs e) => UpdateCwdHint();

        private string FullCwd() =>
            CommandBuilder.ToolCwd(_workspace, new ToolBinding { ToolId = _binding.ToolId, Cwd = Cwd }, _tool);

        private void UpdateCwdHint()
        {
            // Có thể gọi khi các phần tử chưa dựng xong.
            if (CwdHint == null || OkButton == null)
                return;
            string full;
            bool ok;
            try
            {
                full = FullCwd();
                ok = Directory.Exists(full);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                full = Cwd;
                ok = false;
            }
            CwdHint.Text = ok ? "Chạy tại: " + full : "Không tìm thấy thư mục: " + full;
            CwdHint.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Muted" : "Danger");
            OkButton.IsEnabled = ok;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var args = ArgsBox.Text.Trim();
            var problem = CommandBuilder.CheckExtraArgs(args);
            if (problem != null)
            {
                Dialogs.Error(this, problem);
                return;
            }
            Result = new ToolBinding
            {
                ToolId = _binding.ToolId,
                Cwd = Cwd.Length == 0 ? null : Cwd,
                Args = args.Length == 0 ? null : args,
            };
            DialogResult = true;
        }
    }
}
