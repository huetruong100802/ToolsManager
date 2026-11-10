using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Toolbox.Core;

namespace Toolbox.Core.Agents
{
    /// <summary>
    /// Chế độ "Hỏi trước mỗi lần chạy": lần agent gọi lệnh chạy, Toolbox ghi một đề xuất <see cref="ProposalKinds.Run"/>
    /// và trả lời agent là chưa chạy. Bạn duyệt ở trang Agent, rồi agent gọi lại đúng lệnh đó: lần này được chạy
    /// và đề xuất chuyển sang "đã dùng", nên lần sau phải xin lại. Khớp theo tên lệnh và tham số, không theo id.
    /// </summary>
    public static class RunApprovals
    {
        /// <summary>Tên trường trong <see cref="Proposal.Fields"/> giữ lệnh đã chuẩn hoá để so khớp và hiển thị.</summary>
        public const string PrintField = "lệnh";

        /// <summary>
        /// Null nếu lệnh được phép chạy ngay (đã có duyệt; duyệt được tiêu thụ). Ngược lại trả thông báo cho agent,
        /// kèm id đề xuất. Lệnh giống hệt đã đang chờ thì dùng lại đề xuất đó, không tạo thêm.
        /// </summary>
        public static string Check(string proposalsPath, AgentRecord agent, string toolName, IDictionary<string, object> arguments)
        {
            var print = Print(toolName, arguments);
            string message = null;
            // Đã chọn "Luôn cho lệnh này": chạy ngay, không tạo đề xuất.
            if (agent.AllowRuns != null && agent.AllowRuns.Contains(print, StringComparer.Ordinal))
                return null;
            ProposalStore.Update(proposalsPath, store =>
            {
                var approved = store.Proposals.FirstOrDefault(p => p.Kind == ProposalKinds.Run && p.State == ProposalStates.Approved
                    && p.AgentId == agent.Id && p.Fields != null && p.Fields.TryGetValue(PrintField, out var f) && f == print);
                if (approved != null)
                {
                    approved.State = ProposalStates.Used;
                    approved.DecidedAt = ListFile.Now();
                    return true;
                }

                // Lệnh vừa bị từ chối: nói lý do cho agent một lần, không tạo đề xuất mới ngay (tránh hỏi dồn người dùng).
                var rejected = store.Proposals.FirstOrDefault(p => p.Kind == ProposalKinds.Run && p.State == ProposalStates.Rejected
                    && p.AgentId == agent.Id && p.Fields != null && p.Fields.TryGetValue(PrintField, out var r) && r == print);
                if (rejected != null)
                {
                    rejected.State = ProposalStates.Used;
                    message = RejectedMessage;
                    return true;
                }

                var pending = store.Add(new Proposal
                {
                    AgentId = agent.Id,
                    AgentName = agent.Name,
                    WorkspaceId = agent.WorkspaceId,
                    Kind = ProposalKinds.Run,
                    ToolId = toolName,
                    ToolName = toolName,
                    Fields = new Dictionary<string, string> { [PrintField] = print },
                    Reason = "Agent muốn chạy lệnh này.",
                });
                message = "Lệnh này cần bạn duyệt trước khi chạy. Đã gửi đề xuất " + pending.Id
                    + " ở Toolbox, trang Agent › Chờ duyệt. Sau khi bạn duyệt, gọi lại đúng lệnh này để chạy.";
                return true;
            });
            return message;
        }

        public const string RejectedMessage = "Người dùng đã từ chối cho chạy lệnh này. Đừng gọi lại nếu chưa hỏi người dùng; hãy hỏi họ muốn làm gì khác.";

        /// <summary>Lệnh chuẩn hoá của một đề xuất chạy lệnh (rỗng nếu không phải đề xuất chạy).</summary>
        public static string PrintOf(Proposal proposal) =>
            proposal?.Fields != null && proposal.Fields.TryGetValue(PrintField, out var print) ? print : "";

        /// <summary>Số đề xuất chạy lệnh đang chờ duyệt.</summary>
        public static int PendingCount(IEnumerable<Proposal> proposals) =>
            (proposals ?? Enumerable.Empty<Proposal>()).Count(p => p.Kind == ProposalKinds.Run && p.IsPending);

        /// <summary>Ghi nhớ lệnh này cho agent: lần sau chạy luôn. Tối đa 100 lệnh mỗi agent.</summary>
        public static void AllowAlways(string agentsPath, string agentId, string print)
        {
            if (string.IsNullOrEmpty(print))
                return;
            AgentStore.Update(agentsPath, store =>
            {
                var agent = store.Find(agentId);
                if (agent == null)
                    return false;
                var list = agent.AllowRuns ?? (agent.AllowRuns = new List<string>());
                if (list.Contains(print, StringComparer.Ordinal))
                    return false;
                list.Add(print);
                if (list.Count > 100)
                    list.RemoveAt(0);
                return true;
            });
        }

        /// <summary>Quên mọi lệnh đã cho phép luôn của agent. Trả về số lệnh đã quên.</summary>
        public static int ForgetAllowed(string agentsPath, string agentId)
        {
            var count = 0;
            AgentStore.Update(agentsPath, store =>
            {
                var agent = store.Find(agentId);
                count = agent?.AllowRuns?.Count ?? 0;
                if (count == 0)
                    return false;
                agent.AllowRuns = null;
                return true;
            });
            return count;
        }

        /// <summary>
        /// Quyết định một đề xuất chạy lệnh: duyệt một lần, duyệt luôn (<paramref name="always"/>), hoặc từ chối. Ghi nhật ký.
        /// Dùng chung cho trang Agent và nút trên thông báo.
        /// </summary>
        public static void Decide(string proposalsPath, string agentsPath, string activityPath, Proposal proposal, bool approve, bool always = false)
        {
            if (approve && always)
                AllowAlways(agentsPath, proposal.AgentId, PrintOf(proposal));
            ProposalStore.Update(proposalsPath, store => { store.Decide(proposal.Id, approve, null); return true; });
            ActivityLog.Append(activityPath, new ActivityEntry
            {
                AgentId = proposal.AgentId, AgentName = proposal.AgentName, WorkspaceId = proposal.WorkspaceId,
                Action = approve ? "approve" : "reject", Target = proposal.ToolName ?? proposal.ToolId,
                Result = approve ? "applied" : "rejected",
            });
        }

        /// <summary>Chuỗi chuẩn hoá tên lệnh và tham số, dùng để so khớp đề xuất với lần gọi lại.</summary>
        public static string Print(string toolName, IDictionary<string, object> arguments)
        {
            var args = arguments == null || arguments.Count == 0
                ? "{}"
                : new JavaScriptSerializer().Serialize(arguments.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));
            return toolName + " " + args;
        }
    }
}
