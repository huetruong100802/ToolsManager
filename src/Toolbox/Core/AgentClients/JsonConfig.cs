using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Toolbox.Core.AgentClients
{
    /// <summary>
    /// Đọc và ghi file cấu hình JSON của app khác (Cursor, VS Code, Claude Desktop…) mà không làm mất các mục khác trong file.
    /// Đọc được JSONC (chú thích, dấu phẩy thừa) như VS Code cho phép; nhưng file có chú thích thì không ghi lại, vì ghi sẽ xoá chú thích.
    /// </summary>
    internal static class JsonConfig
    {
        public static Dictionary<string, object> Load(string path, out bool hadComments)
        {
            hadComments = false;
            if (!File.Exists(path))
                return new Dictionary<string, object>();
            var text = File.ReadAllText(path);
            if (text.Trim().Length == 0)
                return new Dictionary<string, object>();

            var clean = StripJsonc(text, out hadComments);
            object parsed;
            try
            {
                parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(clean);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new InvalidDataException($"Không đọc được {path}: {ex.Message}", ex);
            }
            return parsed as Dictionary<string, object>
                ?? throw new InvalidDataException($"{path} không phải một đối tượng JSON.");
        }

        /// <summary>Ghi nguyên tử (file tạm rồi thay), giữ bản cũ ở <c>&lt;file&gt;.toolbox.bak</c>.</summary>
        public static void Save(string path, Dictionary<string, object> root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".toolbox.tmp";
            File.WriteAllText(temp, Write(root) + Environment.NewLine, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, path + ".toolbox.bak");
            else
                File.Move(temp, path);
        }

        /// <summary>Bỏ chú thích <c>//</c>, <c>/* */</c> và dấu phẩy thừa trước <c>}</c>/<c>]</c>, giữ nguyên nội dung chuỗi.</summary>
        public static string StripJsonc(string text, out bool hadComments)
        {
            hadComments = false;
            var noComments = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    i = CopyString(text, i, noComments);
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    hadComments = true;
                    while (i < text.Length && text[i] != '\n')
                        i++;
                    noComments.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    hadComments = true;
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 1;
                    noComments.Append(' ');
                    continue;
                }
                noComments.Append(c);
            }

            var source = noComments.ToString();
            var result = new StringBuilder(source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '"')
                {
                    i = CopyString(source, i, result);
                    continue;
                }
                if (c == ',')
                {
                    var next = i + 1;
                    while (next < source.Length && char.IsWhiteSpace(source[next]))
                        next++;
                    if (next < source.Length && (source[next] == '}' || source[next] == ']'))
                        continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>Chép một chuỗi JSON bắt đầu ở <paramref name="start"/>; trả về vị trí dấu nháy đóng.</summary>
        private static int CopyString(string text, int start, StringBuilder into)
        {
            into.Append('"');
            var i = start + 1;
            for (; i < text.Length; i++)
            {
                into.Append(text[i]);
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    into.Append(text[++i]);
                    continue;
                }
                if (text[i] == '"')
                    break;
            }
            return i;
        }

        /// <summary>JSON thụt lề 2 dấu cách; mảng chuỗi ngắn (như <c>args</c>) viết trên một dòng.</summary>
        public static string Write(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, 0);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case IDictionary<string, object> map:
                    WriteObject(sb, map, depth);
                    return;
                case IEnumerable items:
                    WriteArray(sb, items.Cast<object>().ToList(), depth);
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case IFormattable number:
                    sb.Append(number.ToString(null, CultureInfo.InvariantCulture));
                    return;
                default:
                    WriteString(sb, value.ToString());
                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, int depth)
        {
            if (map.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append("{\n");
            var first = true;
            foreach (var pair in map)
            {
                if (!first)
                    sb.Append(",\n");
                first = false;
                Indent(sb, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteValue(sb, pair.Value, depth + 1);
            }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, List<object> items, int depth)
        {
            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            var inline = items.All(i => i == null || i is string || i is bool || i is IFormattable)
                && items.Sum(i => (i?.ToString().Length ?? 4) + 4) < 100;
            if (inline)
            {
                sb.Append('[');
                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    WriteValue(sb, items[i], depth);
                }
                sb.Append(']');
                return;
            }
            sb.Append("[\n");
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    sb.Append(",\n");
                Indent(sb, depth + 1);
                WriteValue(sb, items[i], depth + 1);
            }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder();
            WriteString(sb, s);
            return sb.ToString();
        }
    }
}
