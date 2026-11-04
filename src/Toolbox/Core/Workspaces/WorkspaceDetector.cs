using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

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

    /// <summary>Nhìn file đánh dấu trong thư mục để đoán workspace dùng tool nào.</summary>
    public static class WorkspaceDetector
    {
        private static readonly HashSet<string> SkipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "bin", "obj", ".vs", ".idea", "dist", "packages",
        };

        /// <summary>Quét gốc và các thư mục con trực tiếp (không sâu hơn, để nhanh và dễ đoán).</summary>
        public static IReadOnlyList<Detection> Detect(string folder)
        {
            var found = new List<Detection>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return found;

            var dotnet = DotNet(folder);
            if (dotnet != null)
                found.Add(dotnet);

            if (Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")))
                found.Add(new Detection { Kind = ToolCatalog.Git, Cwd = "", Reason = "Thấy thư mục .git" });

            var angularCwd = (string)null;
            foreach (var dir in Candidates(folder))
            {
                var angular = Path.Combine(dir, "angular.json");
                if (angularCwd == null && File.Exists(angular))
                {
                    angularCwd = Relative(folder, dir);
                    var projects = CountAngularProjects(angular);
                    found.Add(new Detection
                    {
                        Kind = ToolCatalog.Angular,
                        Cwd = angularCwd,
                        Reason = "Thấy angular.json" + (projects > 0 ? $" với {projects} project" : "") + (angularCwd.Length > 0 ? $", thư mục chạy {angularCwd}\\" : ""),
                    });
                }
            }

            foreach (var dir in Candidates(folder))
            {
                var package = Path.Combine(dir, "package.json");
                var scripts = ToolCatalog.ReadNpmScripts(package);
                if (scripts.Count == 0)
                    continue;
                var cwd = Relative(folder, dir);
                found.Add(new Detection
                {
                    Kind = ToolCatalog.Npm,
                    Cwd = cwd,
                    Reason = $"Đọc {scripts.Count} script trong package.json" + (cwd.Length > 0 ? $" ({cwd}\\)" : "") + ": " + string.Join(", ", scripts.Take(4)) + (scripts.Count > 4 ? "…" : ""),
                });
                break; // một binding npm là đủ; thêm thủ công nếu có package.json khác
            }

            return found;
        }

        /// <summary>Tên workspace gợi ý: tên thư mục.</summary>
        public static string SuggestName(string folder) =>
            new DirectoryInfo(Path.GetFullPath(folder)).Name;

        private static Detection DotNet(string folder)
        {
            var sln = Directory.GetFiles(folder, "*.sln").Concat(Directory.GetFiles(folder, "*.slnx")).FirstOrDefault();
            if (sln != null)
                return new Detection { Kind = ToolCatalog.DotNet, Cwd = "", Reason = "Thấy " + Path.GetFileName(sln) };
            var proj = Directory.GetFiles(folder, "*.csproj").FirstOrDefault();
            return proj == null ? null : new Detection { Kind = ToolCatalog.DotNet, Cwd = "", Reason = "Thấy " + Path.GetFileName(proj) };
        }

        private static IEnumerable<string> Candidates(string folder)
        {
            yield return folder;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                if (!SkipFolders.Contains(Path.GetFileName(dir)))
                    yield return dir;
        }

        private static string Relative(string root, string dir)
        {
            var rel = dir.Substring(root.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            return rel;
        }

        private static int CountAngularProjects(string angularJson)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(angularJson)) as Dictionary<string, object>;
                return root != null && root.TryGetValue("projects", out var projects) && projects is Dictionary<string, object> map ? map.Count : 0;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return 0;
            }
        }
    }
}
