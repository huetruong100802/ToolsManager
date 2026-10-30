using Toolbox.Core;

namespace Toolbox
{
    /// <summary>Một dòng trong cửa sổ kết nối agent: một target (toàn máy hoặc workspace) và trạng thái của một server.</summary>
    public sealed class TargetRow
    {
        public TargetRow(ClaudeTarget target, string server, string title, string detail, params string[] exes)
        {
            Target = target;
            Server = server;
            Title = title;
            Detail = detail;
            Link = target.LinkTo(server, exes);
        }

        public ClaudeTarget Target { get; }
        public string Server { get; }
        public string Title { get; }
        public string Detail { get; }
        public AgentLink Link { get; }
        public string ToolTip => Target.Entry(Server)?.Command == null ? null : "Khai báo hiện tại: " + Target.Entry(Server).Command;
    }
}
