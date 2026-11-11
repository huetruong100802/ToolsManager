using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Toolbox.Core.Agents
{
    /// <summary>
    /// Agent hiện diện trong giao diện: ai đang kết nối (theo workspace) và việc nào agent vừa làm mà người dùng chưa thấy.
    /// Chỉ là logic thuần trên dữ liệu đã đọc, để rail, trang workspace và test dùng chung một cách tính.
    /// </summary>
    public static class AgentPresence
    {
        /// <summary>Agent gọi công cụ trong chừng này thì coi là đang kết nối.</summary>
        public static readonly TimeSpan ConnectedWindow = TimeSpan.FromMinutes(2);

        /// <summary>Việc ghi vào nhật ký mà không phải thay đổi dữ liệu của agent (bạn duyệt, hoàn tác, chạy) không cần báo "agent vừa…".</summary>
        private static readonly HashSet<string> Silent = new HashSet<string>(StringComparer.Ordinal)
        {
            "approve", "reject", "undo", "propose", "run", "run_tool", "run_profile", "run_custom", "run_stack", "run_action",
        };

        public static readonly IReadOnlyDictionary<string, string> ActionVerbs = new Dictionary<string, string>
        {
            ["register_tool"] = "đăng ký tool", ["update_tool"] = "đề xuất sửa tool", ["unregister_tool"] = "gỡ tool",
            ["add_tool"] = "thêm tool dự án", ["remove_tool"] = "gỡ tool dự án", ["bind_library_tool"] = "gắn tool thư viện", ["attach"] = "gắn tool thư viện",
            ["create_profile"] = "tạo profile", ["update_profile"] = "sửa profile", ["delete_profile"] = "xoá profile",
            ["create_custom"] = "tạo lệnh riêng", ["update_custom"] = "sửa lệnh riêng", ["delete_custom"] = "xoá lệnh riêng",
            ["create_stack"] = "tạo workflow", ["update_stack"] = "sửa workflow", ["delete_stack"] = "xoá workflow",
            ["set_worktree"] = "cấu hình worktree", ["prepare_worktree"] = "chuẩn bị worktree",
            ["save"] = "lưu", ["delete"] = "xoá",
        };

        public static bool TryTime(string value, out DateTime utc) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

        public static bool IsConnected(AgentRecord agent, DateTime now) =>
            agent != null && TryTime(agent.LastSeenAt, out var seen) && now - seen < ConnectedWindow;

        /// <summary>Agent đang kết nối và thấy được workspace này (agent toàn máy thấy mọi workspace).</summary>
        public static List<AgentRecord> ConnectedFor(IEnumerable<AgentRecord> agents, string workspaceId, DateTime now) =>
            (agents ?? Enumerable.Empty<AgentRecord>())
                .Where(a => IsConnected(a, now) && (a.IsGlobal || string.Equals(a.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        /// <summary>Id các workspace đang có agent kết nối. Có agent toàn máy kết nối thì trả <paramref name="allWorkspaceIds"/>.</summary>
        public static HashSet<string> ConnectedWorkspaces(IEnumerable<AgentRecord> agents, IEnumerable<string> allWorkspaceIds, DateTime now)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in (agents ?? Enumerable.Empty<AgentRecord>()).Where(a => IsConnected(a, now)))
            {
                if (agent.IsGlobal)
                    foreach (var id in allWorkspaceIds ?? Enumerable.Empty<string>())
                        result.Add(id);
                else
                    result.Add(agent.WorkspaceId);
            }
            return result;
        }

        /// <summary>Mốc đọc nhật ký: thời điểm của dòng mới nhất (để lúc mở app không báo lại việc cũ). Nhật ký rỗng: <see cref="DateTime.MinValue"/>.</summary>
        public static DateTime Watermark(IEnumerable<ActivityEntry> log) =>
            (log ?? Enumerable.Empty<ActivityEntry>()).Select(e => TryTime(e.Time, out var t) ? t : DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();

        /// <summary>
        /// Việc agent vừa làm (mới hơn <paramref name="after"/>), cũ trước, chỉ các thay đổi dữ liệu đã áp dụng
        /// (không tính việc của chính bạn, hoàn tác hay chạy). <paramref name="workspaceId"/> khác null thì chỉ lấy workspace đó.
        /// </summary>
        public static List<ActivityEntry> Fresh(IEnumerable<ActivityEntry> log, DateTime after, string workspaceId = null) =>
            (log ?? Enumerable.Empty<ActivityEntry>())
                .Where(e => e != null && !Silent.Contains(e.Action ?? "") && ActionVerbs.ContainsKey(e.Action ?? "")
                            && (e.Result == null || e.Result == "applied" || e.Result == "applied-workspace")
                            && TryTime(e.Time, out var t) && t > after
                            && (workspaceId == null || string.Equals(e.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => { TryTime(e.Time, out var t); return t; })
                .ToList();

        /// <summary>"Claude vừa tạo profile "Dev API"".</summary>
        public static string Describe(ActivityEntry entry)
        {
            var who = string.IsNullOrWhiteSpace(entry.AgentName) ? "Agent" : entry.AgentName;
            var verb = ActionVerbs.TryGetValue(entry.Action ?? "", out var v) ? v : entry.Action;
            var target = string.IsNullOrWhiteSpace(entry.Target) ? "" : $" \"{entry.Target}\"";
            return $"{who} vừa {verb}{target}";
        }

        /// <summary>Id đối tượng (profile, workflow, lệnh riêng) mà thay đổi này chạm tới, để đánh dấu "mới" trên đúng mục.</summary>
        public static IEnumerable<string> TouchedIds(ActivityEntry entry)
        {
            if (entry?.Undo == null)
                yield break;
            if (!string.IsNullOrEmpty(entry.Undo.Id))
                yield return entry.Undo.Id;
            foreach (var also in entry.Undo.Also ?? new List<ActivityUndoData>())
                if (!string.IsNullOrEmpty(also.Id))
                    yield return also.Id;
        }
    }
}
