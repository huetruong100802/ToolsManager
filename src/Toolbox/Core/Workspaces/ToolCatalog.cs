using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Tham số điền vào lệnh, ví dụ cổng.</summary>
    public sealed class ParamDef
    {
        public ParamDef(string key, string prompt, string defaultValue, bool isPort = false)
        {
            Key = key;
            Prompt = prompt;
            Default = defaultValue;
            IsPort = isPort;
        }

        public string Key { get; }
        public string Prompt { get; }
        public string Default { get; }
        /// <summary>Tham số là cổng: kiểm tra 1024–65535 và trùng cổng.</summary>
        public bool IsPort { get; }
    }

    /// <summary>Một cách chạy của action, ví dụ "dev" hay "production". Template dùng <c>{key}</c> cho tham số.</summary>
    public sealed class VariantDef
    {
        public VariantDef(string id, string label, string template, params ParamDef[] parameters)
        {
            Id = id;
            Label = label;
            Template = template;
            Params = parameters;
        }

        public string Id { get; }
        public string Label { get; }
        public string Template { get; }
        public IReadOnlyList<ParamDef> Params { get; }
        public bool HasParams => Params.Count > 0;
    }

    public sealed class ActionDef
    {
        public ActionDef(string id, string label, params VariantDef[] variants)
        {
            Id = id;
            Label = label;
            Variants = variants;
        }

        public string Id { get; }
        public string Label { get; }
        /// <summary>Biến thể đầu tiên là mặc định.</summary>
        public IReadOnlyList<VariantDef> Variants { get; }
        public VariantDef Default => Variants[0];
        public VariantDef Variant(string id) =>
            Variants.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }

    public sealed class ToolDef
    {
        public ToolDef(string kind, string name, string rule, params ActionDef[] actions)
        {
            Kind = kind;
            Name = name;
            Rule = rule;
            Actions = actions;
        }

        public string Kind { get; }
        public string Name { get; }
        /// <summary>Vì sao tool tự áp dụng, hiện ở dòng thông tin của tab.</summary>
        public string Rule { get; }
        public IReadOnlyList<ActionDef> Actions { get; }
    }

    /// <summary>Danh mục tool có sẵn và các lệnh của chúng. Chỉ lệnh .NET/git/Angular/npm; lệnh khác là "Lệnh riêng".</summary>
    public static class ToolCatalog
    {
        public const string DotNet = "dotnet";
        public const string Git = "git";
        public const string Angular = "angular";
        public const string Npm = "npm";

        /// <summary>Tên script npm hợp lệ để ghép vào dòng lệnh.</summary>
        private static readonly Regex SafeScript = new Regex(@"^[A-Za-z0-9][A-Za-z0-9:_.\-]*$", RegexOptions.Compiled);

        private static readonly ToolDef[] Tools =
        {
            new ToolDef(DotNet, ".NET", "sln/csproj",
                new ActionDef("build", "Build", new VariantDef("debug", "Debug", "dotnet build"), new VariantDef("release", "Release", "dotnet build -c Release")),
                new ActionDef("test", "Test", new VariantDef("all", "Tất cả", "dotnet test"), new VariantDef("filter", "Theo tên", "dotnet test --filter \"{filter}\"", new ParamDef("filter", "Bộ lọc test", "FullyQualifiedName~"))),
                new ActionDef("run", "Chạy", new VariantDef("default", "Mặc định", "dotnet run"), new VariantDef("port", "Theo cổng", "dotnet run --urls http://localhost:{port}", new ParamDef("port", "Cổng", "5000", isPort: true))),
                new ActionDef("restore", "Restore", new VariantDef("default", "Mặc định", "dotnet restore")),
                new ActionDef("clean", "Clean", new VariantDef("default", "Mặc định", "dotnet clean"))),

            new ToolDef(Git, "git", ".git",
                new ActionDef("status", "Trạng thái", new VariantDef("short", "Gọn", "git status -sb"), new VariantDef("full", "Đầy đủ", "git status")),
                new ActionDef("pull", "Pull", new VariantDef("rebase", "Rebase", "git pull --rebase"), new VariantDef("merge", "Merge", "git pull")),
                new ActionDef("fetch", "Fetch", new VariantDef("all", "Mọi remote", "git fetch --all --prune")),
                new ActionDef("log", "Lịch sử", new VariantDef("recent", "20 commit gần nhất", "git log --oneline -20"), new VariantDef("count", "Số commit tuỳ chọn", "git log --oneline -{n}", new ParamDef("n", "Số commit", "50"))),
                new ActionDef("diff", "Diff", new VariantDef("stat", "Thống kê", "git diff --stat"))),

            new ToolDef(Angular, "Angular", "angular.json",
                new ActionDef("serve", "Chạy dev", new VariantDef("dev", "Dev", "npx ng serve --port {port}", new ParamDef("port", "Cổng", "4200", isPort: true)),
                    new VariantDef("open", "Dev + mở trình duyệt", "npx ng serve --open --port {port}", new ParamDef("port", "Cổng", "4200", isPort: true))),
                new ActionDef("build", "Build", new VariantDef("dev", "Dev", "npx ng build"), new VariantDef("prod", "Production", "npx ng build --configuration production")),
                new ActionDef("test", "Test", new VariantDef("watch", "Theo dõi", "npx ng test"), new VariantDef("once", "Một lần", "npx ng test --watch=false")),
                new ActionDef("lint", "Lint", new VariantDef("default", "Mặc định", "npx ng lint"))),

            // Action của npm lấy từ package.json lúc hiển thị (xem ActionsFor).
            new ToolDef(Npm, "npm", "package.json"),
        };

        public static IReadOnlyList<ToolDef> All => Tools;

        public static ToolDef Find(string kind) =>
            Tools.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

        /// <summary>Các action của một binding. Với npm đọc <c>scripts</c> trong package.json tại thư mục chạy.</summary>
        public static IReadOnlyList<ActionDef> ActionsFor(Workspace workspace, ToolBinding binding)
        {
            var tool = Find(binding.Kind);
            if (tool == null)
                return new ActionDef[0];
            if (tool.Kind != Npm)
                return tool.Actions;

            return ReadNpmScripts(Path.Combine(workspace.ResolveCwd(binding.Cwd), "package.json"))
                .Select(name => new ActionDef(name, name, new VariantDef("run", name, "npm run " + name)))
                .ToArray();
        }

        public static ActionDef FindAction(Workspace workspace, ToolBinding binding, string actionId) =>
            ActionsFor(workspace, binding).FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tên script trong package.json; file thiếu hoặc hỏng thì trả danh sách rỗng. Bỏ tên có ký tự lạ.</summary>
        public static IReadOnlyList<string> ReadNpmScripts(string packageJsonPath)
        {
            try
            {
                if (!File.Exists(packageJsonPath))
                    return new string[0];
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(packageJsonPath)) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue("scripts", out var scripts) || !(scripts is Dictionary<string, object> map))
                    return new string[0];
                return map.Keys.Where(k => SafeScript.IsMatch(k)).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
