using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Gợi ý cho một dòng không có key (đối số vị trí): giá trị liệt kê được và/hoặc đường dẫn trong thư mục chạy.</summary>
    public sealed class PositionalChoice
    {
        public static readonly PositionalChoice None = new PositionalChoice();

        /// <summary>Lệnh con và giá trị cho phép lấy từ bảng khả năng.</summary>
        public IReadOnlyList<string> Values { get; set; } = new string[0];

        /// <summary>Đối số này là đường dẫn: dropdown thêm file (hoặc thư mục) trong thư mục chạy và nút chọn file.</summary>
        public bool IsPath { get; set; }

        public bool DirectoryOnly { get; set; }

        public bool Any => Values.Count > 0 || IsPath;
    }

    /// <summary>
    /// Tính gợi ý cho đối số vị trí từ dữ liệu đã quét, không biết tên tool nào. Các dòng key trống đứng đầu mà giống tên lệnh con
    /// là "chuỗi lệnh" (<c>migrations add</c>) và quyết định phạm vi; dòng tiếp theo được gợi ý lệnh con của phạm vi đó
    /// cộng giá trị/đường dẫn của đối số "Arguments:" tương ứng. Sau chuỗi lệnh, mỗi dòng key trống là đối số thứ n của phạm vi.
    /// </summary>
    public static class PositionalOptions
    {
        private const int MaxFiles = 400;
        private static readonly string[] SkipDirs = { "bin", "obj", "node_modules", "packages", "dist", "out", "testresults" };

        /// <param name="before">Các cặp đứng trước dòng đang xét (đúng thứ tự).</param>
        public static PositionalChoice Resolve(ToolCapabilities data, string actionId, IEnumerable<ParamPair> before)
        {
            if (data == null)
                return PositionalChoice.None;

            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(actionId))
                tokens.Add(actionId.Trim());
            var inChain = true;
            var positionalAfterChain = 0;
            foreach (var pair in before ?? Enumerable.Empty<ParamPair>())
            {
                var value = (pair.Value ?? "").Trim();
                if (inChain && pair.Key.Length == 0 && value.Length > 0 && CapabilityStore.SubcommandName.IsMatch(value))
                {
                    tokens.Add(value);
                    continue;
                }
                inChain = false;
                if (pair.Key.Length == 0)
                    positionalAfterChain++;
            }

            var scope = string.Join(" ", tokens);
            var args = data.Args.Where(a => Same(a.Action, scope)).ToList();
            var result = new PositionalChoice();
            var values = new List<string>();

            ArgHint arg = null;
            if (inChain)
            {
                // Dòng này có thể là lệnh con tiếp theo hoặc đối số đầu tiên của phạm vi.
                values.AddRange(Children(data, scope));
                arg = args.FirstOrDefault();
            }
            else if (positionalAfterChain < args.Count)
            {
                arg = args[positionalAfterChain];
            }
            else if (args.Count > 0 && args[args.Count - 1].Variadic)
            {
                arg = args[args.Count - 1];
            }

            if (arg != null)
            {
                if (arg.Choices != null)
                    values.AddRange(arg.Choices);
                result.IsPath = arg.IsPath;
                result.DirectoryOnly = arg.DirectoryOnly;
            }
            result.Values = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        /// <summary>Lệnh con trực tiếp của phạm vi (mục không phải cờ, một từ).</summary>
        public static IEnumerable<string> Children(ToolCapabilities data, string scope) =>
            data.Entries
                .Where(e => Same(e.Action, scope) && !CapabilityStore.IsFlag(e.Name) && CapabilityStore.SubcommandName.IsMatch(e.Name ?? "") && e.Name.IndexOf(' ') < 0)
                .Select(e => e.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// File (hoặc thư mục) trong thư mục chạy, sâu tối đa 2 cấp, bỏ thư mục ẩn và thư mục sinh ra (bin, obj, node_modules…).
        /// Đường dẫn tương đối theo thư mục chạy. Danh sách có giới hạn; lọc bằng ô tìm trong menu.
        /// </summary>
        public static IReadOnlyList<string> PathCandidates(string workingDirectory, bool directoriesOnly)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
                return result;
            try
            {
                var queue = new Queue<KeyValuePair<string, int>>();
                queue.Enqueue(new KeyValuePair<string, int>(workingDirectory, 0));
                while (queue.Count > 0 && result.Count < MaxFiles)
                {
                    var current = queue.Dequeue();
                    if (!directoriesOnly)
                        foreach (var file in Directory.EnumerateFiles(current.Key).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                            if (!Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal) && result.Count < MaxFiles)
                                result.Add(Relative(workingDirectory, file));
                    if (current.Value >= 2)
                        continue;
                    foreach (var dir in Directory.EnumerateDirectories(current.Key).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileName(dir);
                        if (name.StartsWith(".", StringComparison.Ordinal) || SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                            continue;
                        if (directoriesOnly && result.Count < MaxFiles)
                            result.Add(Relative(workingDirectory, dir));
                        queue.Enqueue(new KeyValuePair<string, int>(dir, current.Value + 1));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Thư mục không đọc được: trả phần đã có.
            }
            return result;
        }

        public static string Relative(string root, string path)
        {
            var full = Path.GetFullPath(path);
            var baseDir = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? full.Substring(baseDir.Length) : full;
        }

        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
