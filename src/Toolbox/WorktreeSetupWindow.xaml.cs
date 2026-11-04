using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>
    /// Cấu hình worktree của workspace (file local và lệnh cài đặt) và chuẩn bị một worktree cụ thể.
    /// Cùng logic với MCP prepare_worktree (<see cref="WorktreePreparer"/>).
    /// </summary>
    public partial class WorktreeSetupWindow : Window
    {
        private static readonly Dictionary<string, string> StatusText = new Dictionary<string, string>
        {
            ["copied"] = "Đã copy",
            ["applied"] = "Đã áp diff",
            ["unchanged"] = "Giữ nguyên",
            ["skipped"] = "Bỏ qua",
            ["conflict"] = "Lệch",
            ["missing"] = "Thiếu",
            ["error"] = "Lỗi",
        };

        private readonly Workspace _workspace;
        private readonly Action<string, string> _startInstall;

        /// <param name="startInstall">Gọi với (đường dẫn worktree, lệnh cài đặt) để chạy lệnh trong tab Tác vụ.</param>
        public WorktreeSetupWindow(Workspace workspace, Action<string, string> startInstall)
        {
            InitializeComponent();
            _workspace = workspace;
            _startInstall = startInstall;
            Title = "Worktree · " + workspace.Name;
            FilesBox.Text = string.Join(Environment.NewLine, workspace.Worktree?.Files ?? new List<string>());
            CommandBox.Text = workspace.Worktree?.Command ?? "";
        }

        /// <summary>Danh sách file sau khi bấm lưu.</summary>
        public List<string> Files { get; private set; } = new List<string>();

        /// <summary>Lệnh cài đặt sau khi bấm lưu.</summary>
        public string Command { get; private set; } = "";

        private static List<string> ParseFiles(string text) =>
            text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Files = ParseFiles(FilesBox.Text);
            Command = CommandBox.Text.Trim();
            DialogResult = true;
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục worktree" })
            {
                if (Directory.Exists(PathBox.Text))
                    dialog.SelectedPath = PathBox.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PathBox.Text = dialog.SelectedPath;
            }
        }

        private void OnPathChanged(object sender, TextChangedEventArgs e) =>
            PrepareButton.IsEnabled = Directory.Exists(PathBox.Text.Trim());

        private void OnPrepare(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text.Trim();
            // Dùng nội dung đang nhập (chưa lưu) để chuẩn bị, nên kiểm tra được trước khi lưu.
            var draft = new Workspace
            {
                Name = _workspace.Name,
                Path = _workspace.Path,
                Worktree = new WorktreeSetup { Files = ParseFiles(FilesBox.Text), Command = CommandBox.Text.Trim() },
            };
            var report = WorktreePreparer.Prepare(draft, path);
            if (report.Error != null)
            {
                ReportText.Text = report.Error;
                return;
            }

            var lines = report.Lines.Select(l => $"{Label(l.Status)}: {l.File}" + (l.Detail.Length > 0 ? " — " + l.Detail : ""));
            var text = string.Join(Environment.NewLine, lines);
            ReportText.Text = text.Length == 0 ? "Chưa có file nào trong danh sách." : text;
            ReportText.Text += report.Ok
                ? Environment.NewLine + "Chuẩn bị xong."
                : Environment.NewLine + "Có file cần xem tay trước khi chạy e2e.";

            if (report.Ok && InstallBox.IsChecked == true && draft.Worktree.Command.Length > 0)
            {
                _startInstall(path, draft.Worktree.Command);
                ReportText.Text += Environment.NewLine + "Đã chạy lệnh cài đặt ở tab Tác vụ.";
            }
        }

        private static string Label(string status) => StatusText.TryGetValue(status, out var text) ? text : status;
    }
}
