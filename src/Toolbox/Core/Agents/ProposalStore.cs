using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Toolbox.Core.Agents
{
    public static class ProposalKinds
    {
        public const string UpdateTool = "update_tool";
        public const string DeleteTool = "delete_tool";
    }

    public static class ProposalStates
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
    }

    /// <summary>Một đề xuất của agent sửa hoặc xoá tool dùng chung. Chỉ có hiệu lực sau khi người dùng duyệt.</summary>
    [DataContract]
    public class Proposal
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "agentId")] public string AgentId { get; set; }
        [DataMember(Name = "agentName")] public string AgentName { get; set; }
        [DataMember(Name = "workspaceId")] public string WorkspaceId { get; set; }
        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "toolId")] public string ToolId { get; set; }
        [DataMember(Name = "toolName")] public string ToolName { get; set; }
        /// <summary>Các trường đề xuất đổi (name, path, args, description, tags, notes) theo dạng chuỗi; tags cách nhau bằng dấu phẩy.</summary>
        [DataMember(Name = "fields")] public Dictionary<string, string> Fields { get; set; }
        [DataMember(Name = "reason")] public string Reason { get; set; }
        [DataMember(Name = "state")] public string State { get; set; }
        [DataMember(Name = "createdAt")] public string CreatedAt { get; set; }
        [DataMember(Name = "decidedAt")] public string DecidedAt { get; set; }

        public bool IsPending => State == ProposalStates.Pending;
    }

    /// <summary>Hộp đề xuất chờ duyệt, lưu trong proposals.json.</summary>
    public class ProposalStore
    {
        private const string LockName = @"Global\ToolboxProposals";
        private readonly List<Proposal> _items;

        private ProposalStore(string filePath, List<Proposal> items)
        {
            FilePath = filePath;
            _items = items;
        }

        public static string DefaultPath =>
            Path.Combine(DataDir.Root, "proposals.json");

        public string FilePath { get; }
        public IReadOnlyList<Proposal> Proposals => _items;

        public IEnumerable<Proposal> Pending => _items.Where(p => p.IsPending);

        public static ProposalStore Load(string filePath) => new ProposalStore(filePath, ListFile.Load<Proposal>(filePath, LockName));

        public void Save() => ListFile.Save(FilePath, LockName, _items);

        public static void Update(string filePath, Func<ProposalStore, bool> change)
        {
            using (ListFile.Lock(LockName))
            {
                var store = Load(filePath);
                if (change(store))
                    store.Save();
            }
        }

        public Proposal Find(string id) =>
            string.IsNullOrEmpty(id) ? null : _items.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Thêm đề xuất chờ duyệt. Đề xuất giống hệt một đề xuất đang chờ (cùng agent, loại, tool, trường) thì trả về cái cũ,
        /// để agent gọi lặp không làm đầy hộp duyệt. Không ghi file; gọi <see cref="Save"/>.
        /// </summary>
        public Proposal Add(Proposal proposal)
        {
            if (string.IsNullOrWhiteSpace(proposal.ToolId))
                throw new ArgumentException("Đề xuất cần có id tool.");
            if (proposal.Kind != ProposalKinds.UpdateTool && proposal.Kind != ProposalKinds.DeleteTool)
                throw new ArgumentException("Loại đề xuất không hợp lệ: " + proposal.Kind);

            var same = Pending.FirstOrDefault(p => p.AgentId == proposal.AgentId && p.Kind == proposal.Kind
                && string.Equals(p.ToolId, proposal.ToolId, StringComparison.OrdinalIgnoreCase) && SameFields(p.Fields, proposal.Fields));
            if (same != null)
                return same;

            proposal.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            proposal.State = ProposalStates.Pending;
            proposal.CreatedAt = ListFile.Now();
            _items.Add(proposal);
            return proposal;
        }

        /// <summary>Đánh dấu đã duyệt hoặc từ chối. Chỉ đề xuất đang chờ mới đổi được. Không ghi file.</summary>
        public Proposal Decide(string id, bool approved)
        {
            var found = Find(id);
            if (found == null)
                throw new KeyNotFoundException("Không có đề xuất " + id);
            if (!found.IsPending)
                throw new InvalidOperationException("Đề xuất này đã được xử lý.");
            found.State = approved ? ProposalStates.Approved : ProposalStates.Rejected;
            found.DecidedAt = ListFile.Now();
            return found;
        }

        private static bool SameFields(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            a = a ?? new Dictionary<string, string>();
            b = b ?? new Dictionary<string, string>();
            return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
        }
    }
}
