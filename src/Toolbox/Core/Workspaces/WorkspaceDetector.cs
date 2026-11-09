using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Một tool tìm thấy trong thư mục, kèm lý do để hiện cho người dùng.</summary>
    public sealed class Detection
    {
        public string Kind { get; set; }
        /// <summary>Thư mục chạy tương đối so với gốc; rỗng là gốc.</summary>
        public string Cwd { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Nhìn file đánh dấu trong thư mục để đoán workspace dùng tool nào. Luật (file nào, quét mấy cấp) lấy từ
    /// <c>detect</c> của từng preset trong <see cref="ToolPresets"/>, nên tool mới nhận diện được mà không sửa code.
    /// <see cref="Detection.Kind"/> là kind của preset (cũng là Id của tool thư viện sinh từ preset).
    /// </summary>
    public static class WorkspaceDetector
    {
        private static readonly HashSet<string> SkipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "bin", "obj", ".vs", ".idea", "dist", "packages",
        };

        /// <summary>Mỗi tool lấy đúng một lần: thư mục đầu tiên (gốc trước, rồi thư mục con theo tên) có file đánh dấu.</summary>
        public static IReadOnlyList<Detection> Detect(string folder)
        {
            var found = new List<Detection>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return found;

            foreach (var tool in ToolPresets.All)
            {
                if (tool.Detect == null)
                    continue;
                foreach (var dir in Candidates(folder, tool.Detect.Depth))
                {
                    var marker = FindMarker(dir, tool.Detect.Patterns);
                    if (marker == null)
                        continue;
                    if (tool.Detect.RequireActions && tool.ActionsFrom != null
                        && ToolCatalog.ReadSource(dir, tool.ActionsFrom.Source).Count == 0)
                        continue;

                    var cwd = Relative(folder, dir);
                    var entries = CountEntries(tool, dir, marker);
                    found.Add(new Detection
                    {
                        Kind = tool.Kind,
                        Cwd = cwd,
                        Reason = "Thấy " + marker + (entries > 0 ? $" với {entries} mục" : "") + (cwd.Length > 0 ? $", thư mục chạy {cwd}\\" : ""),
                    });
                    break;
                }
            }

            return found;
        }

        /// <summary>Tên workspace gợi ý: tên thư mục.</summary>
        public static string SuggestName(string folder) =>
            new DirectoryInfo(Path.GetFullPath(folder)).Name;

        /// <summary>Số mục đọc được từ chính file đánh dấu (script trong package.json, project trong angular.json...); 0 nếu không có.</summary>
        private static int CountEntries(ToolDef tool, string dir, string marker)
        {
            return tool.ActionsFrom != null && IsFrom(tool.ActionsFrom.Source, marker)
                ? ToolCatalog.ReadSource(dir, tool.ActionsFrom.Source).Count
                : 0;
        }

        /// <summary>Nguồn đọc từ đúng file đánh dấu vừa tìm thấy.</summary>
        private static bool IsFrom(ValueSource source, string marker) =>
            source?.File != null && string.Equals(source.File, marker, StringComparison.OrdinalIgnoreCase);

        /// <summary>Tên file khớp đầu tiên trong danh sách mẫu (có <c>*</c> thì là glob); null nếu không có.</summary>
        private static string FindMarker(string dir, IReadOnlyList<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.IndexOf('*') >= 0)
                {
                    var file = Directory.GetFiles(dir, pattern).FirstOrDefault();
                    if (file != null)
                        return Path.GetFileName(file);
                }
                else if (File.Exists(Path.Combine(dir, pattern)) || Directory.Exists(Path.Combine(dir, pattern)))
                {
                    return pattern;
                }
            }
            return null;
        }

        /// <summary>Gốc, và nếu depth là 1 thì thêm các thư mục con trực tiếp (bỏ thư mục rác).</summary>
        private static IEnumerable<string> Candidates(string folder, int depth)
        {
            yield return folder;
            if (depth < 1)
                yield break;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                if (!SkipFolders.Contains(Path.GetFileName(dir)))
                    yield return dir;
        }

        private static string Relative(string root, string dir)
        {
            var rel = dir.Substring(root.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            return rel;
        }
    }
}
