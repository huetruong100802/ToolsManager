using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core
{
    /// <summary>
    /// Câu "người nhận sẽ có gì" dùng chung cho nút Chia sẻ ở Thư viện và ở workspace, để hai nơi nói cùng một giọng
    /// và người dùng thấy đúng thứ sẽ rời khỏi máy trước khi gửi.
    /// </summary>
    public static class ShareSummary
    {
        public static string Counts(int tools, int profiles, int stacks, int customs)
        {
            var parts = new List<string>();
            if (stacks > 0) parts.Add(stacks + " workflow");
            if (profiles > 0) parts.Add(profiles + " profile");
            if (tools > 0) parts.Add(tools + " tool");
            if (customs > 0) parts.Add(customs + " lệnh riêng");
            return parts.Count == 0 ? "Chưa có gì để chia sẻ." : string.Join(" · ", parts);
        }

        public static string Header(int tools, int profiles, int stacks, int customs) =>
            "Người nhận sẽ có: " + Counts(tools, profiles, stacks, customs);

        /// <summary>Một dòng cho một tool trong gói: kèm file bao nhiêu, hay chỉ thông tin.</summary>
        public static string ToolLine(PackPreview preview)
        {
            var how = preview.SourceRoot == null ? "chỉ gửi thông tin"
                : preview.Tool.Pack == PackMode.Setup ? $"kèm bộ cài {System.IO.Path.GetFileName(preview.Tool.ResolvedSetup)}, {Format(preview.TotalBytes)}"
                : $"{preview.FileCount} file, {Format(preview.TotalBytes)}";
            return $"• {preview.Tool.Name} — {how}";
        }

        public static IEnumerable<string> Warnings(IEnumerable<PackPreview> previews) =>
            previews.Where(p => p.IsBroadFolder)
                    .Select(p => $"{p.Tool.Name}: lấy cả thư mục rộng ({p.SourceRoot}); nên sửa tool, chọn \"Chỉ lấy file này\".");

        public static string Format(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
        }
    }
}
