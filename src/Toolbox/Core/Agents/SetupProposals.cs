using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core.Agents
{
    /// <summary>Một lệnh agent đề xuất khi thiết lập workspace. Chưa áp dụng gì cho tới khi người dùng duyệt.</summary>
    [DataContract]
    public class SetupItem
    {
        [DataMember(Name = "label")] public string Label { get; set; }
        [DataMember(Name = "commandLine")] public string CommandLine { get; set; }
        [DataMember(Name = "cwd")] public string Cwd { get; set; }
        [DataMember(Name = "reason")] public string Reason { get; set; }

        /// <summary>Trạng thái chọn trong sheet duyệt.</summary>
        public bool IsChecked { get; set; } = true;
    }

    [DataContract]
    internal class SetupReply
    {
        [DataMember(Name = "commands")] public List<SetupItem> Commands { get; set; }
    }

    public sealed class SetupParseResult
    {
        public List<SetupItem> Items { get; } = new List<SetupItem>();
        /// <summary>Số mục bị bỏ vì thiếu trường, thư mục chạy ra ngoài workspace hoặc trùng lệnh đã có.</summary>
        public int Skipped { get; set; }
        /// <summary>Có khi đầu ra không phải JSON đúng hình dạng: không mục nào được lấy.</summary>
        public string Error { get; set; }
        public bool Ok => Error == null;
    }

    /// <summary>
    /// Prompt và bộ đọc kết quả cho "AI thiết lập" gọi thẳng. Agent chỉ được trả về JSON; mọi thứ khác bị từ chối.
    /// Đọc chặt: thư mục chạy phải nằm trong workspace, lệnh một dòng, trùng lệnh đã có thì bỏ.
    /// </summary>
    public static class SetupProposals
    {
        public const int MaxItems = 20;

        public static string BuildPrompt(Workspace workspace, IEnumerable<string> detectedTools)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Bạn đang chạy trong thư mục gốc của một dự án. Hãy đọc dự án (README, package.json, *.csproj, docker-compose, Makefile, script) và đề xuất các lệnh nên ghim để chạy dự án bằng một cú bấm: cài đặt, chạy dev, build, test, lint.");
            var detected = (detectedTools ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (detected.Count > 0)
                sb.AppendLine("Toolbox đã nhận ra: " + string.Join(", ", detected) + ".");
            var existing = workspace.Customs.Select(c => c.CommandLine).Where(x => !string.IsNullOrWhiteSpace(x)).Take(30).ToList();
            if (existing.Count > 0)
                sb.AppendLine("Đã có sẵn (đừng đề xuất lại): " + string.Join("; ", existing) + ".");
            sb.AppendLine();
            sb.AppendLine("Quy tắc:");
            sb.AppendLine("- Chỉ đọc, không sửa file, không chạy lệnh của dự án.");
            sb.AppendLine("- Tối đa " + MaxItems + " lệnh, mỗi lệnh một dòng, đúng thứ người dùng gõ ở terminal.");
            sb.AppendLine("- cwd là thư mục chạy tính từ gốc dự án (để trống nếu chạy ở gốc), không dùng đường dẫn tuyệt đối hoặc \"..\".");
            sb.AppendLine("- label ngắn, tiếng Việt, ví dụ \"Chạy dev\". reason một câu nói vì sao có lệnh này.");
            sb.AppendLine();
            sb.AppendLine("Chỉ trả về MỘT đối tượng JSON, không văn bản nào khác, theo mẫu:");
            sb.AppendLine("{\"commands\":[{\"label\":\"Chạy dev\",\"commandLine\":\"npm run start\",\"cwd\":\"web\",\"reason\":\"script start trong web/package.json\"}]}");
            return sb.ToString();
        }

        /// <summary>Lấy đối tượng JSON đầu tiên trong đầu ra (chấp nhận rào ``` hoặc vài dòng dẫn) rồi kiểm từng mục.</summary>
        public static SetupParseResult Parse(string output, Workspace workspace)
        {
            var result = new SetupParseResult();
            var start = (output ?? "").IndexOf('{');
            var end = (output ?? "").LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                result.Error = "Agent không trả về JSON. Đầu ra:\n" + Excerpt(output);
                return result;
            }

            SetupReply reply;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(output.Substring(start, end - start + 1))))
                    reply = Json.Read<SetupReply>(stream);
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is ArgumentException)
            {
                result.Error = "Không đọc được JSON của agent (" + ex.Message + "). Đầu ra:\n" + Excerpt(output);
                return result;
            }

            if (reply?.Commands == null)
            {
                result.Error = "JSON thiếu mảng \"commands\". Đầu ra:\n" + Excerpt(output);
                return result;
            }

            var existing = new HashSet<string>(workspace.Customs.Select(c => Norm(c.CommandLine)), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in reply.Commands)
            {
                if (item == null || result.Items.Count >= MaxItems)
                {
                    result.Skipped++;
                    continue;
                }
                var line = (item.CommandLine ?? "").Trim();
                var label = (item.Label ?? "").Trim();
                var cwd = (item.Cwd ?? "").Trim().Replace('/', '\\').Trim('\\');
                if (label.Length == 0 || line.Length == 0 || line.IndexOfAny(new[] { '\r', '\n' }) >= 0
                    || !SafeCwd(workspace, cwd) || existing.Contains(Norm(line)) || !seen.Add(Norm(line) + "|" + cwd))
                {
                    result.Skipped++;
                    continue;
                }
                result.Items.Add(new SetupItem { Label = label, CommandLine = line, Cwd = cwd, Reason = (item.Reason ?? "").Trim() });
            }
            if (result.Items.Count == 0)
                result.Error = result.Skipped > 0
                    ? "Agent không đề xuất lệnh mới hợp lệ nào (" + result.Skipped + " mục bị bỏ vì trùng hoặc không hợp lệ)."
                    : "Agent không đề xuất lệnh nào.";
            return result;
        }

        /// <summary>Thêm các mục đã duyệt thành lệnh riêng. Trả về số lệnh đã thêm; lỗi kiểm của <see cref="WorkspaceEditor"/> bỏ qua mục đó.</summary>
        public static int Apply(Workspace workspace, IEnumerable<SetupItem> approved)
        {
            var added = 0;
            foreach (var item in approved)
            {
                var command = new CustomCommand { Label = item.Label, CommandLine = item.CommandLine, Cwd = string.IsNullOrEmpty(item.Cwd) ? null : item.Cwd };
                if (WorkspaceEditor.CreateCustom(workspace, command) == null)
                    added++;
            }
            return added;
        }

        private static bool SafeCwd(Workspace workspace, string cwd)
        {
            if (cwd.Length == 0)
                return true;
            if (Path.IsPathRooted(cwd) || cwd.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return false;
            return !cwd.Split('\\').Any(part => part == "..");
        }

        private static string Norm(string line) => (line ?? "").Trim();

        private static string Excerpt(string text)
        {
            text = (text ?? "").Trim();
            return text.Length <= 600 ? text : text.Substring(0, 600) + "…";
        }
    }
}
