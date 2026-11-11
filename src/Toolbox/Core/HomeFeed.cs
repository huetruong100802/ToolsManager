using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    public enum HomeTarget { Agents, Task, Workspace, Library, Run, OpenPort, AddWorkspace, PickApp, ConnectAgent, OpenTool }

    /// <summary>Một dòng trên Trang chủ: việc cần xử lý, tác vụ đang chạy, hoặc mục để tiếp tục.</summary>
    public sealed class HomeItem
    {
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public HomeTarget Target { get; set; }
        public string ActionText { get; set; }
        public string WorkspaceId { get; set; }
        public string ToolId { get; set; }
        public TaskInfo Task { get; set; }
        public int? Port { get; set; }

        /// <summary>Mục chạy được (profile, workflow, lệnh riêng) cho nút Chạy và nút Mở.</summary>
        public SearchEntry Entry { get; set; }

        /// <summary>Nút phụ (vd. "Xem log" cạnh "Mở :4200"); null nếu không có.</summary>
        public string SecondaryText { get; set; }
    }

    /// <summary>Thẻ một workspace trên Trang chủ: tên, và vài lệnh chạy được ngay (ghim, dùng gần đây, rồi vài lệnh đầu).</summary>
    public sealed class HomeCard
    {
        public string WorkspaceId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public List<HomeItem> Commands { get; } = new List<HomeItem>();
    }

    /// <summary>Một bước làm quen cho lần đầu mở app.</summary>
    public sealed class OnboardingStep
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public bool Done { get; set; }
        public HomeTarget Target { get; set; }
        public string ActionText { get; set; }
    }

    /// <summary>
    /// Nội dung Trang chủ, tính từ dữ liệu hiện có (không tự đọc file nên test được): "Cần bạn xử lý", "Đang chạy", "Tiếp tục",
    /// và 3 bước làm quen khi chưa có gì. Mỗi dòng chỉ tới một nơi cụ thể để bấm là tới đúng chỗ cần làm.
    /// </summary>
    public sealed class HomeFeed
    {
        public const int MaxFailedShown = 3;
        public const int MaxContinue = 6;

        /// <summary>Số lệnh tối đa trên thẻ một workspace.</summary>
        public const int MaxCardCommands = 4;

        /// <summary>Tác vụ lỗi trong vòng chừng này thì còn được nhắc.</summary>
        public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

        public List<HomeItem> Attention { get; } = new List<HomeItem>();
        public List<HomeItem> Running { get; } = new List<HomeItem>();
        public List<HomeItem> Continue { get; } = new List<HomeItem>();
        public List<OnboardingStep> Onboarding { get; } = new List<OnboardingStep>();

        /// <summary>Thẻ từng workspace (không lưu trữ, thư mục còn) kèm lệnh chạy được.</summary>
        public List<HomeCard> Cards { get; } = new List<HomeCard>();

        /// <summary>Tool đã ghim trong Thư viện, để mở nhanh.</summary>
        public List<HomeItem> PinnedTools { get; } = new List<HomeItem>();

        public bool OnboardingDone => Onboarding.Count > 0 && Onboarding.All(s => s.Done);

        public static HomeFeed Build(
            IReadOnlyList<Workspace> workspaces,
            IReadOnlyList<ToolEntry> tools,
            IReadOnlyList<TaskInfo> tasks,
            int pendingProposals,
            bool hasAgent,
            IEnumerable<SearchEntry> entries,
            UsageStore usage,
            DateTime now,
            Func<string, bool> pathExists = null,
            int pendingRunRequests = 0,
            IReadOnlyDictionary<string, int> readinessIssues = null,
            Func<Workspace, IReadOnlyList<QuickCommand>> quickCommands = null)
        {
            var exists = pathExists ?? (p => File.Exists(p) || Directory.Exists(p));
            var feed = new HomeFeed();
            workspaces = workspaces ?? new List<Workspace>();
            tools = tools ?? new List<ToolEntry>();
            tasks = tasks ?? new List<TaskInfo>();

            // ---- Cần bạn xử lý ----
            if (pendingRunRequests > 0)
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "", Title = pendingRunRequests == 1 ? "Agent xin chạy một lệnh" : $"Agent xin chạy {pendingRunRequests} lệnh",
                    Detail = "Chỉ chạy khi bạn cho phép: duyệt một lần, luôn cho lệnh đó, hoặc từ chối.", Target = HomeTarget.Agents, ActionText = "Duyệt",
                });
            var otherProposals = Math.Max(0, pendingProposals - pendingRunRequests);
            if (otherProposals > 0)
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "", Title = $"{otherProposals} đề xuất của agent đang chờ bạn duyệt",
                    Detail = "Agent muốn thêm hoặc sửa tool; bạn quyết định.", Target = HomeTarget.Agents, ActionText = "Xem và duyệt",
                });

            foreach (var task in FailedTasks(tasks, now).Take(MaxFailedShown))
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "", Title = $"\"{task.Spec.Label}\" bị lỗi" + (task.ExitCode.HasValue ? $" (mã {task.ExitCode})" : ""),
                    Detail = WorkspaceName(workspaces, task.Spec.WorkspaceId) + " · " + Text.Duration(now - (task.EndedAt ?? now)) + " trước",
                    Target = HomeTarget.Task, Task = task, ActionText = "Xem log",
                });

            foreach (var workspace in workspaces.Where(w => !w.Archived && readinessIssues != null && w.PathExists
                                                       && readinessIssues.TryGetValue(w.Id, out var n) && n > 0))
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "E7BA", Title = $"\"{workspace.Name}\" chưa sẵn sàng: {readinessIssues[workspace.Id]} việc",
                    Detail = "Còn thiếu tool, lệnh hoặc có profile hỏng trên máy này. Mở workspace để xem và sửa.",
                    Target = HomeTarget.Workspace, WorkspaceId = workspace.Id, ActionText = "Xem việc cần làm",
                });

            foreach (var workspace in workspaces.Where(w => !w.Archived && !w.PathExists))
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "", Title = $"Không tìm thấy thư mục của \"{workspace.Name}\"",
                    Detail = workspace.Path, Target = HomeTarget.Workspace, WorkspaceId = workspace.Id, ActionText = "Trỏ lại thư mục",
                });

            foreach (var tool in tools.Where(t => !t.IsCommand && !string.IsNullOrWhiteSpace(t.Path) && !exists(Environment.ExpandEnvironmentVariables(t.Path))))
                feed.Attention.Add(new HomeItem
                {
                    Glyph = "", Title = $"Tool \"{tool.Name}\" mất đường dẫn", Detail = tool.Path,
                    Target = HomeTarget.Library, ToolId = tool.Id, ActionText = "Mở trong Thư viện",
                });

            // ---- Đang chạy ----
            foreach (var task in tasks.Where(t => t.IsRunning).OrderBy(t => t.StartedAt))
                feed.Running.Add(new HomeItem
                {
                    Glyph = "", Title = task.Spec.Label,
                    Detail = WorkspaceName(workspaces, task.Spec.WorkspaceId) + " · " + Text.Duration(now - task.StartedAt),
                    Task = task, Port = task.Port, WorkspaceId = task.Spec.WorkspaceId,
                    Target = task.Port.HasValue ? HomeTarget.OpenPort : HomeTarget.Task,
                    ActionText = task.Port.HasValue ? "Mở :" + task.Port : "Xem log",
                    SecondaryText = task.Port.HasValue ? "Xem log" : null,
                });

            // ---- Tiếp tục: workspace ghim, rồi mục chạy được dùng gần đây ----
            foreach (var workspace in workspaces.Where(w => w.Pinned && !w.Archived).OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
                feed.Continue.Add(new HomeItem
                {
                    Glyph = "", Title = workspace.Name, Detail = "Workspace đã ghim", Target = HomeTarget.Workspace,
                    WorkspaceId = workspace.Id, ActionText = "Mở",
                });
            var byKey = (entries ?? Enumerable.Empty<SearchEntry>()).Where(e => e.Runnable && !string.IsNullOrEmpty(e.Key))
                .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var key in usage?.Recent(MaxContinue * 3) ?? (IReadOnlyList<string>)new string[0])
            {
                if (feed.Continue.Count >= MaxContinue + workspaces.Count(w => w.Pinned && !w.Archived))
                    break;
                if (!byKey.TryGetValue(key, out var entry))
                    continue;
                feed.Continue.Add(new HomeItem
                {
                    Glyph = entry.Kind == SearchKind.Workflow ? "" : entry.Kind == SearchKind.Custom ? "" : "",
                    Title = entry.Title, Detail = entry.Subtitle, Target = HomeTarget.Run, Entry = entry, WorkspaceId = entry.WorkspaceId,
                    ActionText = "Chạy", SecondaryText = "Mở",
                });
            }

            // ---- Thẻ workspace và tool ghim ----
            var quickOf = quickCommands ?? QuickCommands.For;
            foreach (var workspace in workspaces.Where(w => !w.Archived && w.PathExists).OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var card = new HomeCard { WorkspaceId = workspace.Id, Name = workspace.Name, Path = workspace.Path };
                card.Commands.AddRange(CardCommands(workspace, quickOf(workspace), usage));
                feed.Cards.Add(card);
            }
            foreach (var tool in tools.Where(t => t.Pinned).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
                feed.PinnedTools.Add(new HomeItem
                {
                    Glyph = "\uE718", Title = tool.Name, Detail = string.IsNullOrWhiteSpace(tool.Description) ? "Tool đã ghim" : tool.Description,
                    Target = HomeTarget.OpenTool, ToolId = tool.Id, ActionText = "Mở", SecondaryText = "Trong Thư viện",
                });

            // ---- Làm quen ----
            feed.Onboarding.Add(new OnboardingStep
            {
                Title = "Thêm workspace", Detail = "Chọn thư mục repo để chạy lệnh ngay tại đó.",
                Done = workspaces.Count > 0, Target = HomeTarget.AddWorkspace, ActionText = "Thêm workspace",
            });
            feed.Onboarding.Add(new OnboardingStep
            {
                Title = "Có tool trong Thư viện", Detail = "Chọn app đã cài hoặc thêm file, thư mục, shortcut.",
                Done = tools.Count > 0, Target = HomeTarget.PickApp, ActionText = "Chọn app đã cài",
            });
            feed.Onboarding.Add(new OnboardingStep
            {
                Title = "Kết nối agent", Detail = "Để Claude Code hay Cursor dùng tool của bạn qua MCP.",
                Done = hasAgent, Target = HomeTarget.ConnectAgent, ActionText = "Kết nối agent",
            });
            return feed;
        }

        /// <summary>
        /// Lệnh hiện trên thẻ workspace: lệnh đã ghim trước, rồi lệnh dùng gần đây, rồi vài lệnh đầu của tool (ưu tiên script/lệnh con
        /// hơn lệnh gốc). Chỉ lấy lệnh chạy được; tối đa <see cref="MaxCardCommands"/>.
        /// </summary>
        internal static IEnumerable<HomeItem> CardCommands(Workspace workspace, IReadOnlyList<QuickCommand> quicks, UsageStore usage)
        {
            var runnable = (quicks ?? new List<QuickCommand>()).Where(q => q.IsRunnable).ToList();
            var chosen = new List<QuickCommand>();
            void Take(IEnumerable<QuickCommand> source)
            {
                foreach (var q in source)
                    if (chosen.Count < MaxCardCommands && !chosen.Contains(q))
                        chosen.Add(q);
            }

            Take(runnable.Where(q => q.Source == QuickSource.Profile));
            var recent = usage?.Recent(MaxCardCommands * 6) ?? (IReadOnlyList<string>)new string[0];
            foreach (var key in recent)
            {
                var hit = runnable.FirstOrDefault(q => string.Equals(KeyOf(q), key, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    Take(new[] { hit });
            }
            Take(runnable.Where(q => q.Source == QuickSource.Script));
            Take(runnable.Where(q => q.Source == QuickSource.Custom));
            Take(runnable.Where(q => q.Source == QuickSource.Action && !string.IsNullOrEmpty(q.ActionId)));
            Take(runnable.Where(q => q.Source == QuickSource.Action));

            return chosen.Select(q => new HomeItem
            {
                Glyph = "\uE768", Title = q.Title, Detail = q.CommandLine, Target = HomeTarget.Run, WorkspaceId = workspace.Id,
                Entry = EntryOf(q), ActionText = "Chạy", SecondaryText = "Mở",
            });
        }

        /// <summary>Khoá mức dùng của một lệnh, khớp với khoá mà bảng lệnh và nút Chạy ghi khi chạy.</summary>
        private static string KeyOf(QuickCommand q)
        {
            switch (q.Source)
            {
                case QuickSource.Profile: return UsageStore.Key("profile", q.RefId, q.WorkspaceId);
                case QuickSource.Custom: return UsageStore.Key("custom", q.RefId, q.WorkspaceId);
                default: return UsageStore.QuickKey(q.Id);
            }
        }

        /// <summary>Mục tìm kiếm tương ứng để <c>OpenEntry</c> chạy hoặc mở đúng dòng.</summary>
        private static SearchEntry EntryOf(QuickCommand q)
        {
            var kind = q.Source == QuickSource.Profile ? SearchKind.Profile : q.Source == QuickSource.Custom ? SearchKind.Custom : SearchKind.Quick;
            return new SearchEntry
            {
                Kind = kind, Key = KeyOf(q), Title = q.Title, Subtitle = q.CommandLine, WorkspaceId = q.WorkspaceId,
                TargetId = kind == SearchKind.Quick ? q.Id : q.RefId, Runnable = true,
            };
        }

        /// <summary>
        /// Tác vụ lỗi gần đây mà chưa được chạy lại thành công: mỗi khoá chỉ lấy lần chạy mới nhất, bỏ nếu lần đó xong hoặc đang chạy.
        /// </summary>
        public static IEnumerable<TaskInfo> FailedTasks(IEnumerable<TaskInfo> tasks, DateTime now) =>
            tasks.GroupBy(t => t.Spec.Key ?? t.Id, StringComparer.OrdinalIgnoreCase)
                 .Select(g => g.OrderByDescending(t => t.StartedAt).First())
                 .Where(t => t.State == TaskState.Failed && t.EndedAt.HasValue && now - t.EndedAt.Value <= FailureWindow)
                 .OrderByDescending(t => t.EndedAt);

        private static string WorkspaceName(IEnumerable<Workspace> workspaces, string id) =>
            workspaces.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? "workspace đã gỡ";
    }
}
