using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Luật về cổng: khoảng hợp lệ, trùng cổng, gợi ý cổng trống.</summary>
    public static class PortRules
    {
        public const int Min = 1024;
        public const int Max = 65535;

        /// <summary>null nếu hợp lệ, ngược lại là thông báo cho người dùng.</summary>
        public static string Check(string text)
        {
            if (!TryParse(text, out _))
                return $"Cổng phải là số từ {Min} đến {Max}.";
            return null;
        }

        public static bool TryParse(string text, out int port)
        {
            port = 0;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || !text.All(c => c >= '0' && c <= '9') || text.Length > 5)
                return false;
            port = int.Parse(text, CultureInfo.InvariantCulture);
            return port >= Min && port <= Max;
        }

        /// <summary>Thông báo nếu cổng đã có tác vụ dùng; <paramref name="used"/> là cổng → tên tác vụ.</summary>
        public static string Conflict(int port, IReadOnlyDictionary<int, string> used) =>
            used != null && used.TryGetValue(port, out var who) ? $"Cổng {port} đang được \"{who}\" dùng." : null;

        /// <summary>Cổng trống đầu tiên từ <paramref name="from"/> trở đi; -1 nếu hết.</summary>
        public static int NextFree(int from, IReadOnlyDictionary<int, string> used)
        {
            for (var p = Math.Max(from, Min); p <= Max; p++)
                if (used == null || !used.ContainsKey(p))
                    return p;
            return -1;
        }
    }

    /// <summary>Ghép template của biến thể với giá trị tham số thành dòng lệnh.</summary>
    public static class CommandBuilder
    {
        private const string Dangerous = "&|<>^\"%\r\n";
        private const string DangerousInQuotes = "\"%^\r\n";

        /// <summary>Giá trị đầy đủ: ô thiếu hoặc rỗng lấy mặc định của tham số.</summary>
        public static Dictionary<string, string> Effective(VariantDef variant, IDictionary<string, string> vals)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in variant.Params)
            {
                string v = null;
                if (vals != null && vals.TryGetValue(p.Key, out v))
                    v = v?.Trim();
                result[p.Key] = string.IsNullOrEmpty(v) ? p.Default : v;
            }
            return result;
        }

        /// <summary>Lỗi theo từng tham số (key → thông báo); rỗng nếu hợp lệ. Có <paramref name="usedPorts"/> thì kiểm cả trùng cổng.</summary>
        public static Dictionary<string, string> Validate(VariantDef variant, IDictionary<string, string> vals, IReadOnlyDictionary<int, string> usedPorts = null)
        {
            var errors = new Dictionary<string, string>();
            var values = Effective(variant, vals);
            foreach (var p in variant.Params)
            {
                var value = values[p.Key];
                if (p.IsPort)
                {
                    var message = PortRules.Check(value);
                    if (message == null && PortRules.TryParse(value, out var port))
                        message = PortRules.Conflict(port, usedPorts);
                    if (message != null)
                        errors[p.Key] = message;
                }
                else if (value.Any(c => (IsQuoted(variant.Template, p.Key) ? DangerousInQuotes : Dangerous).IndexOf(c) >= 0))
                {
                    errors[p.Key] = IsQuoted(variant.Template, p.Key)
                        ? "Không dùng các ký tự \" % ^ hoặc xuống dòng."
                        : "Không dùng các ký tự & | < > ^ \" % hoặc xuống dòng.";
                }
            }
            return errors;
        }

        /// <summary>Dòng lệnh cuối cùng. Ném <see cref="ArgumentException"/> nếu tham số không hợp lệ (trừ trùng cổng).</summary>
        public static string Build(VariantDef variant, IDictionary<string, string> vals = null)
        {
            var errors = Validate(variant, vals);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors.Values));

            var line = variant.Template;
            foreach (var pair in Effective(variant, vals))
                line = line.Replace("{" + pair.Key + "}", pair.Value);
            return line;
        }

        /// <summary>Cổng mà lệnh sẽ dùng (tham số cổng đầu tiên); null nếu lệnh không có tham số cổng hoặc cổng sai.</summary>
        public static int? PortOf(VariantDef variant, IDictionary<string, string> vals)
        {
            var p = variant.Params.FirstOrDefault(x => x.IsPort);
            if (p == null)
                return null;
            return PortRules.TryParse(Effective(variant, vals)[p.Key], out var port) ? port : (int?)null;
        }

        private static bool IsQuoted(string template, string key) => template.Contains("\"{" + key + "}\"");
    }
}
