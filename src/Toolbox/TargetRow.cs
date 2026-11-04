using Toolbox.Core;

namespace Toolbox
{
    /// <summary>Một dòng trong cửa sổ kết nối agent: một target (toàn máy hoặc workspace) và trạng thái của server.</summary>
    public sealed class TargetRow
    {
        /// <param name="workspaceRow">true: dòng một workspace Claude Code. Kết nối phải giới hạn đúng vào workspace đã đăng ký trong Toolbox.</param>
        /// <param name="toolboxWorkspaceId">Id workspace trong Toolbox của thư mục này; null nếu chưa đăng ký.</param>
        public TargetRow(ClaudeTarget target, string server, string title, string detail, bool workspaceRow, string toolboxWorkspaceId, params string[] exes)
        {
            Target = target;
            Server = server;
            Title = title;
            Detail = detail;

            var link = target.LinkTo(server, exes);
            var entry = target.Entry(server);
            if (link == AgentLink.Stale)
                StaleReason = "Trỏ tới exe khác";
            else if (link == AgentLink.Connected && entry != null)
            {
                // Toàn máy không được có giới hạn; workspace phải giới hạn đúng workspace này.
                var scopeOk = workspaceRow
                    ? toolboxWorkspaceId != null && entry.ScopedTo(toolboxWorkspaceId)
                    : !entry.HasWorkspaceScope;
                if (!scopeOk)
                {
                    link = AgentLink.Stale;
                    StaleReason = workspaceRow && toolboxWorkspaceId == null
                        ? "Chưa đăng ký workspace"
                        : "Kết nối cũ, cần kết nối lại";
                }
            }
            Link = link;
        }

        public ClaudeTarget Target { get; }
        public string Server { get; }
        public string Title { get; }
        public string Detail { get; }
        public AgentLink Link { get; }

        /// <summary>Lý do trạng thái Stale, hiện trên chip.</summary>
        public string StaleReason { get; }

        /// <summary>Nhãn chip trạng thái.</summary>
        public string ChipText
        {
            get
            {
                if (Link == AgentLink.Connected)
                    return "Đã kết nối";
                if (Link == AgentLink.Stale)
                    return StaleReason ?? "Trỏ tới exe khác";
                return "Chưa kết nối";
            }
        }

        public string ToolTip => Target.Entry(Server)?.Command == null ? null : "Khai báo hiện tại: " + Target.Entry(Server).Command;
    }
}
