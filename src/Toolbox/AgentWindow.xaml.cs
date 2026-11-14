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
using System.Windows.Threading;
using Toolbox.Core;
using Toolbox.Core.AgentClients;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>
    /// Kết nối Toolbox vào app agent bất kỳ: chọn app ở cột trái (Claude Code, Cursor, VS Code, Claude Desktop, Codex, Gemini, Windsurf,
    /// hoặc agent khác tự dán cấu hình), rồi kết nối từng workspace hoặc toàn máy. Mỗi kết nối là một agent trong agents.json;
    /// dòng đã kết nối cho biết agent đã thực sự gọi tới Toolbox chưa (cập nhật theo nhịp khi cửa sổ mở).
    /// </summary>
    public partial class AgentWindow : Window
    {
        private readonly Installer _installer;
        private readonly string _currentExe = Process.GetCurrentProcess().MainModule.FileName;
        private readonly string _workspacePath;
        private readonly List<ClientCard> _cards;
        private readonly DispatcherTimer _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private List<TargetRow> _workspaces = new List<TargetRow>();
        private TargetRow _userRow;
        private string _exe;

        /// <param name="workspacePath">Mở từ trang một workspace: lọc sẵn về workspace đó.</param>
        public AgentWindow(Installer installer, string workspacePath = null)
        {
            InitializeComponent();
            _installer = installer;
            _workspacePath = workspacePath;
            _cards = AgentClientCatalog.CreateDefault().Select(c => new ClientCard(c)).ToList();
            ClientList.ItemsSource = _cards;
            _presenceTimer.Tick += (s, e) => UpdatePresence();

            Loaded += (s, e) =>
            {
                if (!string.IsNullOrEmpty(workspacePath))
                    FilterBox.Text = workspacePath;
                RefreshCards();
                // Mở ở app đang dùng: app có kết nối trước, rồi app đã cài, theo thứ tự danh mục.
                ClientList.SelectedItem = _cards.FirstOrDefault(c => c.HasConnections && !c.Client.IsManual)
                    ?? _cards.FirstOrDefault(c => c.IsInstalled && !c.Client.IsManual)
                    ?? _cards[0];
                _presenceTimer.Start();
            };
            Closed += (s, e) => _presenceTimer.Stop();
        }

        /// <summary>Người dùng đã chọn cài Toolbox vào máy trong cửa sổ này.</summary>
        public bool InstalledNow { get; private set; }

        private AgentClient Client => (ClientList.SelectedItem as ClientCard)?.Client;

        /// <summary>Bản đang chạy hoặc bản đã cài đều tính là "đúng exe".</summary>
        private string[] Exes => new[] { _currentExe, _installer.InstalledExe };

        private void OnClientChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Client == null)
                return;
            Reload();
        }

        private void Reload()
        {
            var client = Client;
            client.Invalidate();
            HeaderMonogram.Text = client.Monogram;
            HeaderName.Text = client.Name;
            HeaderTagline.Text = client.Tagline;
            FooterHint.Text = client.IsManual
                ? "Agent khác: Toolbox tạo agent (có quyền, nhật ký riêng) rồi copy cấu hình; bạn tự dán vào agent đó."
                : client.RestartHint + " Agent vẫn hỏi bạn trước khi gọi tool, theo cấu hình của app.";
            NotInstalledBanner.Visibility = client.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
            NotInstalledText.Text = $"Chưa thấy {client.Name} trên máy này. Vẫn kết nối được: Toolbox ghi cấu hình sẵn, cài {client.Name} xong là dùng.";
            OpenConfigButton.Visibility = client.ConfigPath(null) == null ? Visibility.Collapsed : Visibility.Visible;
            AgentNameHint.Text = $"Để trống: \"{client.Name} · <tên workspace>\"";
            ScopeHint.Text = client.IsManual
                ? "Chọn workspace để tạo agent và copy cấu hình."
                : client.HasProjectConfig
                    ? "Agent chỉ thấy tool, profile, workflow của workspace đó."
                    : $"{client.Name} chỉ có cấu hình toàn máy: mỗi workspace là một server riêng (toolbox-<tên>).";
            ManualExpander.Visibility = client.IsManual ? Visibility.Collapsed : Visibility.Visible;
            var snippetExe = File.Exists(_installer.InstalledExe) ? _installer.InstalledExe : _currentExe;
            SnippetBox.Text = client.IsManual ? "" : client.Snippet(null, AgentClient.ServerName, snippetExe, new[] { "--mcp" });

            var registered = LoadRegistered();
            var global = ConnectTarget.Global;
            _userRow = new TargetRow(client, global, "Toàn bộ máy", "Agent quản lý mọi tool trong thư viện, không giới hạn workspace",
                AgentConnections.State(client, global, Exes));
            UserRow.Content = _userRow;
            if (_userRow.Link != AgentLink.NotConnected)
                AdvancedExpander.IsExpanded = true;

            // Workspace đã đăng ký trong Toolbox, cộng thư mục app tự nhớ (Claude Code) chưa đăng ký.
            var paths = registered.Where(w => w.PathExists).Select(w => w.ResolvedPath).ToList();
            IEnumerable<string> known;
            try { known = client.KnownWorkspaces().ToList(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { known = new string[0]; }
            paths.AddRange(known.Where(k => !paths.Any(p => SamePath(p, k))));
            if (!string.IsNullOrEmpty(_workspacePath) && !paths.Any(p => SamePath(p, _workspacePath)))
                paths.Add(_workspacePath);

            _workspaces = paths.Select(path =>
                {
                    var match = registered.FirstOrDefault(r => SamePath(r.ResolvedPath, path));
                    var target = new ConnectTarget { Path = path, WorkspaceId = match?.Id, Name = match?.Name ?? Path.GetFileName(path.TrimEnd('\\', '/')) };
                    var detail = (match == null ? "Chưa đăng ký trong Toolbox · " : "") + path;
                    return new TargetRow(client, target, target.Name, detail, AgentConnections.State(client, target, Exes));
                })
                .OrderBy(r => r.Link == AgentLink.NotConnected)
                .ThenBy(r => r.Target.WorkspaceId == null)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();
            UpdatePresence();
            RefreshCards();
        }

        /// <summary>Dòng trạng thái của từng app ở cột trái: đã cài chưa, đang kết nối mấy nơi.</summary>
        private void RefreshCards()
        {
            List<Workspace> registered;
            try { registered = WorkspaceStore.Load(WorkspaceStore.DefaultPath).Workspaces.Where(w => w.PathExists).ToList(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { registered = new List<Workspace>(); }
            var agents = LoadAgents();

            foreach (var card in _cards)
            {
                var client = card.Client;
                int count;
                if (client.IsManual)
                {
                    count = agents.Count(a => a.Client == client.Id);
                    card.Status = count == 0 ? "Copy cấu hình MCP" : $"{count} agent tự khai báo";
                }
                else
                {
                    var targets = new[] { ConnectTarget.Global }
                        .Concat(registered.Select(w => new ConnectTarget { Path = w.ResolvedPath, WorkspaceId = w.Id, Name = w.Name }));
                    count = targets.Count(t => AgentConnections.State(client, t, Exes).Link == AgentLink.Connected);
                    card.Status = count > 0 ? (count == 1 ? "Đã kết nối 1 nơi" : $"Đã kết nối {count} nơi") : card.IsInstalled ? "Đã cài · chưa kết nối" : "Chưa thấy trên máy";
                }
                card.HasConnections = count > 0;
                card.IsLive = agents.Any(a => a.Client == client.Id && IsLive(a));
            }
        }

        private void UpdatePresence()
        {
            var client = Client;
            if (client == null)
                return;
            var all = LoadAgents();
            var agents = all.Where(a => a.Client == client.Id).ToList();
            var now = DateTime.UtcNow;
            foreach (var row in _workspaces.Concat(new[] { _userRow }).Where(r => r != null))
                row.UpdatePresence(agents.FirstOrDefault(a => SameScope(a.WorkspaceId, row.Target.WorkspaceId)), now);
            foreach (var card in _cards)
                card.IsLive = all.Any(a => a.Client == card.Client.Id && IsLive(a));
        }

        private List<AgentRecord> _agentsCache = new List<AgentRecord>();

        /// <summary>agents.json; đọc lỗi (đang bị ghi…) thì giữ bản đọc trước.</summary>
        private List<AgentRecord> LoadAgents()
        {
            try { _agentsCache = AgentStore.Load(AgentStore.DefaultPath).Agents.ToList(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { /* giữ bản trước */ }
            return _agentsCache;
        }

        private static bool IsLive(AgentRecord agent) =>
            DateTime.TryParse(agent.LastSeenAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var seen)
            && DateTime.UtcNow - seen.ToUniversalTime() < TargetRow.LiveWindow;

        private void ApplyFilter()
        {
            var query = Text.Fold(FilterBox.Text.Trim());
            WorkspaceList.ItemsSource = query.Length == 0
                ? _workspaces
                : _workspaces.Where(r => Text.Fold(r.Target.Path).Contains(query) || Text.Fold(r.Title).Contains(query)).ToList();
            EmptyText.Visibility = _workspaces.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private async void OnConnect(object sender, RoutedEventArgs e) => await Connect(RowOf(sender).Target);

        private async void OnDisconnect(object sender, RoutedEventArgs e)
        {
            var client = Client;
            var target = RowOf(sender).Target;
            if (await Busy(() => AgentConnections.Disconnect(client, target)))
                Dialogs.Info($"Đã huỷ kết nối {client.Name} · {Label(target)}.");
        }

        private async void OnPickWorkspace(object sender, RoutedEventArgs e)
        {
            string path;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = $"Chọn thư mục workspace cho {Client?.Name}" })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                path = dialog.SelectedPath;
            }
            var existing = _workspaces.FirstOrDefault(r => SamePath(r.Target.Path, path));
            await Connect(existing?.Target ?? new ConnectTarget { Path = path, Name = Path.GetFileName(path.TrimEnd('\\', '/')) });
        }

        private void OnOpenConfig(object sender, RoutedEventArgs e)
        {
            var path = Client?.ConfigPath(null);
            if (path == null)
                return;
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(Path.GetDirectoryName(path)))
                    Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
                else
                    Dialogs.Info($"Chưa có file cấu hình. Toolbox sẽ tạo {path} khi kết nối.");
            }
            catch (Win32Exception ex)
            {
                Dialogs.Error(this, ex.Message);
            }
        }

        private void OnCopySnippet(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(SnippetBox.Text);
            Dialogs.Info("Đã copy cấu hình.");
        }

        /// <summary>
        /// Toàn máy: <c>--mcp</c>, agent quản lý mọi tool. Workspace: đăng ký trong Toolbox nếu chưa có, rồi kết nối với
        /// <c>--workspace &lt;id&gt;</c>. Mỗi (app, phạm vi) có một agent riêng, truyền bằng <c>--agent &lt;id&gt;</c>.
        /// </summary>
        private async Task Connect(ConnectTarget target)
        {
            var client = Client;
            var exe = ChooseExe();
            if (exe == null)
                return;

            if (!target.IsGlobal && string.IsNullOrEmpty(target.WorkspaceId))
            {
                var registered = EnsureRegistered(target.Path);
                if (registered == null)
                    return;
                target = new ConnectTarget { Path = target.Path, WorkspaceId = registered.Id, Name = registered.Name };
            }

            var runMode = RunMode.SelectedIndex == 2 ? RunModes.None : RunMode.SelectedIndex == 1 ? RunModes.Ask : RunModes.Auto;
            var agentId = EnsureAgent(client, target, AgentName.Text, runMode);

            if (client.IsManual)
            {
                Clipboard.SetText(AgentConnections.Snippet(client, target, exe, agentId));
                Dialogs.Success($"Đã copy cấu hình cho {Label(target)}. Dán vào phần MCP của agent rồi khởi động lại agent đó.");
                Reload();
                return;
            }

            string note = null;
            if (await Busy(() => note = AgentConnections.Connect(client, target, exe, agentId)))
                Dialogs.Success($"Đã kết nối {client.Name} · {Label(target)}. {client.RestartHint}" + (note != null ? "\n" + note : ""), seconds: 8);
        }

        private static string Label(ConnectTarget target) => target.IsGlobal ? "toàn máy" : target.Name;

        /// <summary>Agent đã đăng ký cho (app, phạm vi); chưa có thì tạo. Null nếu không ghi được agents.json.</summary>
        private string EnsureAgent(AgentClient client, ConnectTarget target, string name, string runMode)
        {
            try
            {
                string id = null;
                AgentStore.Update(AgentStore.DefaultPath, store =>
                {
                    var existing = store.Agents.FirstOrDefault(a => a.Client == client.Id && SameScope(a.WorkspaceId, target.WorkspaceId));
                    if (existing != null)
                    {
                        id = existing.Id;
                        return false;
                    }
                    var agentName = string.IsNullOrWhiteSpace(name) ? client.Name + " · " + Label(target) : name.Trim();
                    id = store.Add(new AgentRecord { Name = agentName, Client = client.Id, WorkspaceId = target.WorkspaceId, RunMode = runMode }).Id;
                    return true;
                });
                return id;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(this, "Không ghi được danh sách agent, kết nối sẽ không có quyền riêng: " + ex.Message);
                return null;
            }
        }

        private List<Workspace> LoadRegistered()
        {
            try
            {
                return WorkspaceStore.Load(WorkspaceStore.DefaultPath).Workspaces.ToList();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialogs.Error(this, ex.Message);
                return new List<Workspace>();
            }
        }

        /// <summary>Workspace Toolbox của thư mục này; chưa có thì hỏi rồi đăng ký. Null nếu người dùng bỏ qua hoặc lỗi.</summary>
        private Workspace EnsureRegistered(string path)
        {
            try
            {
                var store = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
                var existing = store.Workspaces.FirstOrDefault(w => SamePath(w.ResolvedPath, path));
                if (existing != null)
                    return existing;

                var answer = MessageBox.Show(this,
                    $"Thư mục này chưa là workspace trong Toolbox:\n{path}\n\n"
                    + "Agent chỉ quản lý được tool của workspace đã đăng ký. Đăng ký workspace này rồi kết nối?",
                    "Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return null;

                var name = Path.GetFileName(path.TrimEnd('\\', '/'));
                var added = store.Add(new Workspace { Name = string.IsNullOrWhiteSpace(name) ? path : name, Path = path });
                store.Save();
                return added;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Dialogs.Error(this, "Không đăng ký được workspace: " + ex.Message);
                return null;
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals((a ?? "").TrimEnd('\\', '/'), (b ?? "").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        private static bool SameScope(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>Chạy việc ghi cấu hình ở luồng nền (CLI claude có thể mất vài giây), báo lỗi, rồi đọc lại trạng thái. true nếu xong không lỗi.</summary>
        private async Task<bool> Busy(Action action)
        {
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await Task.Run(action);
                return true;
            }
            catch (ClientUnavailableException ex)
            {
                Dialogs.Error(this, ex.Message + "\n\n(Ctrl+C để copy hộp thoại này)\n" + ex.ManualStep);
                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception || ex is IOException
                || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException)
            {
                Dialogs.Error(this, ex.Message);
                return false;
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
