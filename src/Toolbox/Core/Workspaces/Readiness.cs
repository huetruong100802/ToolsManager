using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    public enum ReadinessFix
    {
        /// <summary>Không có nút sửa tự động; chỉ cần đọc lời giải thích.</summary>
        None,
        RelocateWorkspace,
        /// <summary>Gỡ tool khỏi workspace (tool không còn trong thư viện).</summary>
        UnbindTool,
        /// <summary>Mở tool trong Thư viện để sửa đường dẫn hoặc lệnh.</summary>
        EditTool,
        /// <summary>Tool cần chạy bộ cài kèm theo.</summary>
        InstallTool,
        /// <summary>Mở profile hỏng để sửa hoặc xoá.</summary>
        OpenProfile,
        /// <summary>Mở workflow có bước hỏng.</summary>
        OpenWorkflow,
    }

    public enum ReadinessLevel { Warning, Error }

    /// <summary>Một thứ còn thiếu trên máy này để workspace chạy được, kèm cách sửa.</summary>
    public sealed class ReadinessIssue
    {
        public ReadinessLevel Level { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public ReadinessFix Fix { get; set; }
        public string FixText { get; set; }

        /// <summary>Id tool, profile hoặc workflow liên quan (tuỳ <see cref="Fix"/>).</summary>
        public string TargetId { get; set; }
    }

    /// <summary>
    /// Kiểm một workspace có chạy được trên máy này không (sau khi nhận gói của đồng nghiệp, hoặc sau khi đổi máy):
    /// thư mục còn không, tool mất đường dẫn hay cần cài, lệnh gốc có trên PATH không, profile và workflow có trỏ vào thứ không còn không.
    /// Trả danh sách kèm cách sửa; danh sách rỗng nghĩa là sẵn sàng.
    /// </summary>
    public static class Readiness
    {
        /// <param name="library">Thư viện tool đang dùng.</param>
        /// <param name="commandExists">Lệnh (tên chương trình) có chạy được không; mặc định tìm trên PATH. Truyền vào để test.</param>
        public static List<ReadinessIssue> Check(Workspace workspace, ToolStore library, Func<string, bool> commandExists = null)
        {
            var issues = new List<ReadinessIssue>();
            commandExists = commandExists ?? OnPath;

            if (!workspace.PathExists)
                issues.Add(new ReadinessIssue
                {
                    Level = ReadinessLevel.Error, Title = "Không tìm thấy thư mục workspace",
                    Detail = "Thư mục đã đổi tên, di chuyển hoặc chưa có trên máy này. Lệnh không chạy được cho tới khi trỏ lại.",
                    Fix = ReadinessFix.RelocateWorkspace, FixText = "Trỏ lại thư mục",
                });

            foreach (var binding in workspace.Bindings)
            {
                var tool = library?.Find(binding.ToolId);
                if (tool == null)
                {
                    issues.Add(new ReadinessIssue
                    {
                        Level = ReadinessLevel.Error, Title = $"Tool \"{binding.ToolId}\" không có trong thư viện",
                        Detail = "Workspace gắn tool này nhưng máy này chưa có nó. Nhận lại gói chứa tool, hoặc gỡ khỏi workspace.",
                        Fix = ReadinessFix.UnbindTool, FixText = "Gỡ khỏi workspace", TargetId = binding.ToolId,
                    });
                    continue;
                }
                CheckTool(tool, issues, commandExists);
            }

            if (library != null)
            {
                using (StackResolver.UseLibrary(library))
                {
                    foreach (var profile in workspace.Profiles)
                    {
                        var resolved = StackResolver.Resolve(workspace, new StackStep
                        {
                            Kind = profile.Kind, ActionId = profile.ActionId, VariantId = profile.VariantId, ProfileId = profile.Id,
                        }, 0, library);
                        if (resolved.IsBroken)
                            issues.Add(new ReadinessIssue
                            {
                                Level = ReadinessLevel.Warning, Title = $"Profile \"{profile.Label}\" không dùng được",
                                Detail = resolved.Problem, Fix = ReadinessFix.OpenProfile, FixText = "Mở profile", TargetId = profile.Id,
                            });
                    }
                    foreach (var stack in workspace.Stacks)
                    {
                        var broken = StackResolver.ResolveAll(workspace, stack, library).Where(r => r.IsBroken).ToList();
                        if (broken.Count > 0)
                            issues.Add(new ReadinessIssue
                            {
                                Level = ReadinessLevel.Warning, Title = $"Workflow \"{stack.Label}\" có {broken.Count} bước hỏng",
                                Detail = $"Bước {broken[0].Index + 1}: {broken[0].Problem}", Fix = ReadinessFix.OpenWorkflow, FixText = "Mở workflow", TargetId = stack.Id,
                            });
                    }
                }
            }
            return issues;
        }

        private static void CheckTool(ToolEntry tool, List<ReadinessIssue> issues, Func<string, bool> commandExists)
        {
            if (tool.CanInstall)
            {
                issues.Add(new ReadinessIssue
                {
                    Level = ReadinessLevel.Error, Title = $"Tool \"{tool.Name}\" cần cài",
                    Detail = "Gói có kèm bộ cài; chạy bộ cài rồi dùng được.", Fix = ReadinessFix.InstallTool, FixText = "Cài đặt", TargetId = tool.Id,
                });
                return;
            }

            if (tool.IsCommand)
            {
                var program = FirstToken(tool.Command);
                if (program != null && !commandExists(program))
                    issues.Add(new ReadinessIssue
                    {
                        Level = ReadinessLevel.Error, Title = $"Lệnh \"{program}\" không có trên máy này",
                        Detail = $"Tool \"{tool.Name}\" chạy lệnh này. Cài chương trình đó hoặc thêm vào PATH.",
                        Fix = ReadinessFix.EditTool, FixText = "Mở tool", TargetId = tool.Id,
                    });
                return;
            }

            if (!string.IsNullOrWhiteSpace(tool.Path) && !tool.PathExists)
                issues.Add(new ReadinessIssue
                {
                    Level = ReadinessLevel.Error, Title = $"Tool \"{tool.Name}\" mất đường dẫn",
                    Detail = "File hoặc thư mục của tool không còn ở đường dẫn đã đăng ký trên máy này.",
                    Fix = ReadinessFix.EditTool, FixText = "Sửa đường dẫn", TargetId = tool.Id,
                });
        }

        /// <summary>Chương trình đầu tiên của dòng lệnh ("npm run dev" → npm; có dấu nháy thì lấy trong nháy).</summary>
        public static string FirstToken(string commandLine)
        {
            var text = (commandLine ?? "").Trim();
            if (text.Length == 0)
                return null;
            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : text.Trim('"');
            }
            var space = text.IndexOf(' ');
            return space < 0 ? text : text.Substring(0, space);
        }

        private static readonly string[] Builtins = { "cmd", "echo", "dir", "set", "start", "powershell", "pwsh", "type", "cd", "copy", "del", "mkdir", "where", "ping" };

        /// <summary>Có thể chạy tên này không: đường dẫn tồn tại, lệnh dựng sẵn của shell, hoặc có trên PATH (thử các đuôi chạy được).</summary>
        public static bool OnPath(string program)
        {
            if (string.IsNullOrWhiteSpace(program))
                return false;
            var name = Environment.ExpandEnvironmentVariables(program);
            if (Builtins.Contains(name, StringComparer.OrdinalIgnoreCase))
                return true;
            if (name.IndexOfAny(new[] { '\\', '/' }) >= 0)
                return File.Exists(name) || Directory.Exists(name);

            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';').Where(e => e.Length > 0).ToList();
            var names = Path.HasExtension(name) ? new[] { name } : new[] { name }.Concat(extensions.Select(e => name + e)).ToArray();
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Select(d => d.Trim().Trim('"'))
                .Where(d => d.Length > 0)
                .Any(d =>
                {
                    try { return names.Any(n => File.Exists(Path.Combine(d, n))); }
                    catch (ArgumentException) { return false; }
                });
        }
    }
}
