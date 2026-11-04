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
