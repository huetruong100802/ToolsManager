using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.Agents
{
    /// <summary>Áp dụng một đề xuất đã duyệt vào danh sách tool dùng chung. GUI gọi khi người dùng bấm Duyệt.</summary>
    public static class ProposalApplier
    {
        /// <summary>Các trường đề xuất đổi, theo tên trường của update_tool.</summary>
        public static readonly string[] Fields = { "name", "path", "args", "command", "description", "tags", "notes" };

        /// <summary>Gom các trường agent truyền vào thành bản đề xuất, bỏ trường trống. Tags cách nhau bằng dấu phẩy.</summary>
        public static Dictionary<string, string> FieldsOf(AgentTools.Request request)
        {
            var fields = new Dictionary<string, string>();
            void Put(string key, string value)
            {
                if (value != null)
                    fields[key] = value;
            }
            Put("name", request.Name);
            Put("path", request.Path);
            Put("args", request.Args);
            Put("command", request.Command);
            Put("description", request.Description);
            Put("notes", request.Notes);
            if (request.Tags != null)
                Put("tags", string.Join(",", request.Tags));
            return fields;
        }

        public static AgentTools.Request ToRequest(Proposal proposal)
        {
            string Get(string key) => proposal.Fields != null && proposal.Fields.TryGetValue(key, out var v) ? v : null;
            var tags = Get("tags");
            return new AgentTools.Request
            {
                Id = proposal.ToolId,
                Name = Get("name"),
                Path = Get("path"),
                Args = Get("args"),
                Command = Get("command"),
                Description = Get("description"),
                Notes = Get("notes"),
                Tags = tags == null ? null : tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
            };
        }

        /// <summary>
        /// "Chỉ cho workspace này" chỉ áp dụng khi đề xuất sửa duy nhất <c>args</c> và biết workspace đề xuất.
        /// </summary>
        /// <param name="reason">Khi trả false: lý do hiện cho người dùng.</param>
        public static bool CanScopeToWorkspace(Proposal proposal, out string reason)
        {
            reason = null;
            if (proposal.Kind != ProposalKinds.UpdateTool)
                reason = "Chỉ áp dụng cho đề xuất sửa tool, không áp dụng cho đề xuất xoá.";
            else if (string.IsNullOrWhiteSpace(proposal.WorkspaceId))
                reason = "Đề xuất không thuộc workspace nào.";
            else if (proposal.Fields == null || proposal.Fields.Count == 0
                     || proposal.Fields.Keys.Any(k => !string.Equals(k, "args", StringComparison.OrdinalIgnoreCase)))
                reason = "Đề xuất đổi cả trường khác ngoài tham số (args); chỉ đổi tham số mới giới hạn được theo workspace.";
            return reason == null;
        }

        /// <summary>
        /// Ghi <c>args</c> đề xuất vào tool đã gắn trong workspace đề xuất; thư viện và workspace khác không đổi.
        /// Người gọi tự <see cref="WorkspaceStore.Save"/> (nên dùng <see cref="WorkspaceStore.Update"/>).
        /// Ném lỗi nếu không áp dụng được; khi đó chưa ghi gì.
        /// </summary>
        public static string ApplyToWorkspace(Workspaces.WorkspaceStore workspaces, Proposal proposal)
        {
            if (!CanScopeToWorkspace(proposal, out var reason))
                throw new InvalidOperationException(reason);
            var workspace = workspaces.Find(proposal.WorkspaceId)
                ?? throw new System.IO.InvalidDataException($"Workspace \"{proposal.WorkspaceId}\" không còn.");
            var binding = workspace.FindLibrary(proposal.ToolId)
                ?? throw new System.IO.InvalidDataException(
                    $"Workspace \"{workspace.Name}\" không còn gắn tool \"{proposal.ToolName ?? proposal.ToolId}\".");

            var key = proposal.Fields.Keys.First();
            // Tham số rỗng nghĩa là "dùng tham số của thư viện" (LibraryBinding.EffectiveArgs).
            binding.Args = proposal.Fields[key];
            return $"Đã đổi tham số của \"{proposal.ToolName ?? proposal.ToolId}\" chỉ trong workspace \"{workspace.Name}\".";
        }

        /// <summary>
        /// Ghi thay đổi vào <paramref name="store"/> (người gọi giữ <see cref="ToolStore.Lock"/> và tự Save).
        /// Ném lỗi nếu tool không còn hoặc nội dung không hợp lệ; khi đó chưa có gì được ghi.
        /// </summary>
        public static string Apply(ToolStore store, Proposal proposal)
        {
            if (store.Find(proposal.ToolId) == null)
                throw new System.IO.InvalidDataException($"Tool \"{proposal.ToolId}\" không còn trong thư viện.");

            if (proposal.Kind == ProposalKinds.DeleteTool)
            {
                store.Remove(proposal.ToolId);
                return $"Đã bỏ \"{proposal.ToolName ?? proposal.ToolId}\" khỏi thư viện.";
            }

            var updated = AgentTools.Update(store, proposal.ToolId, ToRequest(proposal));
            return $"Đã cập nhật \"{updated.Tool.Name}\".";
        }
    }
}
