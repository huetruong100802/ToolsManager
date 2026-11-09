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

        /// <summary>
        /// Dựng nguồn từ các trường đã khai báo và kiểm cấu trúc: <paramref name="values"/> (khác null) thắng, rồi
        /// <paramref name="file"/> + <paramref name="pattern"/>, rồi <paramref name="file"/> + <paramref name="jsonPath"/>.
        /// Ném <see cref="InvalidDataException"/> (thông báo mở đầu bằng <paramref name="owner"/>) nếu sai.
        /// </summary>
        internal static ValueSource Create(string file, string jsonPath, string pattern, IEnumerable<string> values, string owner)
        {
            if (values != null)
            {
                var items = values.Select(v => (v ?? "").Trim()).ToList();
                if (items.Any(i => !ToolCatalog.IsSafe(i)))
                    throw new InvalidDataException($"\"{owner}\": values có mục không hợp lệ (chỉ chữ, số và : _ . - @).");
                return FromValues(items);
            }

            if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || System.IO.Path.IsPathRooted(file))
                throw new InvalidDataException($"\"{owner}\": nguồn cần file tương đối, không ra ngoài thư mục chạy.");

            if (pattern != null)
            {
                Regex regex;
                try
                {
                    regex = new Regex(pattern, RegexOptions.Multiline, ToolCatalog.RegexTimeout);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException($"\"{owner}\": pattern không hợp lệ: {ex.Message}");
                }
                // GetGroupNumbers luôn có 0; cần thêm ít nhất một nhóm (...) để lấy tên.
                if (regex.GetGroupNumbers().Length < 2)
                    throw new InvalidDataException($"\"{owner}\": pattern cần một nhóm (...) để lấy tên.");
                return FromPattern(file, pattern);
            }

            if (jsonPath == null)
                throw new InvalidDataException($"\"{owner}\": nguồn cần jsonPath, pattern hoặc values.");
            return FromJson(file, jsonPath);
        }

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

        /// <summary>
        /// Hậu tố từ nguồn khai báo trên tool. Template trống thì là <paramref name="baseCommand"/> cộng <c>{id}</c>.
        /// Nguồn sai cấu trúc (file ra ngoài thư mục chạy, pattern không có nhóm, template thiếu <c>{id}</c>...) thì null:
        /// tool vẫn chạy được, chỉ là không có hậu tố từ nguồn.
        /// </summary>
        public static ActionsFromDef From(SuffixSource source, string baseCommand, string owner)
        {
            if (source == null)
                return null;
            var template = string.IsNullOrWhiteSpace(source.Template)
                ? (string.IsNullOrEmpty(baseCommand) ? null : baseCommand + " {id}")
                : source.Template.Trim();
            if (template == null || !template.Contains("{id}"))
                return null;
            try
            {
                var values = source.Values != null && source.Values.Count > 0 ? source.Values : null;
                return new ActionsFromDef(ValueSource.Create(source.File, source.JsonPath, source.Pattern, values, owner), template, "run");
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
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
        public ToolDef(string kind, string name, string rule, DetectRule detect, ActionsFromDef actionsFrom, string command = null, string helpTemplate = null)
        {
            HelpTemplate = helpTemplate;
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

        /// <summary>Lệnh trợ giúp đToolIuét hậu tố: của chính tool (<paramref name="suffix"/> rỗng) hoặc của một lệnh con.</summary>
        public string HelpCommand(string suffix)
        {
            if (string.IsNullOrEmpty(Command))
                return null;
            var template = string.IsNullOrWhiteSpace(HelpTemplate) ? "{base} {tokens} --help" : HelpTemplate.Trim();
            return Regex.Replace(template.Replace("{base}", Command).Replace("{tokens}", suffix ?? ""), @"\s{2,}", " ").Trim();
        }

        /// <summary>Mẫu lệnh trợ giúp của tool (trống = mặc định <c>{base} {tokens} --help</c>).</summary>
        public string HelpTemplate { get; }

        /// <summary>
        /// View trong bộ nhớ của một tool thư viện: kind = Id, lệnh gốc = <see cref="ToolEntry.BaseCommand"/>,
        /// hậu tố từ <see cref="ToolEntry.SuffixSource"/>; không tự nhận diện (nhận diện là việc của <see cref="ToolPresets"/>).
        /// </summary>
        public static ToolDef From(ToolEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            var command = entry.BaseCommand;
            return new ToolDef(
                entry.Id,
                string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name,
                entry.Description ?? "",
                null,
                ActionsFromDef.From(entry.SuffixSource, command, entry.Id),
                command,
                entry.HelpTemplate);
        }
    }

    /// <summary>
    /// Tool dùng được trong workspace: mọi tool trong thư viện (<c>tools.json</c>), kind là <see cref="ToolEntry.Id"/>.
    /// Mỗi lần gọi đọc qua <see cref="Source"/>; mặc định đọc <see cref="ToolStore.DefaultPath"/>, giữ bản đọc được theo
    /// dấu thời gian và độ dài file. File lỗi: giữ danh sách đọc được lần trước và ghi lỗi vào <see cref="LastError"/>.
    /// Preset trong <c>catalog.json</c> (nhận diện thư mục, mẫu tool) nằm ở <see cref="ToolPresets"/>.
    /// </summary>
    public static class ToolCatalog
    {
        /// <summary>Tên tool dựng sẵn mà test dùng làm mẫu. Không có gì khác trong code gắn với tool cụ thể.</summary>
        public const string DotNet = "dotnet";
        public const string Git = "git";

        /// <summary>Tên action, khoá, tên file... hợp lệ để ghép vào dòng lệnh.</summary>
        private static readonly Regex SafeKey = new Regex(@"^[A-Za-z0-9][A-Za-z0-9:_.\-@]*$", RegexOptions.Compiled);

        /// <summary>Giới hạn thời gian của regex do nguồn khai báo, để một pattern xấu không treo app.</summary>
        internal static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private static readonly object Gate = new object();
        private static Func<IReadOnlyList<ToolEntry>> _source;
        private static IReadOnlyList<ToolEntry> _entries;
        private static string _path;
        private static DateTime _stamp;
        private static long _length = -1;

        /// <summary>Lỗi đọc thư viện tool lần gần nhất; null nếu ổn.</summary>
        public static string LastError { get; private set; }

        /// <summary>
        /// Nơi lấy danh sách tool. Mặc định đọc thư viện trên đĩa; test gán một hàm trả danh sách giả.
        /// Gán null để quay về mặc định.
        /// </summary>
        public static Func<IReadOnlyList<ToolEntry>> Source
        {
            get => _source ?? ReadLibrary;
            set => _source = value;
        }

        /// <summary>Mọi tool trong thư viện (bỏ mục không có Id); tool nào cũng có thể có hậu tố.</summary>
        public static IReadOnlyList<ToolDef> All =>
            (Source() ?? new ToolEntry[0])
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id))
                .Select(ToolDef.From)
                .ToList();

        public static ToolDef Find(string kind)
        {
            var entry = FindEntry(kind);
            return entry == null ? null : ToolDef.From(entry);
        }

        /// <summary>Tool trong thư viện theo Id (kind); null nếu không có.</summary>
        public static ToolEntry FindEntry(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return null;
            return (Source() ?? new ToolEntry[0])
                .FirstOrDefault(e => e != null && string.Equals(e.Id, kind, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Action gốc của tool: chạy lệnh gốc không hậu tố (Id rỗng, biến thể Id rỗng), chỗ gõ thêm là <c>{args}</c>.
        /// Mọi tool đều có action này, nên tool chưa có bảng khả năng vẫn chạy và tạo profile được.
        /// </summary>
        public static ActionDef BaseAction(ToolDef tool, string defaultArgs) =>
            new ActionDef("", tool.Name, new VariantDef("", tool.Name, tool.Command + " {args}",
                new ParamDef("args", "Tham số", defaultArgs ?? "")));

        /// <summary>
        /// Các action của một binding. Mỗi action là một hậu tố của lệnh gốc, nhãn là chính chữ đó. Nguồn: script đọc từ file
        /// của binding (npm), hoặc lệnh con trong bảng khả năng của tool (quét hay nhập tay). Chưa có bảng thì chưa có action.
        /// </summary>
        public static IReadOnlyList<ActionDef> ActionsFor(Workspace workspace, ToolBinding binding)
        {
            var entry = FindEntry(binding.ToolId);
            if (entry == null)
                return new ActionDef[0];
            var tool = ToolDef.From(entry);

            var actions = new List<ActionDef>();
            if (!string.IsNullOrEmpty(tool.Command))
                actions.Add(BaseAction(tool, null));

            if (tool.ActionsFrom != null)
            {
                var from = tool.ActionsFrom;
                actions.AddRange(ReadSource(CommandBuilder.ToolCwd(workspace, binding, entry), from.Source)
                    .Select(name => new ActionDef(name, name, new VariantDef(from.VariantId, name, from.Template.Replace("{id}", name)))));
                return actions;
            }
            if (string.IsNullOrEmpty(tool.Command))
                return actions;

            var capabilities = LoadCapabilities(tool.Kind);
            if (capabilities != null)
                actions.AddRange(CapabilityStore.Subcommands(capabilities).Select(sub => FromSubcommand(tool, capabilities, sub.Name)));
            return actions;
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

        internal static bool IsSafe(string name) => name != null && SafeKey.IsMatch(name);

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

        /// <summary>Nguồn mặc định: thư viện trên đĩa. File chưa có thì rỗng; file lỗi thì giữ bản đọc được lần trước.</summary>
        private static IReadOnlyList<ToolEntry> ReadLibrary()
        {
            lock (Gate)
            {
                var path = ToolStore.DefaultPath;
                try
                {
                    if (!File.Exists(path))
                    {
                        _entries = new ToolEntry[0];
                        _path = path;
                        _length = -1;
                        LastError = null;
                        return _entries;
                    }

                    var info = new FileInfo(path);
                    if (_entries != null && path == _path && info.LastWriteTimeUtc == _stamp && info.Length == _length)
                        return _entries;
                    // Ghi nhớ trước khi đọc: file lỗi thì không đọc lại liên tục cho tới khi nó đổi.
                    _path = path;
                    _stamp = info.LastWriteTimeUtc;
                    _length = info.Length;
                    _entries = ToolStore.Peek(path).Tools.ToList();   // chỉ đọc: không lấy khoá tools, vì có thể đang chạy trong khoá workspaces
                    LastError = null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    LastError = $"Không đọc được {path}: {ex.Message}\nTạm dùng danh sách tool đọc được trước đó.";
                    if (_entries == null)
                        _entries = new ToolEntry[0];
                }
                return _entries;
            }
        }
    }
}
