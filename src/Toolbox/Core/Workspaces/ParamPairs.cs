using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Một tham số của lệnh: cặp (key, value) theo thứ tự. Key trống + value = đối số vị trí; key + value rỗng = công tắc (flag);
    /// cả hai = option. Key lặp được (<c>--file A --file B</c>). Key <c>--</c> là dấu tách: các cặp sau nó thuộc về app đang chạy.
    /// Key kết thúc bằng <c>=</c> (<c>--port=</c>) nối thẳng value, không có dấu cách. <see cref="Ask"/>: hỏi giá trị lúc chạy.
    /// </summary>
    [DataContract]
    public sealed class ParamPair
    {
        public ParamPair() { }

        public ParamPair(string key, string value = "", bool ask = false)
        {
            Key = key ?? "";
            Value = value ?? "";
            Ask = ask;
        }

        [DataMember(Name = "key", Order = 0, EmitDefaultValue = false)]
        public string Key { get; set; }

        [DataMember(Name = "value", Order = 1, EmitDefaultValue = false)]
        public string Value { get; set; }

        /// <summary>True: không lưu giá trị, hỏi người chạy mỗi lần.</summary>
        [DataMember(Name = "ask", Order = 2, EmitDefaultValue = false)]
        public bool Ask { get; set; }

        public ParamPair Clone() => (ParamPair)MemberwiseClone();
    }

    /// <summary>Dựng, kiểm tra và tách dòng tham số từ danh sách <see cref="ParamPair"/>. Không biết tên tool nào.</summary>
    public static class PairLine
    {
        /// <summary>Key hợp lệ: trống (đối số vị trí), <c>--</c>, hoặc <c>-x</c>/<c>--ten</c> (có thể kết thúc bằng <c>=</c>).</summary>
        private static readonly Regex KeyName = new Regex(@"^(--|--?[A-Za-z0-9][A-Za-z0-9_.:-]*=?)$", RegexOptions.Compiled);

        /// <summary>Ký tự không được có trong value: đóng dấu nháy sớm, mở rộng biến, ký tự thoát và xuống dòng của cmd.</summary>
        private const string BadInValue = "\"%^\r\n";

        public static bool IsSeparator(string key) => (key ?? "").Trim() == "--";

        /// <summary>Lỗi của danh sách cặp (key sai hoặc value có ký tự cấm); null nếu hợp lệ.</summary>
        public static string Check(IEnumerable<ParamPair> pairs)
        {
            foreach (var p in pairs ?? Enumerable.Empty<ParamPair>())
            {
                var key = (p.Key ?? "").Trim();
                if (key.Length > 0 && !KeyName.IsMatch(key))
                    return $"Tên tham số \"{key}\" không hợp lệ; chỉ dạng -x hoặc --ten (chữ, số, - _ . :).";
                if ((p.Value ?? "").Any(c => BadInValue.IndexOf(c) >= 0))
                    return $"Giá trị của \"{(key.Length == 0 ? "đối số" : key)}\" không được dùng các ký tự \" % ^ hoặc xuống dòng.";
            }
            return null;
        }

        /// <summary>Các cặp hỏi lúc chạy mà chưa có giá trị, theo thứ tự xuất hiện.</summary>
        public static IReadOnlyList<ParamPair> Pending(IEnumerable<ParamPair> pairs) =>
            (pairs ?? Enumerable.Empty<ParamPair>()).Where(p => p.Ask && string.IsNullOrEmpty((p.Value ?? "").Trim())).ToList();

        /// <summary>Bản sao với các cặp đang chờ được điền lần lượt từ <paramref name="answers"/> (và hết chờ); thiếu câu trả lời thì giữ nguyên.</summary>
        public static List<ParamPair> Resolve(IEnumerable<ParamPair> pairs, IList<string> answers)
        {
            var result = new List<ParamPair>();
            var next = 0;
            foreach (var p in pairs ?? Enumerable.Empty<ParamPair>())
            {
                var copy = p.Clone();
                if (copy.Ask && string.IsNullOrEmpty((copy.Value ?? "").Trim()) && answers != null && next < answers.Count)
                {
                    copy.Value = (answers[next++] ?? "").Trim();
                    copy.Ask = false;
                }
                result.Add(copy);
            }
            return result;
        }

        /// <summary>Chuỗi tham số theo thứ tự. Cặp hỏi lúc chạy mà chưa có giá trị bị bỏ qua; value có khoảng trắng hoặc &amp; | &lt; &gt; được bọc dấu nháy.</summary>
        public static string Render(IEnumerable<ParamPair> pairs)
        {
            var parts = new List<string>();
            foreach (var p in pairs ?? Enumerable.Empty<ParamPair>())
            {
                var key = (p.Key ?? "").Trim();
                var value = (p.Value ?? "").Trim();
                if (p.Ask && value.Length == 0)
                    continue;
                if (key.Length == 0 && value.Length == 0)
                    continue;
                if (key.Length == 0)
                    parts.Add(Quote(value));
                else if (value.Length == 0)
                    parts.Add(key);
                else if (key.EndsWith("=", StringComparison.Ordinal))
                    parts.Add(key + Quote(value));
                else
                    parts.Add(key + " " + Quote(value));
            }
            return string.Join(" ", parts);
        }

        private static string Quote(string value) =>
            value.IndexOfAny(new[] { ' ', '\t', '&', '|', '<', '>' }) >= 0 ? "\"" + value + "\"" : value;

        /// <summary>
        /// Tách một dòng tham số thành cặp. Token bắt đầu bằng <c>-</c> là key; token ngay sau nó là value nếu
        /// <paramref name="takesValue"/>(key) trả true (không biết = công tắc). <c>--key=v</c> thành key <c>--key=</c>. Dấu nháy được bỏ.
        /// </summary>
        public static List<ParamPair> Parse(string text, Func<string, bool> takesValue = null)
        {
            var tokens = Tokenize(text);
            var result = new List<ParamPair>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Quoted || !token.Text.StartsWith("-", StringComparison.Ordinal) || token.Text == "-")
                {
                    result.Add(new ParamPair("", token.Text));
                    continue;
                }
                var eq = token.Text.IndexOf('=');
                if (eq > 0 && token.Text.StartsWith("--", StringComparison.Ordinal))
                {
                    result.Add(new ParamPair(token.Text.Substring(0, eq + 1), token.Text.Substring(eq + 1)));
                    continue;
                }
                if (token.Text != "--" && takesValue != null && takesValue(token.Text) && i + 1 < tokens.Count
                    && (tokens[i + 1].Quoted || !tokens[i + 1].Text.StartsWith("-", StringComparison.Ordinal)))
                {
                    result.Add(new ParamPair(token.Text, tokens[i + 1].Text));
                    i++;
                    continue;
                }
                result.Add(new ParamPair(token.Text));
            }
            return result;
        }

        private struct Token
        {
            public string Text;
            public bool Quoted;
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var sb = new StringBuilder();
            var inQuote = false;
            var wasQuoted = false;
            Action flush = () =>
            {
                if (sb.Length > 0 || wasQuoted)
                    tokens.Add(new Token { Text = sb.ToString(), Quoted = wasQuoted });
                sb.Clear();
                wasQuoted = false;
            };
            foreach (var c in text ?? "")
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    wasQuoted = true;
                }
                else if (!inQuote && char.IsWhiteSpace(c))
                    flush();
                else
                    sb.Append(c);
            }
            flush();
            return tokens;
        }
    }
}
