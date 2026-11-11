using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core.Agents
{
    /// <summary>
    /// Prompt dựng sẵn ngữ cảnh để người dùng dán vào agent (Claude Code, Cursor...) ngay tại chỗ đang kẹt, thay vì tự viết và giải thích.
    /// Toolbox không tự gọi mô hình: chỉ dựng văn bản. Prompt nhắc đúng tên công cụ MCP trong workspace
    /// (<c>overview</c>, <c>search</c>, <c>get</c>, <c>save</c>, <c>attach</c>, <c>run</c>, <c>logs</c>) và không chứa đường dẫn tuyệt đối của máy.
    /// </summary>
    public static class AgentPrompts
    {
        public const int LogLines = 40;

        private static StringBuilder Header(Workspace workspace, string goal)
        {
            var sb = new StringBuilder();
            sb.AppendLine(goal);
            sb.AppendLine();
            sb.AppendLine($"Workspace Toolbox: \"{workspace.Name}\" (id {workspace.Id}). Bạn đã kết nối qua MCP của Toolbox; dùng công cụ `overview` trước để xem workspace đang có gì.");
            return sb;
        }

        /// <summary>Workspace mới hoặc còn trống: nhờ agent đọc dự án rồi dựng tool, profile, workflow phù hợp.</summary>
        public static string SetupWorkspace(Workspace workspace, IEnumerable<string> detectedTools = null)
        {
            var sb = Header(workspace, "Hãy thiết lập workspace này để tôi chạy dự án bằng một cú bấm.");
            var detected = (detectedTools ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (detected.Count > 0)
                sb.AppendLine("Toolbox nhận ra dự án này dùng: " + string.Join(", ", detected) + ".");
            sb.AppendLine();
            sb.AppendLine("Việc cần làm:");
            sb.AppendLine("1. Đọc cấu trúc dự án (README, package.json, *.csproj, docker-compose, script) để biết cách cài, chạy dev, build, test.");
            sb.AppendLine("2. Dùng `search` (type=library) để xem thư viện đã có tool nào; tool có sẵn thì `attach` vào workspace, chưa có thì `save` (type=tool) để đăng ký.");
            sb.AppendLine("3. Dùng `save` (type=profile) tạo profile cho các lệnh hay dùng (chạy dev, build, test), ghi rõ cổng nếu có.");
            sb.AppendLine("4. Nếu có thứ tự bật (vd. API rồi web), dùng `save` (type=stack) tạo workflow, bước chờ cổng khi cần.");
            sb.AppendLine("5. Chưa cần chạy gì; cuối cùng tóm tắt đã tạo những gì.");
            return sb.ToString();
        }

        /// <summary>Đọc lệnh của một tool: nhờ agent đọc trợ giúp và ghi lại các lệnh con, cờ.</summary>
        public static string ReadToolCommands(Workspace workspace, string toolName, string toolCommand, string relativeCwd)
        {
            var sb = Header(workspace, $"Hãy đọc các lệnh của tool \"{toolName}\" để tôi chọn lệnh khi tạo profile.");
            sb.AppendLine();
            sb.AppendLine("Thông tin:");
            sb.AppendLine("- Lệnh gốc: " + (string.IsNullOrWhiteSpace(toolCommand) ? toolName : toolCommand));
            sb.AppendLine("- Thư mục chạy (tính từ gốc workspace): " + (string.IsNullOrWhiteSpace(relativeCwd) ? "." : relativeCwd));
            sb.AppendLine();
            sb.AppendLine("Việc cần làm: chạy `<lệnh gốc> --help` (và `--help` của từng lệnh con quan trọng) trong thư mục trên, rồi mô tả ngắn từng lệnh con và cờ hay dùng,");
            sb.AppendLine("đánh dấu cờ nào nhận giá trị, cờ nào là cổng. Dùng `get` (type=tool) để xem tool đã đăng ký thế nào; nếu mô tả của tool còn thiếu, dùng `save` (type=tool) gửi đề xuất cập nhật kèm lý do.");
            return sb.ToString();
        }

        /// <summary>Dựng workflow từ mô tả của người dùng.</summary>
        public static string BuildWorkflow(Workspace workspace, string description)
        {
            var sb = Header(workspace, "Hãy dựng một workflow (chuỗi lệnh chạy lần lượt) cho workspace này theo mô tả của tôi.");
            sb.AppendLine();
            sb.AppendLine("Mô tả của tôi: " + (string.IsNullOrWhiteSpace(description) ? "(tôi chưa mô tả; hãy hỏi lại tôi muốn bật gì trước, gì sau)" : description.Trim()));
            sb.AppendLine();
            var profiles = workspace.Profiles.Select(p => p.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Take(20).ToList();
            if (profiles.Count > 0)
                sb.AppendLine("Profile đang có (dùng lại thay vì tạo trùng): " + string.Join("; ", profiles) + ".");
            var stacks = workspace.Stacks.Select(s => s.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Take(10).ToList();
            if (stacks.Count > 0)
                sb.AppendLine("Workflow đang có: " + string.Join("; ", stacks) + ".");
            sb.AppendLine();
            sb.AppendLine("Dùng `search` để tìm profile và lệnh riêng phù hợp, rồi `save` (type=stack) tạo workflow. Bước nào cần chờ dịch vụ sẵn sàng thì để chờ cổng; bước chạy song song thì gom cùng nhóm. Đừng chạy workflow khi tôi chưa đồng ý.");
            return sb.ToString();
        }

        /// <summary>Tìm nguyên nhân tác vụ lỗi. Dùng chung văn bản của "Sao chép cho agent" kèm lời nhờ.</summary>
        public static string DiagnoseFailure(Workspace workspace, string label, string commandLine, string cwd, int? exitCode, IEnumerable<string> logLines, FailureHint hint = null)
        {
            var sb = Header(workspace, "Hãy tìm nguyên nhân và đề xuất cách sửa cho tác vụ bị lỗi dưới đây.");
            sb.AppendLine();
            sb.AppendLine(FailureHints.CopyForAgent(label, commandLine, workspace.ResolvedPath, cwd, exitCode, logLines, hint));
            sb.AppendLine("Có thể dùng `logs` để xem thêm log, và `get` để xem cấu hình profile hoặc workflow liên quan. Sửa xong thì cho tôi biết, đừng tự chạy lại nếu chưa hỏi.");
            return sb.ToString();
        }
    }
}
