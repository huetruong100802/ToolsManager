using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;

namespace Toolbox.Mcp
{
    /// <summary>Nơi lưu danh sách agent, đề xuất và nhật ký, cùng danh tính agent của phiên MCP này (--agent).</summary>
    public sealed class AgentContext
    {
        /// <summary>Id agent trong agents.json. Rỗng: agent chưa đăng ký, vẫn ghi nhật ký và gửi đề xuất ẩn danh.</summary>
        public string AgentId { get; set; }
        public string AgentsPath { get; set; }
        public string ProposalsPath { get; set; }
        public string ActivityPath { get; set; }

        public static AgentContext Default(string agentId) => new AgentContext
        {
            AgentId = agentId,
            AgentsPath = AgentStore.DefaultPath,
            ProposalsPath = ProposalStore.DefaultPath,
            ActivityPath = ActivityLog.DefaultPath,
        };
    }

    /// <summary>Danh tính và quyền của agent: chặn khi agent bị gỡ, chặn chạy khi tắt quyền chạy, gửi đề xuất thay cho sửa tool dùng chung, ghi nhật ký.</summary>
    public sealed partial class McpServer
    {
        private AgentContext _agent;
        private bool _proposed;

        private static readonly HashSet<string> RunNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "run_tool", "run_profile", "run_custom", "run_stack", "run_action", "run",
        };

        // Công cụ làm thay đổi dữ liệu hoặc chạy lệnh thì ghi nhật ký; công cụ chỉ đọc thì không.
        private static readonly HashSet<string> LoggedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "register_tool", "update_tool", "unregister_tool", "add_tool", "remove_tool", "bind_library_tool",
            "create_profile", "update_profile", "delete_profile", "create_custom", "update_custom", "delete_custom",
            "create_stack", "update_stack", "delete_stack", "set_worktree", "prepare_worktree",
            "run_tool", "run_profile", "run_custom", "run_stack", "run_action",
            "save", "delete", "attach", "run", "propose",
        };

        private bool HasAgentContext => _agent != null;

        /// <summary>Lỗi trả cho agent nếu nó không được gọi công cụ này; null nếu được. <paramref name="record"/> là bản ghi agent (có thể null).</summary>
        private string AgentError(string toolName, out AgentRecord record)
        {
            record = null;
            if (_agent == null || string.IsNullOrEmpty(_agent.AgentId))
                return null;

            record = AgentStore.Load(_agent.AgentsPath).Find(_agent.AgentId);
            if (record == null)
                return $"Agent \"{_agent.AgentId}\" chưa đăng ký hoặc đã bị gỡ khỏi Toolbox. Mở Toolbox, trang Agent, để kết nối lại.";
            if (!string.Equals(record.WorkspaceId ?? "", _workspaceId ?? "", StringComparison.OrdinalIgnoreCase))
                return "Agent này được cấp cho phạm vi khác. Mở Toolbox, trang Agent, để kiểm tra phạm vi.";
            if (RunNames.Contains(toolName) && RunModes.Normalize(record.RunMode) != RunModes.Auto)
                return "Agent này không được phép chạy lệnh. Nhờ người dùng bật quyền chạy ở Toolbox, trang Agent, hoặc tự chạy trong Toolbox.";
            return null;
        }

        private void TouchAgent()
        {
            if (_agent == null || string.IsNullOrEmpty(_agent.AgentId))
                return;
            try { AgentStore.Touch(_agent.AgentsPath, _agent.AgentId); }
            catch (IOException) { /* ghi lần cuối hoạt động là phụ, không làm hỏng lệnh của agent */ }
        }

        private void RecordActivity(string toolName, Dictionary<string, object> arguments, object result, AgentRecord record, string forcedResult = null)
        {
            if (_agent == null || !LoggedNames.Contains(toolName))
                return;
            try
            {
                ActivityLog.Append(_agent.ActivityPath, new ActivityEntry
                {
                    AgentId = _agent.AgentId,
                    AgentName = record?.Name ?? "Agent chưa đăng ký",
                    WorkspaceId = _workspaceId,
                    Action = toolName,
                    Target = TargetOf(arguments),
                    Result = forcedResult ?? (IsError(result) ? "failed" : "applied"),
                });
            }
            catch (IOException) { /* nhật ký là phụ */ }
        }

        private static bool IsError(object result) =>
            result is Dictionary<string, object> d && d.TryGetValue("isError", out var e) && e is bool b && b;

        private static string TargetOf(Dictionary<string, object> arguments)
        {
            foreach (var key in new[] { "label", "name", "kind", "id", "profile", "stack" })
            {
                var value = GetString(arguments, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        /// <summary>update_tool trong workspace: không sửa tool dùng chung, chỉ gửi đề xuất cho người dùng duyệt.</summary>
        private object ProposeToolUpdate(ToolStore store, Dictionary<string, object> arguments, Workspace workspace, AgentRecord record)
        {
            var id = GetString(arguments, "id");
            if (!IsBound(workspace, id))
                return NotInWorkspace(id);
            var tool = store.Find(id);
            if (tool == null)
                return ToolResult($"Không có tool \"{id}\". Dùng list_tools để xem id.", isError: true);

            var fields = ProposalApplier.FieldsOf(ToRequest(arguments));
            fields.Remove("id");
            if (fields.Count == 0)
                return ToolResult("Không có trường nào để đề xuất. Truyền ít nhất một trong: " + string.Join(", ", ProposalApplier.Fields) + ".", isError: true);

            return SubmitProposal(ProposalKinds.UpdateTool, tool, fields, GetString(arguments, "reason"), workspace, record);
        }

        private object SubmitProposal(string kind, ToolEntry tool, Dictionary<string, string> fields, string reason, Workspace workspace, AgentRecord record)
        {
            Proposal saved = null;
            _proposed = true;
            ProposalStore.Update(_agent.ProposalsPath, proposals =>
            {
                saved = proposals.Add(new Proposal
                {
                    AgentId = _agent.AgentId,
                    AgentName = record?.Name ?? "Agent chưa đăng ký",
                    WorkspaceId = workspace.Id,
                    Kind = kind,
                    ToolId = tool.Id,
                    ToolName = tool.Name,
                    Fields = fields,
                    Reason = reason,
                });
                return true;
            });

            return ToolResult($"Đã gửi đề xuất {saved.Id} cho người dùng duyệt trong Toolbox, trang Agent. Tool dùng chung \"{tool.Id}\" chưa đổi. "
                + "Trong lúc chờ, muốn khác biệt riêng cho workspace này thì dùng profile hoặc lệnh riêng.", isError: false);
        }
    }
}
