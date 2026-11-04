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
            panel.Children.Add(Section("TRONG PHẠM VI CỦA AGENT"));
            panel.Children.Add(agent.IsGlobal
                ? Line("Agent toàn máy chỉ quản lý thư viện tool (thêm, sửa, xoá), không đụng profile hay bộ chạy của workspace nào.")
                : Line("Thêm, sửa, xoá tool riêng, profile, bộ chạy, lệnh riêng trong workspace này. Luôn bật."));
            panel.Children.Add(Section("THƯ VIỆN TOOL"));
            panel.Children.Add(agent.IsGlobal
                ? Line("Đầy đủ: xem, thêm, sửa, xoá tool dùng chung.")
                : Line("Xem thư viện, gắn tool có sẵn vào workspace, thêm tool mới. Sửa hoặc xoá tool dùng chung chỉ qua đề xuất để bạn duyệt."));

            panel.Children.Add(Section("CHẠY LỆNH"));
            var auto = new RadioButton { Content = "Tự chạy profile, bộ chạy và tool đã có", GroupName = "run-" + agent.Id, Margin = new Thickness(0, 2, 0, 2) };
            var none = new RadioButton { Content = "Không cho chạy", GroupName = "run-" + agent.Id, Margin = new Thickness(0, 2, 0, 2) };
            var current = RunModes.Normalize(agent.RunMode);
            auto.IsChecked = current == RunModes.Auto;
            none.IsChecked = current != RunModes.Auto;
            panel.Children.Add(auto);
            panel.Children.Add(none);

            panel.Children.Add(Section("CÔNG CỤ AGENT THẤY"));
            panel.Children.Add(Line(agent.IsGlobal
                ? "Bộ công cụ quản lý thư viện (5 công cụ)."
                : $"{ToolCountForAgent} công cụ, cố định: workspace có 5 hay 500 profile vẫn vậy. Agent tìm theo từ khoá và phân trang, không đọc cả danh sách."));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(Btn("Lưu quyền", "SoftButton", (s, e) =>
            {
                var mode = auto.IsChecked == true ? RunModes.Auto : RunModes.None;
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
            head.Children.Add(Text((proposal.Kind == ProposalKinds.DeleteTool ? "Xoá khỏi thư viện: " : "Sửa tool dùng chung: ") + (tool?.Name ?? proposal.ToolName ?? proposal.ToolId), bold: true));
            head.Children.Add(Pill(affected == 0 ? "không workspace nào dùng" : $"ảnh hưởng {affected} workspace", affected == 0 ? "OkSoft" : "WarnSoft", affected == 0 ? "Ok" : "WarnInk"));
            panel.Children.Add(head);
            panel.Children.Add(Text($"{proposal.AgentName} · {Ago(proposal.CreatedAt)}", size: 12, brush: "Muted"));

            if (!string.IsNullOrWhiteSpace(proposal.Reason))
                panel.Children.Add(Text("Lý do: " + proposal.Reason, size: 12.5, margin: new Thickness(0, 6, 0, 0), wrap: true));

            if (proposal.Kind == ProposalKinds.UpdateTool && proposal.Fields != null)
            {
                var diff = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var field in proposal.Fields)
                    diff.Children.Add(Text($"{field.Key}:  {Shown(CurrentValue(tool, field.Key))}  →  {Shown(field.Value)}", size: 12, family: "Consolas", wrap: true));
                panel.Children.Add(diff);
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Btn("Duyệt", "PrimaryButton", (s, e) => Decide(proposal, approve: true)));
            actions.Children.Add(Btn("Từ chối", "GhostButton", (s, e) => Decide(proposal, approve: false)));
            panel.Children.Add(actions);
            return Card(panel, bottom: 10);
        }

        private void Decide(Proposal proposal, bool approve)
        {
            try
            {
                var result = approve ? Apply(proposal) : "Đã từ chối.";
                ProposalStore.Update(ProposalStore.DefaultPath, store => { store.Decide(proposal.Id, approve); return true; });
                ActivityLog.Append(ActivityLog.DefaultPath, new ActivityEntry
                {
                    AgentId = proposal.AgentId, AgentName = proposal.AgentName, WorkspaceId = proposal.WorkspaceId,
                    Action = approve ? "approve" : "reject", Target = proposal.ToolName ?? proposal.ToolId,
                    Result = approve ? "applied" : "rejected",
                });
                if (approve)
                    MessageBox.Show(Host, result, "Toolbox", MessageBoxButton.OK, MessageBoxImage.Information);
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
            ["create_stack"] = "Tạo bộ chạy", ["update_stack"] = "Sửa bộ chạy", ["delete_stack"] = "Xoá bộ chạy",
            ["set_worktree"] = "Cấu hình worktree", ["prepare_worktree"] = "Chuẩn bị worktree",
            ["run_tool"] = "Chạy tool", ["run_profile"] = "Chạy profile", ["run_custom"] = "Chạy lệnh riêng", ["run_stack"] = "Chạy bộ chạy", ["run_action"] = "Chạy lệnh", ["run"] = "Chạy",
            ["save"] = "Lưu", ["delete"] = "Xoá", ["propose"] = "Đề xuất", ["approve"] = "Bạn duyệt đề xuất", ["reject"] = "Bạn từ chối đề xuất",
        };

        private void ShowActivity()
        {
            var entries = ActivityLog.Read(ActivityLog.DefaultPath, 100);
            if (entries.Count == 0)
            {
                Body.Children.Add(Hint("Chưa có việc nào. Mỗi lần agent thêm, sửa, xoá hoặc chạy gì, và mỗi lần bạn duyệt đề xuất, sẽ ghi ở đây."));
                return;
            }

            foreach (var entry in entries)
            {
                var row = new DockPanel();
                var result = ResultPill(entry.Result);
                DockPanel.SetDock(result, Dock.Right);
                row.Children.Add(result);
                var text = new StackPanel();
                text.Children.Add(Text($"{(ActionNames.TryGetValue(entry.Action ?? "", out var name) ? name : entry.Action)}  {entry.Target}".TrimEnd(), bold: true));
                text.Children.Add(Text($"{entry.AgentName} · {Ago(entry.Time)}", size: 12, brush: "Muted"));
                row.Children.Add(text);
                Body.Children.Add(Card(row, bottom: 6));
            }
            Body.Children.Add(Hint("Mỗi thay đổi đều được sao lưu trước khi ghi (xem mục Dữ liệu và sao lưu trong README)."));
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

        private static string RunModeText(string mode) => RunModes.Normalize(mode) == RunModes.Auto ? "tự chạy cái đã có" : "không cho chạy";

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
