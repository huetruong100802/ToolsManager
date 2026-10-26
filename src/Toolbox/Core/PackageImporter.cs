using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core
{
    /// <summary>Nhập gói: giải nén các tool được chọn rồi thêm vào danh sách.</summary>
    public static class PackageImporter
    {
        /// <param name="choices">Id tool trong gói → cách xử lý nếu trùng id. Tool không có trong choices hoặc Skip thì không nhập.</param>
        public static IList<ToolEntry> Import(ToolStore store, string zipPath, string destRoot, IDictionary<string, ConflictResolution> choices)
        {
            // Bỏ qua tool Skip đang trùng id ngay từ đầu để không giải nén thừa.
            var wanted = choices
                .Where(c => c.Value != ConflictResolution.Skip || store.Find(c.Key) == null)
                .Select(c => c.Key)
                .ToList();

            var imported = new List<ToolEntry>();
            foreach (var entry in Packer.Extract(zipPath, destRoot, wanted))
            {
                var added = store.Import(entry, choices[entry.Id]);
                if (added != null)
                    imported.Add(added);
            }
            return imported;
        }
    }
}
