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

    /// <summary>
    /// Một tool: lệnh gốc cộng các hậu tố. Hậu tố (lệnh con như <c>serve</c>, cờ như <c>--port</c>) không khai báo ở đây;
    /// chúng đến từ bảng khả năng của tool (quét từ trợ giúp hoặc bạn nhập tay), nên tool nào cũng dùng được.
    /// </summary>
    public sealed class ToolDef
    {
        public ToolDef(string kind, string name, string rule, DetectRule detect, ActionsFromDef actionsFrom, string command = null)
        {
            Kind = kind;
            Name = name;
            Rule = rule;
            Detect = detect;
            ActionsFrom = actionsFrom;
            Command = command;
        }

        public string Kind { get; }
        public string Name { get; }
        /// <summary>Vì sao tool tự áp dụng, hiện ở dòng thông tin của tab.</summary>
        public string Rule { get; }
        /// <summary>Cách nhận diện trong thư mục (null nếu tool không tự nhận diện).</summary>
        public DetectRule Detect { get; }
        /// <summary>Hậu tố lấy từ nguồn trong thư mục chạy, ví dụ script trong package.json (null nếu lấy từ bảng khả năng).</summary>
        public ActionsFromDef ActionsFrom { get; }
        /// <summary>Lệnh gốc, ví dụ <c>npx ng</c>; null nếu tool chỉ có hậu tố từ <see cref="ActionsFrom"/>.</summary>
        public string Command { get; }

        /// <summary>Lệnh trợ giúp để quét hậu tố: của chính tool (<paramref name="suffix"/> rỗng) hoặc của một lệnh con.</summary>
        public string HelpCommand(string suffix) =>
            string.IsNullOrEmpty(Command) ? null : Command + (string.IsNullOrEmpty(suffix) ? "" : " " + suffix) + " --help";
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

        /// <summary>
        /// Các action của một binding. Mỗi action là một hậu tố của lệnh gốc, nhãn là chính chữ đó. Nguồn: script đọc từ file
        /// của binding (npm), hoặc lệnh con trong bảng khả năng của tool (quét hay nhập tay). Chưa có bảng thì chưa có action.
        /// </summary>
        public static IReadOnlyList<ActionDef> ActionsFor(Workspace workspace, ToolBinding binding)
        {
            var tool = Find(binding.Kind);
            if (tool == null)
                return new ActionDef[0];
            if (tool.ActionsFrom != null)
            {
                var from = tool.ActionsFrom;
                return ReadSource(workspace.ResolveCwd(binding.Cwd), from.Source)
                    .Select(name => new ActionDef(name, name, new VariantDef(from.VariantId, name, from.Template.Replace("{id}", name))))
                    .ToArray();
            }
            if (string.IsNullOrEmpty(tool.Command))
                return new ActionDef[0];

            var capabilities = LoadCapabilities(tool.Kind);
            return capabilities == null
                ? new ActionDef[0]
                : CapabilityStore.Subcommands(capabilities).Select(sub => FromSubcommand(tool, capabilities, sub.Name)).ToArray();
        }

        /// <summary>
        /// Action của một lệnh con: <c>lệnh-gốc lệnh-con [cờ-cổng {port}] {args}</c>. Cờ cổng chỉ có khi bạn đã gán vai trò
        /// "port" cho một cờ của lệnh con; <c>{args}</c> là chỗ gõ mọi hậu tố khác.
        /// </summary>
        public static ActionDef FromSubcommand(ToolDef tool, ToolCapabilities capabilities, string subcommand)
        {
            var port = CapabilityStore.PortFlag(capabilities, subcommand);
            var parameters = new List<ParamDef>();
            var template = tool.Command + " " + subcommand;
            if (port != null)
            {
                template += " " + port.Name + " {port}";
                parameters.Add(new ParamDef("port", "Cổng", "", isPort: true));
            }
            template += " {args}";
            parameters.Add(new ParamDef("args", "Hậu tố thêm", ""));
            return new ActionDef(subcommand, subcommand, new VariantDef("default", subcommand, template, parameters.ToArray()));
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

                var command = Text(tool, "command")?.Trim();
                var actionsFrom = ParseActionsFrom(Get(tool, "actionsFrom") as Dictionary<string, object>, kind);
                if (string.IsNullOrEmpty(command) && actionsFrom == null)
                    throw new InvalidDataException($"\"{kind}\": cần \"command\" (lệnh gốc) hoặc \"actionsFrom\".");
                if (command != null && command.Any(c => "&|<>^\"%\r\n".IndexOf(c) >= 0))
                    throw new InvalidDataException($"\"{kind}\": command không được có các ký tự & | < > ^ \" % hoặc xuống dòng.");
                tools.Add(new ToolDef(
                    kind,
                    Text(tool, "name") ?? kind,
                    Text(tool, "rule") ?? "",
                    ParseDetect(Get(tool, "detect") as Dictionary<string, object>, kind),
                    actionsFrom,
                    string.IsNullOrEmpty(command) ? null : command));
            }
            return tools;
        }

        /// <summary>Bảng khả năng của tool; file hỏng thì null (tool coi như chưa có hậu tố nào).</summary>
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
                var text = File.ReadAllText(path, Encoding.UTF8);
                var upgraded = UpgradeOutdated(path, text);
                if (!ReferenceEquals(upgraded, text))
                {
                    text = upgraded;
                    info = new FileInfo(path);
                    _stamp = info.LastWriteTimeUtc;
                    _length = info.Length;
                }
                _tools = Parse(text);
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            {
                LastError = $"Không đọc được {path}: {ex.Message}\nTạm dùng danh mục trước đó hoặc mặc định.";
                if (_tools == null)
                    _tools = Parse(DefaultJson());
            }
        }

        /// <summary>
        /// Catalog cũ (schemaVersion thấp hơn bản mặc định) khai báo action và biến thể ngay trong file, mô hình đã bỏ.
        /// Sao lưu thành <c>catalog.json.v{n}.bak</c> rồi ghi lại bản mặc định; trả về văn bản mới, hoặc đúng chuỗi cũ nếu không cần.
        /// File không có schemaVersion được coi là do bạn tự viết theo định dạng hiện tại, không đụng tới.
        /// </summary>
        internal static string UpgradeOutdated(string path, string text)
        {
            var found = SchemaOf(text);
            var fresh = DefaultJson();
            if (found <= 0 || found >= SchemaOf(fresh))
                return text;
            File.Copy(path, path + ".v" + found + ".bak", true);
            File.WriteAllText(path, fresh, new UTF8Encoding(false));
            return fresh;
        }

        /// <summary>schemaVersion của catalog; 0 nếu thiếu hoặc không đọc được.</summary>
        private static int SchemaOf(string json)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                return root != null && root.TryGetValue("schemaVersion", out var v) ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return 0;
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
