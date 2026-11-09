using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Toolbox.Core;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Preset tool đọc từ <c>catalog.json</c> trong thư mục dữ liệu (lần đầu chép từ mặc định nhúng sẵn). Preset không phải
    /// một loại tool riêng; chỉ dùng để nhận diện thư mục (<see cref="ToolDef.Detect"/>), sinh <see cref="ToolEntry"/> mẫu
    /// khi người dùng chọn gợi ý (<see cref="ToEntry"/>) và cho migration. File đổi thì đọc lại ở lần gọi sau.
    /// File lỗi: giữ danh sách đọc được lần trước (hoặc mặc định nếu chưa có) và ghi lỗi vào <see cref="LastError"/>.
    /// </summary>
    public static class ToolPresets
    {
        public const string FileName = "catalog.json";
        private const string EmbeddedName = "catalog.default.json";

        private static readonly object Gate = new object();
        private static IReadOnlyList<ToolDef> _tools;
        private static string _path;
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
        /// Tool thư viện mẫu sinh từ preset: Id = kind, lệnh gốc và nguồn hậu tố của preset, Path rỗng (chạy tại thư mục
        /// workspace), không đóng gói file.
        /// </summary>
        public static ToolEntry ToEntry(ToolDef preset)
        {
            if (preset == null)
                throw new ArgumentNullException(nameof(preset));
            return new ToolEntry
            {
                Id = preset.Kind,
                Name = preset.Name,
                Path = "",
                Command = preset.Command,
                Pack = PackMode.None,
                SuffixSource = ToSuffixSource(preset.ActionsFrom),
            };
        }

        /// <summary>
        /// Đảm bảo thư viện có tool cho mỗi kind preset (tool nhận diện từ thư mục dự án). Kind không phải preset thì bỏ qua.
        /// Trả về true nếu đã thêm; không ghi file, người gọi lưu thư viện.
        /// </summary>
        public static bool EnsureInLibrary(ToolStore library, IEnumerable<string> kinds)
        {
            var added = false;
            foreach (var kind in kinds.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (library.Find(kind) != null)
                    continue;
                var preset = Find(kind);
                if (preset == null)
                    continue;
                library.Add(ToEntry(preset));
                added = true;
            }
            return added;
        }

        /// <summary>Đọc preset từ JSON. Ném <see cref="InvalidDataException"/> nếu cấu trúc sai.</summary>
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
                if (!ToolCatalog.IsSafe(kind))
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

        /// <summary>
        /// Catalog cũ (schemaVersion thấp hơn bản mặc định) theo mô hình đã bỏ. Sao lưu thành <c>catalog.json.v{n}.bak</c>
        /// rồi ghi lại bản mặc định; trả về văn bản mới, hoặc đúng chuỗi cũ nếu không cần.
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

        /// <summary>Nội dung mặc định nhúng sẵn của <c>catalog.json</c>.</summary>
        internal static string DefaultJson()
        {
            using (var stream = typeof(ToolPresets).GetTypeInfo().Assembly.GetManifestResourceStream(EmbeddedName))
            {
                if (stream == null)
                    throw new InvalidDataException("Thiếu tài nguyên mặc định của danh mục tool.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static SuffixSource ToSuffixSource(ActionsFromDef from)
        {
            if (from == null)
                return null;
            var source = from.Source;
            return new SuffixSource
            {
                File = source.File,
                JsonPath = source.Kind == SourceKind.Json ? source.JsonPath : null,
                Pattern = source.Kind == SourceKind.Pattern ? source.Pattern : null,
                Values = source.Kind == SourceKind.Values ? source.Items.ToList() : null,
                Template = from.Template,
            };
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
                if (_tools != null && path == _path && info.LastWriteTimeUtc == _stamp && info.Length == _length)
                    return;
                // Ghi nhớ trước khi đọc: file lỗi thì không đọc lại liên tục cho tới khi nó đổi.
                _path = path;
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
            var values = (Get(source, "values") as object[])?.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture));
            return ValueSource.Create(Text(source, "file"), Text(source, "jsonPath"), Text(source, "pattern"), values, kind);
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
