using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>
    /// Kết nối Toolbox vào Claude Code bằng một nút: toàn máy (scope user) hoặc từng workspace (scope local).
    /// Client khác vẫn khai báo tay.
    /// </summary>
    public partial class AgentWindow : Window
    {
        private readonly Installer _installer;
        private readonly string _currentExe = Process.GetCurrentProcess().MainModule.FileName;
        private List<TargetRow> _workspaces = new List<TargetRow>();
        private string _exe;

        public AgentWindow(Installer installer)
        {
            InitializeComponent();
            _installer = installer;

            var jsonExe = _currentExe.Replace("\\", "\\\\");
            DesktopConfig.Text =
$@"{{
  ""mcpServers"": {{
    ""toolbox"": {{ ""command"": ""{jsonExe}"", ""args"": [""--mcp""] }}
  }}
}}";
            VsCodeConfig.Text =
$@"{{
  ""servers"": {{
    ""toolbox"": {{ ""type"": ""stdio"", ""command"": ""{jsonExe}"", ""args"": [""--mcp""] }}
  }}
}}";
            Loaded += (s, e) => Reload();
        }

        /// <summary>Người dùng đã chọn cài Toolbox vào máy trong cửa sổ này.</summary>
        public bool InstalledNow { get; private set; }

        private void Reload()
        {
            ClaudeConfig config;
            try
            {
                config = ClaudeCode.ReadConfig(ClaudeCode.DefaultConfigPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(this, ex.Message);
                config = new ClaudeConfig();
            }

            // Bản đang chạy hoặc bản đã cài đều tính là "đúng exe".
            var exes = new[] { _currentExe, _installer.InstalledExe };
            var user = new TargetRow(config.User, "Toàn bộ máy", "Mọi workspace Claude Code đều dùng được (scope user)", exes);
            UserRow.Content = user;
            UserHint.Visibility = user.Link == AgentLink.Connected ? Visibility.Visible : Visibility.Collapsed;

            _workspaces = config.Workspaces
                .Select(w => new TargetRow(w, Path.GetFileName(w.Workspace), w.Workspace, exes))
                .OrderBy(r => r.Link == AgentLink.NotConnected)
                .ThenBy(r => r.Target.Workspace, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = Text.Fold(FilterBox.Text.Trim());
            WorkspaceList.ItemsSource = query.Length == 0 ? _workspaces : _workspaces.Where(r => Text.Fold(r.Target.Workspace).Contains(query)).ToList();
            EmptyText.Visibility = _workspaces.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private async void OnConnect(object sender, RoutedEventArgs e) => await Connect(RowOf(sender).Target);

        private async void OnDisconnect(object sender, RoutedEventArgs e)
        {
            var target = RowOf(sender).Target;
            await RunCli(cli => ClaudeCode.Disconnect(cli, target), null);
        }

        private async void OnPickWorkspace(object sender, RoutedEventArgs e)
        {
            string path;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục workspace cho Claude Code" })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                path = dialog.SelectedPath;
            }

            var existing = _workspaces.FirstOrDefault(r => string.Equals(r.Target.Workspace, path, StringComparison.OrdinalIgnoreCase));
            await Connect(existing?.Target ?? new ClaudeTarget { Workspace = path });
        }

        private async Task Connect(ClaudeTarget target)
        {
            var exe = ChooseExe();
            if (exe != null)
                await RunCli(cli => ClaudeCode.Connect(cli, target, exe), "claude " + ClaudeCode.AddArguments(target.Workspace, exe));
        }

        /// <summary>
        /// Khai báo bằng exe ở chỗ cố định. Đang chạy bản tải về thì đề nghị cài trước, tránh agent mất kết nối khi file bị xoá/di chuyển.
        /// Hỏi một lần cho mỗi lần mở cửa sổ.
        /// </summary>
        private string ChooseExe()
        {
            if (_exe != null)
                return _exe;
            if (_installer.IsInstalledCopy(_currentExe))
                return _exe = _currentExe;

            var answer = MessageBox.Show(this,
                $"Toolbox đang chạy từ:\n{_currentExe}\n\nNên cài vào máy trước, để agent không mất kết nối khi file này bị xoá hoặc di chuyển.\n\n"
                + "Yes: cài rồi kết nối bản đã cài\nNo: kết nối thẳng file đang chạy",
                "Toolbox", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return null;
            if (answer == MessageBoxResult.No)
                return _exe = _currentExe;

            try
            {
                _exe = _installer.Install(_currentExe);
                InstalledNow = true;
                return _exe;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException)
            {
                Dialogs.Error(this, "Không cài được: " + ex.Message + "\n\nNếu Toolbox đã cài đang mở, hãy đóng nó rồi thử lại.");
                return null;
            }
        }

        /// <param name="manualCommand">Lệnh để người dùng tự chạy khi máy không có CLI claude; null nếu không cần.</param>
        private async Task RunCli(Action<string> action, string manualCommand)
        {
            var cli = ClaudeCode.FindCli();
            if (cli == null)
            {
                Dialogs.Error(this, "Không tìm thấy lệnh claude (Claude Code CLI) trên máy. Cài Claude Code rồi thử lại."
                    + (manualCommand != null ? "\n\nHoặc tự chạy trong terminal (Ctrl+C để copy hộp thoại này):\n" + manualCommand : ""));
                return;
            }

            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await Task.Run(() => action(cli));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception || ex is IOException)
            {
                Dialogs.Error(this, ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                IsEnabled = true;
                Reload();
            }
        }

        private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(((TextBox)((Button)sender).Tag).Text);

        private static TargetRow RowOf(object sender) => (TargetRow)((FrameworkElement)sender).DataContext;

        public sealed class TargetRow
        {
            public TargetRow(ClaudeTarget target, string title, string detail, string[] toolboxExes)
            {
                Target = target;
                Title = title;
                Detail = detail;
                Link = target.LinkTo(toolboxExes);
            }

            public ClaudeTarget Target { get; }
            public string Title { get; }
            public string Detail { get; }
            public AgentLink Link { get; }
            public string ToolTip => Target.Command == null ? null : "Khai báo hiện tại: " + Target.Command;
        }
    }
}
