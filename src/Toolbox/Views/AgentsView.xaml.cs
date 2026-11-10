using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Trang Agent: danh sách agent và quyền, đề xuất chờ duyệt, nhật ký việc agent đã làm.</summary>
    public partial class AgentsView : UserControl
    {
        /// <summary>Số công cụ MCP agent thấy trong workspace (bộ công cụ gom, cố định).</summary>
        public const int ToolCountForAgent = 12;

        private static readonly TimeSpan ConnectedWindow = TimeSpan.FromMinutes(2);
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private string _tab = "agents";
        private string _expanded;
        private Dictionary<string, string> _workspaceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mở cửa sổ Kết nối agent.</summary>
        public event Action ConnectRequested;

        public AgentsView()
        {
            InitializeComponent();
            // Agent gửi đề xuất từ tiến trình khác: làm mới định kỳ khi trang đang hiện.
            _timer.Tick += (s, e) => { if (IsVisible) Refresh(); };
            _timer.Start();
        }

        private Window Host => Window.GetWindow(this);

        private void OnConnect(object sender, RoutedEventArgs e) => ConnectRequested?.Invoke();

        private void OnTab(object sender, RoutedEventArgs e)
        {
            _tab = (string)((FrameworkElement)sender).Tag;
            Refresh();
        }

        /// <summary>Số đề xuất đang chờ duyệt, để hiện trên thanh trái. Lỗi đọc file thì coi như 0.</summary>
        public static int PendingCount()
        {
            try { return ProposalStore.Load(ProposalStore.DefaultPath).Pending.Count(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return 0; }
        }

        public void Refresh()
        {
            try
            {
                _workspaceNames = WorkspaceStore.Load(WorkspaceStore.DefaultPath).Workspaces
                    .ToDictionary(w => w.Id, w => w.Name, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { }

            var pending = PendingCount();
            ProposalsTab.Content = pending > 0 ? $"Chờ duyệt ({pending})" : "Chờ duyệt";

            // Giữ vị trí cuộn khi làm mới định kỳ.
            var scroll = Body.Parent as ScrollViewer;
            var offset = scroll?.VerticalOffset ?? 0;
            Body.Children.Clear();
            try
            {
                if (_tab == "proposals")
                    ShowProposals();
                else if (_tab == "activity")
                    ShowActivity();
                else
                    ShowAgents();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Body.Children.Add(Hint("Không đọc được dữ liệu agent: " + ex.Message));
            }
            scroll?.ScrollToVerticalOffset(offset);
        }

        // ---------- Tab Agent ----------

        private void ShowAgents()
        {
            var agents = AgentStore.Load(AgentStore.DefaultPath).Agents;
            if (agents.Count == 0)
            {
                Body.Children.Add(Hint("Chưa có agent nào. Bấm \"Kết nối agent…\" để cho Claude Code hoặc agent khác dùng Toolbox qua MCP."));
                return;
            }

            foreach (var agent in agents)
            {
                Body.Children.Add(AgentCard(agent));
                if (string.Equals(agent.Id, _expanded, StringComparison.OrdinalIgnoreCase))
                    Body.Children.Add(PermissionPanel(agent));
            }
            Body.Children.Add(Hint("Agent \"toàn máy\" chỉ quản lý thư viện tool. Muốn agent làm việc trong một workspace thì kết nối theo workspace đó."));
        }

        private UIElement AgentCard(AgentRecord agent)
        {
            var connected = IsConnected(agent);
            var dock = new DockPanel();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Btn(agent.Id == _expanded ? "Đóng" : "Quyền…", "GhostButton", (s, e) =>
            {
                _expanded = agent.Id == _expanded ? null : agent.Id;
                Refresh();
            }));
            DockPanel.SetDock(buttons, Dock.Right);
            dock.Children.Add(buttons);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 0),
                Fill = connected ? Brush("Ok") : Brush("InputLine"),
            };
            DockPanel.SetDock(dot, Dock.Left);
            dock.Children.Add(dot);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Text(agent.Name, bold: true));
            title.Children.Add(Pill(ScopeText(agent), "AccentSoft", "Accent"));
            text.Children.Add(title);
            text.Children.Add(Text(
                (connected ? "Đang kết nối" : "Không kết nối") + " · " + (string.IsNullOrEmpty(agent.LastSeenAt) ? "chưa hoạt động" : "hoạt động " + Ago(agent.LastSeenAt))
                + " · " + RunModeText(agent.RunMode), size: 12, brush: "Muted"));
            dock.Children.Add(text);

            return Card(dock, bottom: 8);
        }

        private UIElement PermissionPanel(AgentRecord agent)
        {
            var panel = new StackPanel();
            panel.Children.Add(Section(agent.IsGlobal ? "PHẠM VI: TOÀN MÁY" : "TRONG WORKSPACE NÀY"));
            panel.Children.Add(agent.IsGlobal
                ? PermRow("Quản lý thư viện tool", "Thêm, sửa, xoá tool dùng chung. Không đụng profile hay workflow của workspace nào.", "cố định")
                : PermRow("Thêm, sửa, xoá lệnh riêng, profile, workflow, lệnh riêng", null, "luôn bật"));
            panel.Children.Add(Section("THƯ VIỆN TOOL"));
            panel.Children.Add(agent.IsGlobal
                ? PermRow("Xem, thêm, sửa, xoá tool dùng chung", null, "cố định")
                : PermRow("Xem thư viện, gắn tool có sẵn, thêm tool mới", null, "luôn bật"));
            if (!agent.IsGlobal)
                panel.Children.Add(PermRow("Sửa, xoá tool dùng chung", "Không trực tiếp. Agent chỉ gửi đề xuất để bạn duyệt.", "qua duyệt"));

            panel.Children.Add(Section("CHẠY LỆNH"));
            var auto = new RadioButton { Content = "Tự chạy profile, workflow và tool đã có", GroupName = "run-" + agent.Id, Margin = new Thickness(0, 2, 0, 2) };
            var ask = new RadioButton { Content = "Hỏi bạn trước mỗi lần chạy (lệnh được duyệt đúng một lần)", GroupName = "run-" + agent.Id, Margin = new Thickness(0, 2, 0, 2) };
            var none = new RadioButton { Content = "Không cho chạy", GroupName = "run-" + agent.Id, Margin = new Thickness(0, 2, 0, 2) };
            var current = RunModes.Normalize(agent.RunMode);
            auto.IsChecked = current == RunModes.Auto;
            ask.IsChecked = current == RunModes.Ask;
            none.IsChecked = current == RunModes.None;
            panel.Children.Add(auto);
            panel.Children.Add(ask);
            panel.Children.Add(none);

            panel.Children.Add(Section("CÔNG CỤ AGENT THẤY"));
            panel.Children.Add(Line(agent.IsGlobal
                ? "Bộ công cụ quản lý thư viện (5 công cụ)."
                : $"{ToolCountForAgent} công cụ, cố định: workspace có 5 hay 500 profile vẫn vậy. Agent tìm theo từ khoá và phân trang, không đọc cả danh sách."));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(Btn("Lưu quyền", "SoftButton", (s, e) =>
            {
                var mode = auto.IsChecked == true ? RunModes.Auto : ask.IsChecked == true ? RunModes.Ask : RunModes.None;
                AgentStore.Update(AgentStore.DefaultPath, store =>
                {
                    var found = store.Find(agent.Id);
                    if (found == null)
                        return false;
                    found.RunMode = mode;
                    return true;
                });
                Refresh();
            }));
            if (agent.AllowRuns != null && agent.AllowRuns.Count > 0)
                actions.Children.Add(Btn($"Quên {agent.AllowRuns.Count} lệnh đã cho phép", "GhostButton", (s, e) =>
                {
                    RunApprovals.ForgetAllowed(AgentStore.DefaultPath, agent.Id);
                    Dialogs.Success("Đã quên. Agent phải xin duyệt lại từng lệnh.", seconds: 5);
                    Refresh();
                }));
            actions.Children.Add(Btn("Gỡ agent", "DangerButton", (s, e) =>
            {
                if (!Dialogs.Confirm(Host, $"Gỡ agent \"{agent.Name}\" khỏi Toolbox?\n\nAgent này sẽ bị từ chối ở lần gọi sau. Nhật ký việc nó đã làm vẫn giữ."))
                    return;
                AgentStore.Update(AgentStore.DefaultPath, store => store.Remove(agent.Id));
                _expanded = null;
                Refresh();
            }));
            panel.Children.Add(actions);

            var wrap = Card(panel, bottom: 8);
            wrap.Margin = new Thickness(24, -2, 0, 8);
            wrap.Background = Brush("WindowBg");
            return wrap;
        }

        /// <summary>Một dòng quyền: tên quyền (và mô tả nếu có) bên trái, nhãn trạng thái bên phải.</summary>
        private UIElement PermRow(string title, string detail, string status)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(Text(title, wrap: true));
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(Text(detail, size: 12, brush: "Muted", wrap: true));
            grid.Children.Add(text);

            var tag = Pill(status, "Hover", "Muted");
            tag.Margin = new Thickness(12, 0, 0, 0);
            tag.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(tag, 1);
            grid.Children.Add(tag);
            return grid;
        }

        // ---------- Tab Chờ duyệt ----------

        private void ShowProposals()
        {
            var pending = ProposalStore.Load(ProposalStore.DefaultPath).Pending.OrderByDescending(p => p.CreatedAt).ToList();
            if (pending.Count == 0)
            {
                Body.Children.Add(Hint("Không có đề xuất nào đang chờ. Khi agent trong workspace muốn sửa hoặc xoá tool dùng chung, đề xuất sẽ hiện ở đây."));
                return;
            }

            ToolStore tools = null;
            try { tools = ToolStore.Load(ToolStore.DefaultPath); } catch (Exception ex) when (ex is InvalidDataException || ex is IOException) { }
            foreach (var proposal in pending)
                Body.Children.Add(ProposalCard(proposal, tools));
        }

        private UIElement ProposalCard(Proposal proposal, ToolStore tools)
        {
            var tool = tools?.Find(proposal.ToolId);
            var affected = CountWorkspacesUsing(proposal.ToolId);
            var panel = new StackPanel();

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            if (proposal.Kind == ProposalKinds.Run)
            {
                head.Children.Add(Text("Chạy: " + (proposal.ToolName ?? proposal.ToolId), bold: true));
            }
            else
            {
                head.Children.Add(Text((proposal.Kind == ProposalKinds.DeleteTool ? "Xoá khỏi thư viện: " : "Sửa tool dùng chung: ") + (tool?.Name ?? proposal.ToolName ?? proposal.ToolId), bold: true));
                head.Children.Add(Pill(affected == 0 ? "không workspace nào dùng" : $"ảnh hưởng {affected} workspace", affected == 0 ? "OkSoft" : "WarnSoft", affected == 0 ? "Ok" : "WarnInk"));
            }
            panel.Children.Add(head);
            panel.Children.Add(Text($"{proposal.AgentName} · {Ago(proposal.CreatedAt)}", size: 12, brush: "Muted"));

            if (!string.IsNullOrWhiteSpace(proposal.Reason))
                panel.Children.Add(Text("Lý do: " + proposal.Reason, size: 12.5, margin: new Thickness(0, 6, 0, 0), wrap: true));

            // Lệnh chạy: hiện đúng lệnh và tham số agent gửi; duyệt xong agent gọi lại thì chạy đúng một lần.
            if (proposal.Kind == ProposalKinds.Run)
            {
                var runPrint = proposal.Fields != null && proposal.Fields.TryGetValue(RunApprovals.PrintField, out var printed) ? printed : "";
                panel.Children.Add(Text(runPrint, size: 12, family: "Consolas", margin: new Thickness(0, 8, 0, 0), wrap: true));
                panel.Children.Add(Text("Chỉ chạy đúng lần này. Lần sau agent phải xin duyệt lại.", size: 12, brush: "Muted", margin: new Thickness(0, 4, 0, 0), wrap: true));
            }

            if (proposal.Kind == ProposalKinds.UpdateTool && proposal.Fields != null)
            {
                var diff = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var field in proposal.Fields)
                    diff.Children.Add(Text($"{field.Key}:  {Shown(CurrentValue(tool, field.Key))}  →  {Shown(field.Value)}", size: 12, family: "Consolas", wrap: true));
                panel.Children.Add(diff);
            }

            // Phạm vi duyệt: sửa tool dùng chung (mặc định) hoặc chỉ ghi tham số cho workspace đề xuất.
            var workspaceScope = new RadioButton { GroupName = "scope-" + proposal.Id, Margin = new Thickness(0, 2, 0, 0) };
            var sharedScope = new RadioButton { GroupName = "scope-" + proposal.Id, IsChecked = true };
            if (proposal.Kind == ProposalKinds.UpdateTool)
            {
                var scopeBox = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                sharedScope.Content = affected == 0
                    ? "Sửa tool dùng chung"
                    : $"Sửa tool dùng chung (ảnh hưởng {affected} workspace)";
                workspaceScope.Content = "Chỉ cho workspace này (" + WorkspaceName(proposal.WorkspaceId) + ")";
                scopeBox.Children.Add(sharedScope);
                scopeBox.Children.Add(workspaceScope);
                if (!ProposalApplier.CanScopeToWorkspace(proposal, out var why))
                {
                    workspaceScope.IsEnabled = false;
                    scopeBox.Children.Add(Text(why, size: 12, brush: "Muted", margin: new Thickness(22, 2, 0, 0), wrap: true));
                }
                else
                {
                    scopeBox.Children.Add(Text("Tool trong thư viện và workspace khác giữ nguyên; chỉ profile ở workspace này chạy với tham số mới.",
                        size: 12, brush: "Muted", margin: new Thickness(22, 2, 0, 0), wrap: true));
                }
                panel.Children.Add(scopeBox);
            }

            string Scope() => workspaceScope.IsEnabled && workspaceScope.IsChecked == true ? ProposalScopes.Workspace : ProposalScopes.Shared;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Btn("Duyệt", "PrimaryButton", (s, e) => Decide(proposal, approve: true, scope: Scope())));
            if (proposal.Kind == ProposalKinds.Run)
                actions.Children.Add(Btn("Luôn cho lệnh này", "SoftButton", (s, e) => AlwaysAllow(proposal)));
            actions.Children.Add(Btn("Từ chối", "GhostButton", (s, e) => Decide(proposal, approve: false)));
            panel.Children.Add(actions);
            return Card(panel, bottom: 10);
        }

        /// <summary>Duyệt và nhớ: lần sau agent chạy đúng lệnh này không phải hỏi lại. Bỏ ghi nhớ ở nút "Quên lệnh đã cho phép" của agent.</summary>
        private void AlwaysAllow(Proposal proposal)
        {
            try
            {
                RunApprovals.Decide(ProposalStore.DefaultPath, AgentStore.DefaultPath, ActivityLog.DefaultPath, proposal, approve: true, always: true);
                Dialogs.Success("Đã nhớ: agent chạy lệnh này luôn, không hỏi lại.", seconds: 6);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Dialogs.Error(Host, "Không áp dụng được: " + ex.Message);
            }
            Refresh();
        }

        private void Decide(Proposal proposal, bool approve, string scope = null)
        {
            try
            {
                var result = !approve ? "Đã từ chối."
                    : proposal.Kind == ProposalKinds.Run ? "Đã duyệt. Agent gọi lại đúng lệnh này để chạy."
                    : ProposalScopes.Normalize(scope) == ProposalScopes.Workspace ? ApplyToWorkspace(proposal)
                    : Apply(proposal);
                ProposalStore.Update(ProposalStore.DefaultPath, store => { store.Decide(proposal.Id, approve, scope); return true; });
                ActivityLog.Append(ActivityLog.DefaultPath, new ActivityEntry
                {
                    AgentId = proposal.AgentId, AgentName = proposal.AgentName, WorkspaceId = proposal.WorkspaceId,
                    Action = approve ? "approve" : "reject", Target = proposal.ToolName ?? proposal.ToolId,
                    Result = !approve ? "rejected" : ProposalScopes.Normalize(scope) == ProposalScopes.Workspace ? "applied-workspace" : "applied",
                });
                if (approve)
                    Dialogs.Success(result, seconds: 8);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Dialogs.Error(Host, "Không áp dụng được đề xuất: " + ex.Message);
            }
            Refresh();
        }

        /// <summary>Ghi thay đổi vào thư viện, và với đề xuất xoá thì gỡ tool khỏi mọi workspace.</summary>
        private static string Apply(Proposal proposal)
        {
            string message;
            using (ToolStore.Lock())
            {
                var store = ToolStore.Load(ToolStore.DefaultPath);
                store.Actor = "gui";
                message = ProposalApplier.Apply(store, proposal);
                store.Save();
            }
            if (proposal.Kind == ProposalKinds.DeleteTool)
                WorkspaceStore.Update(WorkspaceStore.DefaultPath, workspaces => workspaces.UnbindToolEverywhere(proposal.ToolId) > 0);
            return message;
        }

        /// <summary>Ghi tham số vào tool đã gắn trong workspace đề xuất; thư viện không đổi.</summary>
        private static string ApplyToWorkspace(Proposal proposal)
        {
            string message = null;
            WorkspaceStore.Update(WorkspaceStore.DefaultPath, workspaces =>
            {
                message = ProposalApplier.ApplyToWorkspace(workspaces, proposal);
                return true;
            });
            return message;
        }

        private static string CurrentValue(ToolEntry tool, string field)
        {
            if (tool == null)
                return null;
            switch (field)
            {
                case "name": return tool.Name;
                case "path": return tool.Path;
                case "args": return tool.Args;
                case "command": return tool.Command;
                case "description": return tool.Description;
                case "notes": return tool.Notes;
                case "tags": return tool.Tags == null ? null : string.Join(",", tool.Tags);
                default: return null;
            }
        }

        private static string Shown(string value) => string.IsNullOrEmpty(value) ? "(trống)" : value;

        private static int CountWorkspacesUsing(string toolId)
        {
            try
            {
                return WorkspaceStore.Load(WorkspaceStore.DefaultPath).Workspaces.Count(w => w.FindLibrary(toolId) != null);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return 0; }
        }

        // ---------- Tab Nhật ký ----------

        private static readonly Dictionary<string, string> ActionNames = new Dictionary<string, string>
        {
            ["register_tool"] = "Đăng ký tool", ["update_tool"] = "Đề xuất sửa tool", ["unregister_tool"] = "Gỡ tool",
            ["add_tool"] = "Thêm tool dự án", ["remove_tool"] = "Gỡ tool dự án", ["bind_library_tool"] = "Gắn tool thư viện", ["attach"] = "Gắn tool thư viện",
            ["create_profile"] = "Tạo profile", ["update_profile"] = "Sửa profile", ["delete_profile"] = "Xoá profile",
            ["create_custom"] = "Tạo lệnh riêng", ["update_custom"] = "Sửa lệnh riêng", ["delete_custom"] = "Xoá lệnh riêng",
            ["create_stack"] = "Tạo workflow", ["update_stack"] = "Sửa workflow", ["delete_stack"] = "Xoá workflow",
            ["set_worktree"] = "Cấu hình worktree", ["prepare_worktree"] = "Chuẩn bị worktree",
            ["run_tool"] = "Chạy tool", ["run_profile"] = "Chạy profile", ["run_custom"] = "Chạy lệnh riêng", ["run_stack"] = "Chạy workflow", ["run_action"] = "Chạy lệnh", ["run"] = "Chạy",
            ["save"] = "Lưu", ["delete"] = "Xoá", ["propose"] = "Đề xuất", ["undo"] = "Hoàn tác", ["approve"] = "Bạn duyệt đề xuất", ["reject"] = "Bạn từ chối đề xuất",
        };

        private void ShowActivity()
        {
            _activity = ActivityLog.Read(ActivityLog.DefaultPath, 100);
            if (_activity.Count == 0)
            {
                Body.Children.Add(Hint("Chưa có việc nào. Mỗi lần agent thêm, sửa, xoá hoặc chạy gì, và mỗi lần bạn duyệt đề xuất, sẽ ghi ở đây."));
                return;
            }

            // Đọc workspace và kho tool một lần để biết dòng nào còn hoàn tác được.
            _activityWorkspaces = null;
            _activityTools = null;
            try
            {
                _activityWorkspaces = WorkspaceStore.Load(WorkspaceStore.DefaultPath);
                _activityTools = ToolStore.Load(ToolStore.DefaultPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { }

            // Thanh lọc: chọn agent và tìm trong nội dung dòng (mockup: "Mọi agent" + "Tìm trong nhật ký").
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            _activityAgent = new ComboBox { MinWidth = 180, ToolTip = "Lọc theo agent" };
            _activityAgent.Items.Add("Mọi agent");
            foreach (var name in _activity.Select(e => e.AgentName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                _activityAgent.Items.Add(name);
            _activityAgent.SelectedIndex = 0;
            DockPanel.SetDock(_activityAgent, Dock.Left);
            bar.Children.Add(_activityAgent);

            _activitySearch = new TextBox { Margin = new Thickness(8, 0, 0, 0), ToolTip = "Tìm trong nhật ký" };
            bar.Children.Add(_activitySearch);
            Body.Children.Add(bar);

            _activityRows = new StackPanel();
            Body.Children.Add(_activityRows);
            Body.Children.Add(Hint("Mỗi thay đổi đều được sao lưu trước khi ghi (xem mục Dữ liệu và sao lưu trong README)."));

            _activityAgent.SelectionChanged += (s, e) => RenderActivity();
            _activitySearch.TextChanged += (s, e) => RenderActivity();
            RenderActivity();
        }

        private List<ActivityEntry> _activity = new List<ActivityEntry>();
        private WorkspaceStore _activityWorkspaces;
        private ToolStore _activityTools;
        private StackPanel _activityRows;
        private ComboBox _activityAgent;
        private TextBox _activitySearch;

        /// <summary>Vẽ lại các dòng nhật ký theo agent và ô tìm đang chọn.</summary>
        private void RenderActivity()
        {
            if (_activityRows == null)
                return;
            _activityRows.Children.Clear();
            var agent = _activityAgent.SelectedIndex > 0 ? _activityAgent.SelectedItem as string : null;
            var needle = (_activitySearch.Text ?? "").Trim();

            var shown = 0;
            foreach (var entry in _activity)
            {
                if (agent != null && !string.Equals(entry.AgentName, agent, StringComparison.OrdinalIgnoreCase))
                    continue;
                var label = ActionNames.TryGetValue(entry.Action ?? "", out var name) ? name : entry.Action;
                if (needle.Length > 0 && $"{label} {entry.Target} {entry.AgentName} {entry.Result}".IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                shown++;

                var row = new DockPanel();
                var result = ResultPill(entry.Result);
                DockPanel.SetDock(result, Dock.Right);
                row.Children.Add(result);
                var undoButton = UndoButton(entry, _activity, _activityWorkspaces, _activityTools);
                if (undoButton != null)
                {
                    undoButton.Margin = new Thickness(0, 0, 8, 0);
                    DockPanel.SetDock(undoButton, Dock.Right);
                    row.Children.Add(undoButton);
                }
                var text = new StackPanel();
                text.Children.Add(Text($"{label}  {entry.Target}".TrimEnd(), bold: true));
                text.Children.Add(Text($"{entry.AgentName} · {Ago(entry.Time)}", size: 12, brush: "Muted"));
                row.Children.Add(text);
                _activityRows.Children.Add(Card(row, bottom: 6));
            }
            if (shown == 0)
                _activityRows.Children.Add(Hint("Không có dòng nào khớp bộ lọc."));
        }

        /// <summary>Nút "Hoàn tác" cho dòng có thể đã làm thay đổi dữ liệu; tắt kèm lý do khi không hoàn tác được. Null: dòng không liên quan.</summary>
        private Button UndoButton(ActivityEntry entry, List<ActivityEntry> log, WorkspaceStore workspaces, ToolStore tools)
        {
            var isChange = entry.Undo != null
                || (entry.Result == "applied" && ActivityUndo.UndoableActions.Contains(entry.Action ?? ""));
            if (!isChange)
                return null;

            string reason = null;
            var enabled = false;
            if (ActivityUndo.IsUndone(entry, log))
                reason = ActivityUndo.AlreadyUndone;
            else if (entry.Undo == null)
                reason = "Dòng cũ, không có dữ liệu hoàn tác.";
            else if (workspaces == null || tools == null)
                reason = "Không đọc được dữ liệu hiện tại để kiểm.";
            else
            {
                var workspace = string.IsNullOrEmpty(entry.WorkspaceId) ? null : workspaces.Find(entry.WorkspaceId);
                enabled = ActivityUndo.CanUndo(entry, workspace, tools, out reason);
            }

            var button = Btn("Hoàn tác", "GhostButton", (s, e) => Undo(entry));
            button.IsEnabled = enabled;
            button.ToolTip = enabled ? "Khôi phục đúng đối tượng này về bản trước khi agent đổi." : reason;
            ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }

        private void Undo(ActivityEntry entry)
        {
            if (!Dialogs.Confirm(Host, $"Hoàn tác \"{(ActionNames.TryGetValue(entry.Action ?? "", out var name) ? name : entry.Action)} {entry.Target}\"?\n\nChỉ đối tượng này được khôi phục về bản trước; các thay đổi khác của bạn giữ nguyên."))
                return;
            try
            {
                var message = ActivityUndo.Undo(entry, WorkspaceStore.DefaultPath, ToolStore.DefaultPath, ActivityLog.DefaultPath);
                Dialogs.Success(message, seconds: 8);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Dialogs.Error(Host, "Không hoàn tác được: " + ex.Message);
            }
            Refresh();
        }

        private UIElement ResultPill(string result)
        {
            switch (result)
            {
                case "applied": return Pill("đã áp dụng", "OkSoft", "Ok");
                case "pending": return Pill("chờ duyệt", "WarnSoft", "WarnInk");
                case "rejected": return Pill("đã từ chối", "TagBg", "TagInk");
                default: return Pill("lỗi", "DangerSoft", "Danger");
            }
        }

        // ---------- Dựng giao diện ----------

        private static bool IsConnected(AgentRecord agent) =>
            TryTime(agent.LastSeenAt, out var seen) && DateTime.UtcNow - seen < ConnectedWindow;

        private string ScopeText(AgentRecord agent) => agent.IsGlobal ? "Toàn máy" : "Workspace: " + WorkspaceName(agent.WorkspaceId);

        private string WorkspaceName(string id) =>
            string.IsNullOrEmpty(id) ? "toàn máy" : _workspaceNames.TryGetValue(id, out var name) ? name : "workspace đã gỡ";

        private static string RunModeText(string mode)
        {
            switch (RunModes.Normalize(mode))
            {
                case RunModes.Auto: return "tự chạy cái đã có";
                case RunModes.Ask: return "hỏi trước mỗi lần chạy";
                default: return "không cho chạy";
            }
        }

        private static bool TryTime(string iso, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return false;
            utc = parsed.ToUniversalTime();
            return true;
        }

        /// <summary>"vừa xong", "5 phút trước", "3 giờ trước", hoặc ngày giờ khi quá một ngày.</summary>
        public static string Ago(string iso)
        {
            if (!TryTime(iso, out var utc))
                return "không rõ";
            var span = DateTime.UtcNow - utc;
            if (span < TimeSpan.FromMinutes(1)) return "vừa xong";
            if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} phút trước";
            if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} giờ trước";
            return utc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        }

        private Brush Brush(string key) => (Brush)FindResource(key);

        private TextBlock Text(string text, double size = 13, bool bold = false, string brush = "Ink", Thickness? margin = null, bool wrap = false, string family = null)
        {
            var block = new TextBlock
            {
                Text = text, FontSize = size, Foreground = Brush(brush), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, Margin = margin ?? new Thickness(0),
            };
            if (bold)
                block.FontWeight = FontWeights.SemiBold;
            if (family != null)
                block.FontFamily = new FontFamily(family);
            return block;
        }

        private TextBlock Hint(string text)
        {
            var block = Text(text, size: 12, brush: "Muted", wrap: true);
            block.Margin = new Thickness(0, 8, 0, 0);
            return block;
        }

        private TextBlock Section(string text)
        {
            var block = Text(text, size: 11, bold: true, brush: "Faint");
            block.Margin = new Thickness(0, 12, 0, 4);
            return block;
        }

        private TextBlock Line(string text) => Text(text, size: 12.5, wrap: true);

        private Border Pill(string text, string background, string foreground) => new Border
        {
            Background = Brush(background), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, size: 11, bold: true, brush: foreground),
        };

        private Border Card(UIElement child, double bottom) => new Border
        {
            Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, bottom), Child = child,
        };

        private Button Btn(string text, string style, RoutedEventHandler click)
        {
            var button = new Button { Content = text, Style = (Style)FindResource(style), Margin = new Thickness(0, 0, 6, 0) };
            button.Click += click;
            return button;
        }
    }
}
