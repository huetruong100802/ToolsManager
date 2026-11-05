using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Toolbox.Core;

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
            {
                // Tham số để trống (vd project Angular không chọn): bỏ luôn dấu cách đứng trước để không để lại "  ".
                line = string.IsNullOrEmpty(pair.Value)
                    ? line.Replace(" {" + pair.Key + "}", "").Replace("{" + pair.Key + "}", "")
                    : line.Replace("{" + pair.Key + "}", pair.Value);
            }
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

        // ----- Profile loại library / custom -----

        /// <summary>Lỗi của tham số thêm (Vals["args"]) vì nó được nối thẳng vào dòng lệnh; null nếu hợp lệ hoặc rỗng.</summary>
        public static string CheckExtraArgs(string args)
        {
            if (!string.IsNullOrEmpty(args) && args.Any(c => Dangerous.IndexOf(c) >= 0))
                return "Tham số thêm không được dùng các ký tự & | < > ^ \" % hoặc xuống dòng.";
            return null;
        }

        /// <summary>Nối tham số thêm vào sau <paramref name="baseLine"/>.</summary>
        public static string JoinArgs(string baseLine, string extra) =>
            string.IsNullOrWhiteSpace(extra) ? (baseLine ?? "").Trim() : ((baseLine ?? "") + " " + extra.Trim()).Trim();

        /// <summary>Dòng lệnh của lệnh riêng kèm tham số thêm của profile (nếu có).</summary>
        public static string CustomLine(CustomCommand command, string extraArgs) => JoinArgs(command.CommandLine, extraArgs);

        /// <summary>
        /// Dòng lệnh chạy một tool thư viện (cho tác vụ chạy ngầm/bộ chạy): lệnh thì "lệnh tham số", file thì theo đuôi
        /// (.exe/.com chạy thẳng, .bat/.cmd, .ps1, .py). <paramref name="effectiveArgs"/> là tham số hiệu lực của tool, đã gồm phần nối thêm.
        /// Ném <see cref="NotSupportedException"/> nếu loại file không chạy ngầm được.
        /// </summary>
        public static string LibraryCommandLine(ToolEntry tool, string effectiveArgs)
        {
            var args = (effectiveArgs ?? "").Trim();
            if (tool.IsCommand)
                return JoinArgs(tool.Command, args);

            var path = System.IO.Path.GetFullPath(tool.ResolvedPath);
            if (System.IO.Directory.Exists(path))
                throw new NotSupportedException($"\"{tool.Name}\" là thư mục, không chạy được.");
            var quoted = "\"" + path + "\"";
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".exe":
                case ".com":
                case ".bat":
                case ".cmd":
                    return JoinArgs(quoted, args);
                case ".ps1":
                    return JoinArgs("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + quoted, args);
                case ".py":
                    return JoinArgs("python.exe " + quoted, args);
                default:
                    throw new NotSupportedException($"Không chạy ngầm được file {System.IO.Path.GetExtension(path)} của \"{tool.Name}\"; chỉ hỗ trợ .exe, .com, .bat, .cmd, .ps1, .py hoặc lệnh.");
            }
        }

        /// <summary>Thư mục chạy của tool thư viện: thư mục đăng ký nếu là lệnh, ngược lại thư mục chứa file.</summary>
        public static string LibraryCwd(ToolEntry tool)
        {
            var path = System.IO.Path.GetFullPath(tool.ResolvedPath);
            return tool.IsCommand ? path : System.IO.Path.GetDirectoryName(path);
        }
    }
}
