using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Kết quả cho một file: copied | applied | unchanged | skipped | conflict | missing | error.</summary>
    public sealed class PrepareLine
    {
        public PrepareLine(string file, string status, string detail)
        {
            File = file;
            Status = status;
            Detail = detail;
        }

        public string File { get; }
        public string Status { get; }
        public string Detail { get; }

        /// <summary>Cần người dùng xem: file bị bỏ qua, lệch, thiếu hoặc lỗi.</summary>
        public bool IsProblem => Status == "skipped" || Status == "conflict" || Status == "missing" || Status == "error";
    }

    public sealed class PrepareReport
    {
        public List<PrepareLine> Lines { get; } = new List<PrepareLine>();

        /// <summary>Lỗi làm cả lần chuẩn bị không chạy được (không phải worktree, thiếu thư mục...). Null nếu chạy được.</summary>
        public string Error { get; set; }

        public bool Ok => Error == null && Lines.All(l => !l.IsProblem);
    }

    /// <summary>
    /// Đưa file local của workspace (không commit) vào một worktree. File không theo git thì copy;
    /// file theo git đã sửa thì áp diff so với HEAD. Không bao giờ ghi đè nội dung đã khác, và không chạy gì nếu worktree không cùng repo.
    /// </summary>
    public static class WorktreePreparer
    {
        public static PrepareReport Prepare(Workspace workspace, string worktreePath)
        {
            var report = new PrepareReport();
            if (!workspace.PathExists)
            {
                report.Error = "Không tìm thấy thư mục workspace: " + workspace.ResolvedPath;
                return report;
            }
            if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
            {
                report.Error = "Không tìm thấy thư mục worktree: " + worktreePath;
                return report;
            }

            var source = Trim(Path.GetFullPath(workspace.ResolvedPath));
            var target = Trim(Path.GetFullPath(worktreePath));
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                report.Error = "Đường dẫn này là chính workspace, không phải worktree mới.";
                return report;
            }
            if (!SameRepository(source, target))
            {
                report.Error = "Thư mục không phải worktree của cùng repo với workspace.";
                return report;
            }

            foreach (var file in workspace.Worktree?.Files ?? new List<string>())
                report.Lines.Add(PrepareFile(source, target, file));
            return report;
        }

        private static PrepareLine PrepareFile(string source, string target, string file)
        {
            var rel = (file ?? "").Trim().Replace('\\', '/');
            if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Split('/').Any(p => p == ".."))
                return new PrepareLine(file, "error", "Đường dẫn phải tương đối so với workspace và không chứa \"..\".");

            var src = Path.Combine(source, rel);
            var dst = Path.Combine(target, rel);

            if (Git(source, "ls-files", "--error-unmatch", "--", rel).Ok)
            {
                var patch = Git(source, "diff", "--no-color", "--no-ext-diff", "HEAD", "--", rel).Output;
                if (string.IsNullOrWhiteSpace(patch))
                    return new PrepareLine(rel, "unchanged", "Theo git, không đổi so với HEAD.");
                if (ApplyPatch(target, patch, reverse: true))
                    return new PrepareLine(rel, "unchanged", "Đã áp từ trước.");
                if (!ApplyPatch(target, patch, reverse: false))
                    return new PrepareLine(rel, "conflict", "Nội dung worktree lệch HEAD nên không áp được diff. Cần xử lý tay.");
                return new PrepareLine(rel, "applied", "Đã áp diff so với HEAD.");
            }

            if (!File.Exists(src))
                return new PrepareLine(rel, "missing", "Không có trong workspace.");

            if (File.Exists(dst))
            {
                return SameBytes(src, dst)
                    ? new PrepareLine(rel, "unchanged", "Đã có, giống nguồn.")
                    : new PrepareLine(rel, "skipped", "Worktree đã có nội dung khác, không ghi đè.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst);
            return new PrepareLine(rel, "copied", "");
        }

        private static bool ApplyPatch(string cwd, string patch, bool reverse)
        {
            var temp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(temp, patch, new UTF8Encoding(false));
                var args = "apply --whitespace=nowarn " + (reverse ? "--reverse --check " : "") + Quote(temp);
                if (reverse)
                    return Git(cwd, args).Ok;
                return Git(cwd, "apply --check --whitespace=nowarn " + Quote(temp)).Ok
                    && Git(cwd, "apply --whitespace=nowarn " + Quote(temp)).Ok;
            }
            finally
            {
                File.Delete(temp);
            }
        }

        private static bool SameRepository(string source, string target)
        {
            var a = Git(source, "rev-parse --path-format=absolute --git-common-dir");
            var b = Git(target, "rev-parse --path-format=absolute --git-common-dir");
            return a.Ok && b.Ok && string.Equals(Trim(a.Output.Trim()), Trim(b.Output.Trim()), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameBytes(string a, string b)
        {
            var x = File.ReadAllBytes(a);
            var y = File.ReadAllBytes(b);
            return x.Length == y.Length && x.SequenceEqual(y);
        }

        private static string Trim(string path) => path.TrimEnd('\\', '/');

        private static string Quote(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";

        private sealed class GitResult
        {
            public bool Ok;
            public string Output;
        }

        /// <summary>Chạy git với đối số đã ghép sẵn. Không ném lỗi: không có git hay lệnh lỗi đều trả Ok=false.</summary>
        private static GitResult Git(string cwd, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = cwd,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using (var process = Process.Start(info))
                {
                    var error = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    error.Wait();
                    return new GitResult { Ok = process.ExitCode == 0, Output = output };
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                return new GitResult { Ok = false, Output = "" };
            }
        }

        /// <summary>Overload nhận danh sách đối số đã tách; dùng cho lệnh có tên file.</summary>
        private static GitResult Git(string cwd, params string[] args) => Git(cwd, string.Join(" ", args.Select(Arg)));

        private static string Arg(string a) => a.Contains(' ') || a.Contains('"') ? Quote(a) : a;
    }
}
