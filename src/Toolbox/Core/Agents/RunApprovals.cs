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
