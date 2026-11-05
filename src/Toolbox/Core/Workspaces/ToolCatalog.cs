using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Toolbox.Core;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Cách lấy một danh sách (tên action hoặc gợi ý tham số) từ catalog.</summary>
    public enum SourceKind
    {
        /// <summary>Khoá của một object JSON, ví dụ script trong package.json hay project trong angular.json.</summary>
        Json,
        /// <summary>Nhóm 1 của mỗi dòng khớp regex trong một file bất kỳ, ví dụ target trong Makefile.</summary>
        Pattern,
        /// <summary>Danh sách ghi sẵn trong catalog.</summary>
        Values,
    }

    /// <summary>
    /// Nguồn danh sách của tool, khai báo trong catalog bằng một trong ba dạng:
    /// <c>{"file", "jsonPath"}</c>, <c>{"file", "pattern"}</c> (nhóm 1 của regex, mỗi dòng khớp một mục)
    /// hoặc <c>{"values": [...]}</c>. File luôn tương đối với thư mục chạy và không được ra ngoài thư mục đó.
    /// </summary>
    public sealed class ValueSource
    {
        private ValueSource(SourceKind kind, string file, string jsonPath, string pattern, IReadOnlyList<string> items)
        {
            Kind = kind;
            File = file;
            JsonPath = jsonPath;
            Pattern = pattern;
            Items = items;
        }

        public static ValueSource FromJson(string file, string jsonPath) =>
            new ValueSource(SourceKind.Json, file, jsonPath, null, null);

        public static ValueSource FromPattern(string file, string pattern) =>
            new ValueSource(SourceKind.Pattern, file, null, pattern, null);

        public static ValueSource FromValues(IReadOnlyList<string> values) =>
            new ValueSource(SourceKind.Values, null, null, null, values);

        public SourceKind Kind { get; }
        /// <summary>File tương đối với thư mục chạy; null với nguồn <see cref="SourceKind.Values"/>.</summary>
        public string File { get; }
        public string JsonPath { get; }
        public string Pattern { get; }
        public IReadOnlyList<string> Items { get; }
    }

    /// <summary>Tham số điền vào lệnh, ví dụ cổng. Nếu có <see cref="Options"/> thì ô có nút ▾ mở danh sách gợi ý, vẫn gõ tay được.</summary>
    public sealed class ParamDef
    {
        public ParamDef(string key, string prompt, string defaultValue, bool isPort = false, ValueSource options = null)
        {
            Key = key;
            Prompt = prompt;
            Default = defaultValue;
            IsPort = isPort;
            Options = options;
        }

        public string Key { get; }
        public string Prompt { get; }
        public string Default { get; }
        /// <summary>Tham số là cổng: kiểm tra 1024–65535 và trùng cổng.</summary>
        public bool IsPort { get; }
        /// <summary>Nguồn danh sách gợi ý (null nếu không có).</summary>
        public ValueSource Options { get; }
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

    /// <summary>Action sinh từ các mục của một nguồn (ví dụ script trong package.json). <see cref="Template"/> có <c>{id}</c> cho tên.</summary>
    public sealed class ActionsFromDef
    {
        public ActionsFromDef(ValueSource source, string template, string variantId)
        {
            Source = source;
            Template = template;
            VariantId = variantId;
        }

        public ValueSource Source { get; }
        public string Template { get; }
        public string VariantId { get; }
    }

    /// <summary>
    /// Cách nhận diện tool trong thư mục. <see cref="Patterns"/>: tên file hoặc glob (có <c>*</c>). <see cref="Depth"/>:
    /// 0 chỉ thư mục gốc, 1 gốc và các thư mục con trực tiếp. <see cref="RequireActions"/>: chỉ nhận khi đọc được action.
    /// </summary>
    public sealed class DetectRule
    {
        public DetectRule(IReadOnlyList<string> patterns, int depth, bool requireActions)
        {
            Patterns = patterns;
            Depth = depth;
            RequireActions = requireActions;
        }

        public IReadOnlyList<string> Patterns { get; }
        public int Depth { get; }
        public bool RequireActions { get; }
    }

    /// <summary>Lệnh trợ giúp của một action, dùng để quét khả năng (cờ, tuỳ chọn). Chạy trong thư mục chạy của binding.</summary>
    public sealed class ScanDef
    {
        public ScanDef(string actionId, string command)
        {
            ActionId = actionId;
            Command = command;
        }

        public string ActionId { get; }
        public string Command { get; }
    }

    public sealed class ToolDef
    {
        public ToolDef(string kind, string name, string rule, DetectRule detect, ActionsFromDef actionsFrom, params ActionDef[] actions)
        {
            Kind = kind;
            Name = name;
            Rule = rule;
            Detect = detect;
            ActionsFrom = actionsFrom;
            Actions = actions;
        }

        public string Kind { get; }
        public string Name { get; }
        /// <summary>Vì sao tool tự áp dụng, hiện ở dòng thông tin của tab.</summary>
        public string Rule { get; }
        /// <summary>Cách nhận diện trong thư mục (null nếu tool không tự nhận diện).</summary>
        public DetectRule Detect { get; }
        /// <summary>Action lấy từ nguồn trong thư mục chạy (null nếu action cố định trong catalog).</summary>
        public ActionsFromDef ActionsFrom { get; }
        public IReadOnlyList<ActionDef> Actions { get; }
        /// <summary>Lệnh trợ giúp để quét khả năng, theo action. Rỗng nếu tool không khai báo.</summary>
        public IReadOnlyList<ScanDef> Scans { get; set; } = new ScanDef[0];
    }

    /// <summary>
    /// Danh mục tool và lệnh, đọc từ <c>catalog.json</c> trong thư mục dữ liệu, nên thêm tool mới chỉ cần sửa file,
    /// không build lại app. Lần đầu chạy file được chép từ mặc định nhúng sẵn. File đổi thì đọc lại ở lần gọi sau.
    /// File lỗi: giữ danh mục đọc được lần trước (hoặc mặc định nếu chưa có) và ghi lỗi vào <see cref="LastError"/>.
    /// </summary>
    public static class ToolCatalog
    {
        /// <summary>Tên tool dựng sẵn mà test và bộ chạy dùng làm mẫu. Không có gì khác trong code gắn với tool cụ thể.</summary>
        public const string DotNet = "dotnet";
        public const string Git = "git";

        public const string FileName = "catalog.json";
        private const string EmbeddedName = "catalog.default.json";

        /// <summary>Tên action, khoá, tên file... hợp lệ để ghép vào dòng lệnh.</summary>
        private static readonly Regex SafeKey = new Regex(@"^[A-Za-z0-9][A-Za-z0-9:_.\-@]*$", RegexOptions.Compiled);

        /// <summary>Giới hạn thời gian của regex do catalog khai báo, để một pattern xấu không treo app.</summary>
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private static readonly object Gate = new object();
        private static IReadOnlyList<ToolDef> _tools;
        private static DateTime _stamp;
        private static long _length = -1;

        /// <summary>Lỗi đọc <c>catalog.json</c> lần gần nhất; null nếu ổn.</summary>
        public static string LastError { get; private set; }

        public static string FilePath => Path.Combine(DataDir.Root, FileName);

        public static IReadOnlyList<ToolDef> All
        {
            get
            {
                lock (Gate)
                {
                    Reload();
                    return _tools;
                }
            }
        }

        public static ToolDef Find(string kind) =>
            All.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

        /// <summary>Các action của một binding. Action lấy từ nguồn (như script npm) đọc tại thư mục chạy của binding.</summary>
        public static IReadOnlyList<ActionDef> ActionsFor(Workspace workspace, ToolBinding binding)
        {
            var tool = Find(binding.Kind);
            if (tool == null)
                return new ActionDef[0];
            if (tool.ActionsFrom == null)
            {
                var capabilities = LoadCapabilities(tool.Kind);
                return tool.Actions.Select(a => WithCapabilities(a, capabilities)).ToArray();
            }

            var from = tool.ActionsFrom;
            return ReadSource(workspace.ResolveCwd(binding.Cwd), from.Source)
                .Select(name => new ActionDef(name, name, new VariantDef(from.VariantId, name, from.Template.Replace("{id}", name))))
                .ToArray();
        }

        public static ActionDef FindAction(Workspace workspace, ToolBinding binding, string actionId) =>
            ActionsFor(workspace, binding).FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Gợi ý cho một tham số có nguồn, đọc theo thư mục chạy (rỗng nếu không có nguồn hoặc file thiếu).</summary>
        public static IReadOnlyList<string> OptionsFor(ParamDef param, string workingDirectory) =>
            ReadSource(workingDirectory, param.Options);

        /// <summary>
        /// Danh sách của một nguồn: khoá JSON, nhóm regex hoặc danh sách ghi sẵn. Chỉ giữ tên an toàn để ghép vào lệnh.
        /// File thiếu hoặc hỏng thì rỗng, không ném lỗi: setup không có file đó thì chỉ là không có gợi ý.
        /// </summary>
        public static IReadOnlyList<string> ReadSource(string workingDirectory, ValueSource source)
        {
            if (source == null)
                return new string[0];
            if (source.Kind == SourceKind.Values)
                return source.Items.Where(IsSafe).Distinct().ToList();
            if (string.IsNullOrEmpty(workingDirectory))
                return new string[0];

            var file = Path.Combine(workingDirectory, source.File);
            return source.Kind == SourceKind.Json
                ? ReadKeys(file, source.JsonPath)
                : ReadPattern(file, source.Pattern);
        }

        /// <summary>Khoá của object JSON tại <paramref name="jsonPath"/>, chỉ giữ tên an toàn. File thiếu hoặc hỏng thì rỗng.</summary>
        public static IReadOnlyList<string> ReadKeys(string jsonFile, string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonFile))
                    return new string[0];
                object node = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(jsonFile));
                foreach (var part in (jsonPath ?? "").Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var map = node as Dictionary<string, object>;
                    if (map == null || !map.TryGetValue(part, out node))
                        return new string[0];
                }
                var keys = node as Dictionary<string, object>;
                return keys == null ? new List<string>() : keys.Keys.Where(IsSafe).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        /// <summary>Đọc catalog từ JSON. Ném <see cref="InvalidDataException"/> nếu cấu trúc sai.</summary>
        public static IReadOnlyList<ToolDef> Parse(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            var items = root == null ? null : Get(root, "tools") as object[];
            if (items == null)
                throw new InvalidDataException("catalog.json phải có mảng \"tools\".");

            var tools = new List<ToolDef>();
            foreach (var item in items)
            {
                var tool = item as Dictionary<string, object>;
                if (tool == null)
                    throw new InvalidDataException("Mỗi phần tử của \"tools\" phải là một object.");
                var kind = Text(tool, "kind");
                if (kind == null || !SafeKey.IsMatch(kind))
                    throw new InvalidDataException($"Tool có kind không hợp lệ: \"{kind}\".");
                if (tools.Any(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Trùng kind \"{kind}\" trong catalog.json.");

                var actions = (Get(tool, "actions") as object[] ?? new object[0]).Select(a => ParseAction(a as Dictionary<string, object>, kind)).ToArray();
                tools.Add(new ToolDef(
                    kind,
                    Text(tool, "name") ?? kind,
                    Text(tool, "rule") ?? "",
                    ParseDetect(Get(tool, "detect") as Dictionary<string, object>, kind),
                    ParseActionsFrom(Get(tool, "actionsFrom") as Dictionary<string, object>, kind),
                    actions) { Scans = ParseScans(tool, kind) });
            }
            return tools;
        }

        /// <summary>
        /// Token cờ cổng trong mẫu lệnh, ví dụ <c>npx ng serve {project} {portFlag} {port}</c>. Tên cờ thật lấy từ bảng
        /// khả năng (vai trò "port"), không ghi trong catalog. Chưa gán thì token còn nguyên và lệnh không chạy được.
        /// </summary>
        public const string PortFlagToken = "{portFlag}";

        /// <summary>Thay <see cref="PortFlagToken"/> trong các mẫu lệnh của action bằng cờ cổng đã gán; chưa có cờ thì giữ nguyên.</summary>
        public static ActionDef WithCapabilities(ActionDef action, ToolCapabilities capabilities)
        {
            if (!action.Variants.Any(v => v.Template.Contains(PortFlagToken)))
                return action;

            var flag = capabilities == null ? null : CapabilityStore.PortFlag(capabilities, action.Id);
            var name = flag != null && CapabilityStore.FlagName.IsMatch(flag.Name) ? flag.Name : null;
            var variants = action.Variants
                .Select(v => v.Template.Contains(PortFlagToken)
                    ? new VariantDef(v.Id, v.Label, v.Template.Replace(PortFlagToken, name ?? PortFlagToken), v.Params.ToArray())
                    : v)
                .ToArray();
            return new ActionDef(action.Id, action.Label, variants);
        }

        /// <summary>Bảng khả năng của tool; file hỏng thì null (lệnh có token cờ sẽ báo chưa có cờ).</summary>
        private static ToolCapabilities LoadCapabilities(string kind)
        {
            try
            {
                return CapabilityStore.Load(CapabilityStore.DefaultDirectory, kind);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static IReadOnlyList<ScanDef> ParseScans(Dictionary<string, object> tool, string kind)
        {
            var items = Get(tool, "scan") as object[];
            if (items == null)
                return new ScanDef[0];
            return items.Select(item =>
            {
                var map = item as Dictionary<string, object>;
                var action = map == null ? null : Text(map, "action");
                var command = map == null ? null : Text(map, "command");
                if (action == null || !SafeKey.IsMatch(action) || string.IsNullOrWhiteSpace(command))
                    throw new InvalidDataException($"\"{kind}\": mỗi mục scan cần action và command.");
                return new ScanDef(action, command.Trim());
            }).ToArray();
        }

        private static bool IsSafe(string name) => name != null && SafeKey.IsMatch(name);

        /// <summary>Nhóm 1 của mỗi dòng khớp, chỉ giữ tên an toàn và không trùng. File thiếu, regex lỗi hoặc quá hạn thì rỗng.</summary>
        private static IReadOnlyList<string> ReadPattern(string file, string pattern)
        {
            try
            {
                if (!File.Exists(file))
                    return new string[0];
                var regex = new Regex(pattern, RegexOptions.Multiline, RegexTimeout);
                return regex.Matches(File.ReadAllText(file, Encoding.UTF8)).Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(IsSafe)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is RegexMatchTimeoutException)
            {
                return new string[0];
            }
        }

        private static void Reload()
        {
            var path = FilePath;
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(DataDir.Root);
                    File.WriteAllText(path, DefaultJson(), new UTF8Encoding(false));
                }

                var info = new FileInfo(path);
                if (_tools != null && info.LastWriteTimeUtc == _stamp && info.Length == _length)
                    return;
                // Ghi nhớ trước khi đọc: file lỗi thì không đọc lại liên tục cho tới khi nó đổi.
                _stamp = info.LastWriteTimeUtc;
                _length = info.Length;
                _tools = Parse(File.ReadAllText(path, Encoding.UTF8));
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            {
                LastError = $"Không đọc được {path}: {ex.Message}\nTạm dùng danh mục trước đó hoặc mặc định.";
                if (_tools == null)
                    _tools = Parse(DefaultJson());
            }
        }

        private static string DefaultJson()
        {
            using (var stream = typeof(ToolCatalog).GetTypeInfo().Assembly.GetManifestResourceStream(EmbeddedName))
            {
                if (stream == null)
                    throw new InvalidDataException("Thiếu tài nguyên mặc định của danh mục tool.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static ActionDef ParseAction(Dictionary<string, object> action, string kind)
        {
            if (action == null)
                throw new InvalidDataException($"Action của \"{kind}\" phải là object.");
            var id = Text(action, "id");
            if (id == null || !SafeKey.IsMatch(id))
                throw new InvalidDataException($"Action của \"{kind}\" có id không hợp lệ: \"{id}\".");
            var variants = (Get(action, "variants") as object[] ?? new object[0])
                .Select(v => ParseVariant(v as Dictionary<string, object>, kind, id))
                .ToArray();
            if (variants.Length == 0)
                throw new InvalidDataException($"Action \"{id}\" của \"{kind}\" cần ít nhất một biến thể.");
            return new ActionDef(id, Text(action, "label") ?? id, variants);
        }

        private static VariantDef ParseVariant(Dictionary<string, object> variant, string kind, string action)
        {
            if (variant == null)
                throw new InvalidDataException($"Biến thể của \"{kind} {action}\" phải là object.");
            var id = Text(variant, "id");
            var template = Text(variant, "template");
            if (string.IsNullOrEmpty(id) || !SafeKey.IsMatch(id) || string.IsNullOrWhiteSpace(template))
                throw new InvalidDataException($"Biến thể \"{id}\" của \"{kind} {action}\" cần id và template.");
            var parameters = (Get(variant, "params") as object[] ?? new object[0]).Select(p => ParseParam(p as Dictionary<string, object>, kind, action)).ToArray();
            return new VariantDef(id, Text(variant, "label") ?? id, template, parameters);
        }

        private static ParamDef ParseParam(Dictionary<string, object> param, string kind, string action)
        {
            if (param == null)
                throw new InvalidDataException($"Tham số của \"{kind} {action}\" phải là object.");
            var key = Text(param, "key");
            if (string.IsNullOrEmpty(key) || !Regex.IsMatch(key, @"^[A-Za-z][A-Za-z0-9_]*$"))
                throw new InvalidDataException($"Tham số của \"{kind} {action}\" có key không hợp lệ: \"{key}\".");
            var options = Get(param, "options") as Dictionary<string, object>;
            return new ParamDef(
                key,
                Text(param, "prompt") ?? key,
                Text(param, "default") ?? "",
                Get(param, "port") is bool port && port,
                options == null ? null : ParseSource(options, kind));
        }

        private static DetectRule ParseDetect(Dictionary<string, object> detect, string kind)
        {
            if (detect == null)
                return null;
            var patterns = (Get(detect, "patterns") as object[] ?? new object[0]).Select(p => p as string).ToArray();
            if (patterns.Length == 0 || patterns.Any(p => string.IsNullOrWhiteSpace(p) || p.Contains("..") || Path.IsPathRooted(p)))
                throw new InvalidDataException($"\"{kind}\": detect.patterns phải là tên file tương đối, không rỗng.");
            var depth = Get(detect, "depth") == null ? 0 : Convert.ToInt32(Get(detect, "depth"), CultureInfo.InvariantCulture);
            if (depth < 0 || depth > 1)
                throw new InvalidDataException($"\"{kind}\": detect.depth chỉ nhận 0 hoặc 1.");
            return new DetectRule(patterns, depth, Get(detect, "requireActions") is bool require && require);
        }

        private static ActionsFromDef ParseActionsFrom(Dictionary<string, object> from, string kind)
        {
            if (from == null)
                return null;
            var template = Text(from, "template");
            if (template == null || !template.Contains("{id}"))
                throw new InvalidDataException($"\"{kind}\": actionsFrom.template phải có {{id}}.");
            return new ActionsFromDef(ParseSource(from, kind), template, Text(from, "variant") ?? "run");
        }

        /// <summary>
        /// Một nguồn: <c>values</c> (danh sách ghi sẵn), hoặc <c>file</c> + <c>pattern</c> (nhóm 1 của regex),
        /// hoặc <c>file</c> + <c>jsonPath</c>. Kiểm cấu trúc ngay khi đọc catalog để lỗi hiện một lần, không rơi vào lúc chạy.
        /// </summary>
        private static ValueSource ParseSource(Dictionary<string, object> source, string kind)
        {
            var values = Get(source, "values") as object[];
            if (values != null)
            {
                var items = values.Select(v => (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim()).ToList();
                if (items.Any(i => !IsSafe(i)))
                    throw new InvalidDataException($"\"{kind}\": values có mục không hợp lệ (chỉ chữ, số và : _ . - @).");
                return ValueSource.FromValues(items);
            }

            var file = Text(source, "file");
            if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || Path.IsPathRooted(file))
                throw new InvalidDataException($"\"{kind}\": nguồn cần file tương đối, không ra ngoài thư mục chạy.");

            var pattern = Text(source, "pattern");
            if (pattern != null)
            {
                Regex regex;
                try
                {
                    regex = new Regex(pattern, RegexOptions.Multiline, RegexTimeout);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException($"\"{kind}\": pattern không hợp lệ: {ex.Message}");
                }
                // GetGroupNumbers luôn có 0; cần thêm ít nhất một nhóm (...) để lấy tên.
                if (regex.GetGroupNumbers().Length < 2)
                    throw new InvalidDataException($"\"{kind}\": pattern cần một nhóm (...) để lấy tên.");
                return ValueSource.FromPattern(file, pattern);
            }

            var jsonPath = Text(source, "jsonPath");
            if (jsonPath == null)
                throw new InvalidDataException($"\"{kind}\": nguồn cần jsonPath, pattern hoặc values.");
            return ValueSource.FromJson(file, jsonPath);
        }

        private static object Get(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? value : null;

        private static string Text(Dictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
