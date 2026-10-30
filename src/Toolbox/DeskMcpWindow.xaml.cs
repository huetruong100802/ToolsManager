using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// Kết nối desk-mcp vào Claude Code: toàn máy (scope user) hoặc từng workspace (scope local), và gỡ kết nối.
    /// Khác AgentWindow ở chỗ không cần exe: lệnh khởi chạy là npx.
    /// </summary>
    public partial class DeskMcpWindow : Window
    {
        private List<TargetRow> _workspaces = new List<TargetRow>();

        public DeskMcpWindow()
        {
            InitializeComponent();
            NpxHint.Visibility = DeskMcp.NpxAvailable() ? Visibility.Collapsed : Visibility.Visible;
            Loaded += (s, e) => Reload();
        }

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

            var user = new TargetRow(config.User, DeskMcp.ServerName, "Toàn bộ máy", "Mọi workspace Claude Code đều dùng được (scope user)");
            UserRow.Content = user;

            _workspaces = config.Workspaces
                .Select(w => new TargetRow(w, DeskMcp.ServerName, Path.GetFileName(w.Workspace), w.Workspace))
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
            await RunCli(cli => ClaudeCode.Disconnect(cli, target, DeskMcp.ServerName), null);
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
            if (!DeskMcp.NpxAvailable())
            {
                Dialogs.Error(this, "Không tìm thấy npx trên máy. desk-mcp chạy bằng npx nên cần Node.js 20+ trước.");
                return;
            }
            await RunCli(cli => ClaudeCode.Connect(cli, target, DeskMcp.ServerName, DeskMcp.CommandArgs),
                "claude " + ClaudeCode.AddArguments(DeskMcp.ServerName, target.Workspace, DeskMcp.CommandArgs));
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

        private static TargetRow RowOf(object sender) => (TargetRow)((FrameworkElement)sender).DataContext;
    }
}
