using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Core.Workspaces
{
    public enum HintAction { None, OpenTasks, RunInstall, RelocateWorkspace }

    /// <summary>Lời giải thích và việc nên làm tiếp cho một tác vụ lỗi.</summary>
    public sealed class FailureHint
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Explain { get; set; }
        public HintAction Action { get; set; }
        public string ActionText { get; set; }

        /// <summary>Lệnh đề xuất khi <see cref="Action"/> là <see cref="HintAction.RunInstall"/>.</summary>
        public string Command { get; set; }
    }

    /// <summary>
    /// Nhận dạng các lỗi hay gặp khi chạy lệnh từ mã thoát và đuôi log, để thẻ lỗi nói được "chuyện gì" và "làm gì tiếp"
    /// thay vì chỉ có mã thoát. Không đoán bừa: không khớp mẫu nào thì trả null.
    /// </summary>
    public static class FailureHints
    {
        public const int CopyLogLines = 40;

        private static readonly Regex PortBusy = new Regex(
            @"EADDRINUSE|address already in use|Only one usage of each socket address|port\s+(?<p>\d{2,5})\s+is already (?:in use|being used)|Port\s+(?<p>\d{2,5})\s+is already in use",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PortNumber = new Regex(@"(?::|port\s+)(?<p>\d{2,5})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NotFoundWin = new Regex(@"'(?<c>[^']+)' is not recognized as an internal or external command", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NotFoundPs = new Regex(@"The term '(?<c>[^']+)' is not recognized", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NotFoundUnix = new Regex(@"(?<c>[\w.\-]+): (?:command )?not found", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MissingModule = new Regex(
            @"Cannot find module|MODULE_NOT_FOUND|Cannot find package|node_modules[\\/]\.bin[\\/].*(?:not found|not recognized)|sh: .*: not found.*node_modules",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Denied = new Regex(@"EACCES|EPERM|Access is denied|permission denied|Operation not permitted", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="logTail">Các dòng cuối của log.</param>
        /// <param name="dirExists">Kiểm thư mục chạy còn không (mặc định <see cref="Directory.Exists"/>); đưa vào để test.</param>
        public static FailureHint Analyze(int? exitCode, IEnumerable<string> logTail, string cwd, Func<string, bool> dirExists = null, bool hasPackageJson = false)
        {
            var lines = (logTail ?? Enumerable.Empty<string>()).ToList();
            var text = string.Join("\n", lines);
            var exists = dirExists ?? Directory.Exists;

            if (!string.IsNullOrWhiteSpace(cwd) && !exists(cwd))
                return new FailureHint
                {
                    Code = "cwd-missing", Title = "Thư mục chạy không còn",
                    Explain = "Thư mục chạy đã bị đổi tên, di chuyển hoặc xoá, nên lệnh không có chỗ để chạy.",
                    Action = HintAction.RelocateWorkspace, ActionText = "Trỏ lại thư mục",
                };

            var busy = PortBusy.Match(text);
            if (busy.Success)
            {
                var port = busy.Groups["p"].Success ? busy.Groups["p"].Value : PortNumber.Match(text).Groups["p"].Value;
                return new FailureHint
                {
                    Code = "port-busy", Title = string.IsNullOrEmpty(port) ? "Cổng đang bận" : $"Cổng {port} đang bận",
                    Explain = "Có chương trình khác đang giữ cổng này. Dừng tác vụ đang giữ cổng, hoặc chọn cổng khác rồi chạy lại.",
                    Action = HintAction.OpenTasks, ActionText = "Xem tác vụ đang chạy",
                };
            }

            var notFound = NotFoundWin.Match(text);
            if (!notFound.Success)
                notFound = NotFoundPs.Match(text);
            if (!notFound.Success && (exitCode == 127 || exitCode == 9009))
                notFound = NotFoundUnix.Match(text);
            if (notFound.Success || exitCode == 9009 || exitCode == 127)
            {
                var command = notFound.Success ? notFound.Groups["c"].Value : "lệnh";
                // Thiếu công cụ của dự án node (ng, vite, tsc...) thường là chưa cài gói, không phải thiếu trên máy.
                if (hasPackageJson && !IsGlobalTool(command))
                    return InstallHint($"Chưa cài gói để chạy \"{command}\"");
                return new FailureHint
                {
                    Code = "command-not-found", Title = $"Không tìm thấy lệnh \"{command}\"",
                    Explain = "Lệnh này không có trên PATH của máy. Cài chương trình đó (hoặc thêm vào PATH) rồi chạy lại; nếu là tool đã đăng ký, kiểm đường dẫn ở Thư viện.",
                    Action = HintAction.None,
                };
            }

            if (MissingModule.IsMatch(text))
                return InstallHint("Thiếu thư viện trong node_modules");

            if (Denied.IsMatch(text))
                return new FailureHint
                {
                    Code = "denied", Title = "Bị từ chối quyền",
                    Explain = "Hệ điều hành không cho truy cập file hoặc cổng. Đóng chương trình đang mở file đó, hoặc chạy ở thư mục bạn có quyền ghi.",
                    Action = HintAction.None,
                };
            return null;
        }

        private static FailureHint InstallHint(string title) => new FailureHint
        {
            Code = "missing-modules", Title = title,
            Explain = "Dự án chưa cài các gói cần thiết. Chạy cài đặt rồi chạy lại.",
            Action = HintAction.RunInstall, ActionText = "Chạy npm install", Command = "npm install",
        };

        private static bool IsGlobalTool(string command) =>
            new[] { "node", "npm", "npx", "git", "dotnet", "python", "pnpm", "yarn", "cargo", "go", "docker" }
                .Contains(Path.GetFileNameWithoutExtension(command), StringComparer.OrdinalIgnoreCase);

        /// <summary>Thư mục chạy tính từ gốc workspace ("." nếu là gốc). Không nằm trong workspace thì chỉ lấy tên thư mục, để không lộ đường dẫn máy.</summary>
        public static string RelativeCwd(string workspaceRoot, string cwd)
        {
            if (string.IsNullOrWhiteSpace(cwd))
                return ".";
            if (!string.IsNullOrWhiteSpace(workspaceRoot))
            {
                var root = workspaceRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var full = cwd.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                    return ".";
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return cwd.TrimEnd('\\', '/').Substring(root.Length).Replace('\\', '/');
            }
            return Path.GetFileName(cwd.TrimEnd('\\', '/'));
        }

        /// <summary>
        /// Văn bản dán cho agent: lệnh, thư mục tương đối, mã thoát, 40 dòng log cuối, và gợi ý đã nhận dạng nếu có.
        /// Không chứa đường dẫn tuyệt đối của máy.
        /// </summary>
        public static string CopyForAgent(string label, string commandLine, string workspaceRoot, string cwd, int? exitCode, IEnumerable<string> logLines, FailureHint hint = null)
        {
            var tail = (logLines ?? Enumerable.Empty<string>()).Reverse().Take(CopyLogLines).Reverse().ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Tác vụ \"{label}\" bị lỗi{(exitCode.HasValue ? $" (mã thoát {exitCode})" : "")}. Giúp tôi tìm nguyên nhân và cách sửa.");
            sb.AppendLine();
            sb.AppendLine("Lệnh: " + commandLine);
            sb.AppendLine("Thư mục chạy (tính từ gốc workspace): " + RelativeCwd(workspaceRoot, cwd));
            if (hint != null)
                sb.AppendLine("Toolbox nhận dạng: " + hint.Title + ". " + hint.Explain);
            sb.AppendLine();
            sb.AppendLine($"{tail.Count} dòng log cuối:");
            sb.AppendLine("```");
            foreach (var line in tail)
                sb.AppendLine(line);
            sb.AppendLine("```");
            return sb.ToString();
        }
    }
}
