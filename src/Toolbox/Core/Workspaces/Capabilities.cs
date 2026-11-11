using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Toolbox.Core;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Một cờ (hoặc tuỳ chọn) của một action. <see cref="Source"/> là "scan" (đọc từ trợ giúp của tool) hoặc
    /// "manual" (bạn nhập tay). Mọi thông tin về cờ đều đến từ tool hoặc từ bạn, không đoán trước trong code.
    /// </summary>
    public sealed class CapabilityEntry
    {
        public string Action { get; set; }
        public string Name { get; set; }
        /// <summary>Cờ có nhận giá trị (--port 4200) hay chỉ là công tắc (--open). Đoán từ trợ giúp, bạn sửa được.</summary>
        public bool TakesValue { get; set; }
        public string Description { get; set; }
        /// <summary>Dòng gốc trong trợ giúp, để đối chiếu.</summary>
        public string Excerpt { get; set; }
        public string Source { get; set; }
        /// <summary>Vai trò do bạn gán: "port" hoặc rỗng. Mỗi action chỉ có một mục giữ vai trò này.</summary>
        public string Role { get; set; }
        /// <summary>Các giá trị hợp lệ đọc được từ trợ giúp (vd. q, m, n, d, diag); null nếu help không liệt kê.</summary>
        public List<string> Choices { get; set; }
    }

    /// <summary>
    /// Một đối số vị trí của một phạm vi (mục "Arguments:" trong help): tên, mô tả, giá trị cho phép nếu help liệt kê,
    /// và cờ cho biết nó là đường dẫn (gợi ý file trong thư mục chạy). Đọc từ help, không đoán theo tên tool.
    /// </summary>
    public sealed class ArgHint
    {
        public string Action { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Choices { get; set; }
        public bool IsPath { get; set; }
        /// <summary>Chỉ nhận thư mục (tên chứa dir/folder), nên gợi ý không liệt kê file.</summary>
        public bool DirectoryOnly { get; set; }
        /// <summary>Nhận nhiều giá trị (<c>&lt;FILE&gt;...</c>).</summary>
        public bool Variadic { get; set; }
    }

    /// <summary>Lần quét gần nhất của một action: lệnh đã chạy, thời điểm, dấu vân tay output, lỗi nếu có.</summary>
    public sealed class ScanRecord
    {
        public string Action { get; set; }
        public string Command { get; set; }
        /// <summary>Thời điểm quét, định dạng "o" (round-trip).</summary>
        public string ScannedAt { get; set; }
        public string Fingerprint { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Bảng khả năng của một tool, lưu trong <c>capabilities\&lt;kind&gt;.json</c>.</summary>
    public sealed class ToolCapabilities
    {
        public string Kind { get; set; }
        public List<ScanRecord> Scans { get; set; } = new List<ScanRecord>();
        public List<CapabilityEntry> Entries { get; set; } = new List<CapabilityEntry>();
        /// <summary>Đối số vị trí của từng phạm vi, đọc từ mục "Arguments:" của help.</summary>
        public List<ArgHint> Args { get; set; } = new List<ArgHint>();
    }

    /// <summary>Kết quả một lần quét: các mục đọc được, dấu vân tay output, lỗi nếu có.</summary>
    public sealed class ScanOutcome
    {
        public string Command { get; set; }
        public string Fingerprint { get; set; }
        public List<CapabilityEntry> Entries { get; set; } = new List<CapabilityEntry>();
        public List<ArgHint> Args { get; set; } = new List<ArgHint>();
        public string Error { get; set; }
    }

    /// <summary>Đọc, ghi và cập nhật bảng khả năng. Mọi thao tác nhận thư mục làm tham số để test không đụng dữ liệu thật.</summary>
    public static class CapabilityStore
    {
        /// <summary>Vai trò của cờ cổng; token <c>{portFlag}</c> trong mẫu lệnh được thay bằng cờ có vai trò này.</summary>
        public const string PortRole = "port";

        /// <summary>Tên cờ hợp lệ để ghép vào dòng lệnh: <c>-x</c> hoặc <c>--ten-co</c>.</summary>
        public static readonly Regex FlagName = new Regex(@"^--?[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);

        /// <summary>Tên lệnh con hợp lệ: <c>serve</c>, <c>add-package</c>, <c>run:dev</c>. Không bắt đầu bằng dấu gạch.</summary>
        public static readonly Regex SubcommandName = new Regex(@"^[A-Za-z][A-Za-z0-9_.:@-]*(?: [A-Za-z][A-Za-z0-9_.:@-]*)*$", RegexOptions.Compiled);

        /// <summary>Hậu tố hợp lệ: cờ hoặc lệnh con. Cả hai chỉ là chữ nối vào sau lệnh gốc.</summary>
        public static bool IsSuffix(string name) => name != null && (FlagName.IsMatch(name) || SubcommandName.IsMatch(name));

        public static bool IsFlag(string name) => name != null && FlagName.IsMatch(name);

        /// <summary>Lệnh con của tool: các mục ở phạm vi của chính tool (<c>Action</c> rỗng) không phải cờ, theo thứ tự đã quét.</summary>
        public static IReadOnlyList<CapabilityEntry> Subcommands(ToolCapabilities data) =>
            data.Entries.Where(e => string.IsNullOrEmpty(e.Action) && !IsFlag(e.Name) && SubcommandName.IsMatch(e.Name ?? ""))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private static readonly object Gate = new object();

        public static string DefaultDirectory => Path.Combine(DataDir.Root, "capabilities");

        public static string PathFor(string directory, string kind) => Path.Combine(directory, kind + ".json");

        /// <summary>Bảng của tool; chưa có file thì trả bảng rỗng. File hỏng thì ném <see cref="InvalidDataException"/> để không ghi đè dữ liệu của bạn.</summary>
        public static ToolCapabilities Load(string directory, string kind)
        {
            var path = PathFor(directory, kind);
            if (!File.Exists(path))
                return new ToolCapabilities { Kind = kind };
            try
            {
                var data = new JavaScriptSerializer().Deserialize<ToolCapabilities>(File.ReadAllText(path, Encoding.UTF8)) ?? new ToolCapabilities();
                data.Kind = kind;
                data.Scans = (data.Scans ?? new List<ScanRecord>()).Where(s => s != null).ToList();
                data.Entries = (data.Entries ?? new List<CapabilityEntry>()).Where(e => e != null && IsSuffix(e.Name)).ToList();
                data.Args = (data.Args ?? new List<ArgHint>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name)).ToList();
                return data;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException)
            {
                throw new InvalidDataException($"Không đọc được {path}: {ex.Message}", ex);
            }
        }

        public static void Save(string directory, ToolCapabilities data)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(PathFor(directory, data.Kind), new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// Áp kết quả quét của một action: thay các mục quét cũ của action đó, giữ mục nhập tay và vai trò còn khớp tên cờ.
        /// Quét lỗi thì giữ nguyên mục cũ, chỉ ghi lỗi vào lần quét.
        /// </summary>
        public static void ApplyScan(ToolCapabilities data, string action, ScanOutcome outcome, DateTime now)
        {
            data.Scans.RemoveAll(s => SameAction(s.Action, action));
            data.Scans.Add(new ScanRecord
            {
                Action = action,
                Command = outcome.Command,
                ScannedAt = now.ToString("o"),
                Fingerprint = outcome.Fingerprint,
                Error = outcome.Error,
            });
            if (outcome.Error != null)
                return;

            data.Args.RemoveAll(a => SameAction(a.Action, action));
            foreach (var arg in outcome.Args ?? new List<ArgHint>())
            {
                arg.Action = action;
                data.Args.Add(arg);
            }

            var previous = data.Entries.Where(e => SameAction(e.Action, action) && e.Source == "scan").ToList();
            data.Entries.RemoveAll(e => SameAction(e.Action, action) && e.Source == "scan");
            foreach (var fresh in outcome.Entries)
            {
                var old = previous.FirstOrDefault(p => p.Name == fresh.Name);
                fresh.Action = action;
                fresh.Source = "scan";
                fresh.Role = old?.Role ?? "";
                data.Entries.Add(fresh);
            }
        }

        /// <summary>Gán vai trò cho một cờ. Vai trò là duy nhất trong action: gán cho cờ này thì bỏ ở cờ khác. Role rỗng là bỏ vai trò.</summary>
        public static void SetRole(ToolCapabilities data, string action, string name, string role)
        {
            if (!string.IsNullOrEmpty(role))
                foreach (var other in data.Entries.Where(e => SameAction(e.Action, action) && e.Role == role))
                    other.Role = "";
            var target = data.Entries.FirstOrDefault(e => SameAction(e.Action, action) && e.Name == name);
            if (target != null)
                target.Role = role ?? "";
        }

        /// <summary>Thêm mục nhập tay. Tên sai hoặc đã có trong action thì ném <see cref="ArgumentException"/>.</summary>
        public static void AddManual(ToolCapabilities data, string action, string name, bool takesValue, string description)
        {
            name = (name ?? "").Trim();
            if (!IsSuffix(name))
                throw new ArgumentException("Hậu tố phải như serve, --port hoặc -p, chỉ gồm chữ, số, - và _.");
            if (data.Entries.Any(e => SameAction(e.Action, action) && e.Name == name))
                throw new ArgumentException($"{name} đã có trong bảng.");
            // Lệnh con không nhận giá trị: giá trị nằm ở các cờ của nó.
            takesValue = takesValue && IsFlag(name);
            data.Entries.Add(new CapabilityEntry
            {
                Action = action,
                Name = name,
                TakesValue = takesValue,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Excerpt = "",
                Source = "manual",
                Role = "",
            });
        }

        /// <summary>
        /// Thêm mục nhập từ nguồn khác ngoài quét (file script, danh sách dán). Mục có sẵn hoặc tên sai thì bỏ qua; trả true nếu đã thêm.
        /// Mục nhập vào có Source "import": giữ nguyên qua các lần quét lại.
        /// </summary>
        public static bool AddImported(ToolCapabilities data, string action, string name, bool takesValue, string description)
        {
            try
            {
                AddManual(data, action, name, takesValue, description);
            }
            catch (ArgumentException)
            {
                return false;
            }
            data.Entries[data.Entries.Count - 1].Source = "import";
            return true;
        }

        public static void Remove(ToolCapabilities data, string action, string name) =>
            data.Entries.RemoveAll(e => SameAction(e.Action, action) && e.Name == name);

        /// <summary>Cờ có vai trò cổng của action, hoặc null nếu chưa gán.</summary>
        public static CapabilityEntry PortFlag(ToolCapabilities data, string action) =>
            data.Entries.FirstOrDefault(e => SameAction(e.Action, action) && e.Role == PortRole && IsFlag(e.Name));

        public static ScanRecord LastScan(ToolCapabilities data, string action) =>
            data.Scans.LastOrDefault(s => SameAction(s.Action, action));

        private static bool SameAction(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Chạy lệnh trợ giúp của tool và đọc output. Lệnh lấy từ catalog (dữ liệu, không phải code), chạy trong thư mục chạy của
    /// binding, có giới hạn thời gian; không ghép tham số người dùng vào lệnh.
    /// </summary>
    public static class CapabilityScanner
    {
        public const int DefaultTimeoutMs = 30000;

        /// <param name="includeSubcommands">True khi quét trợ giúp của chính tool: đọc luôn danh sách lệnh con. Quét lệnh con thì chỉ lấy cờ.</param>
        public static ScanOutcome Run(string command, string workingDirectory, int timeoutMs = DefaultTimeoutMs, bool includeSubcommands = false)
        {
            var outcome = new ScanOutcome { Command = command };
            if (string.IsNullOrWhiteSpace(command))
            {
                outcome.Error = "Chưa có lệnh trợ giúp để quét.";
                return outcome;
            }
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                outcome.Error = "Thư mục chạy không tồn tại: " + workingDirectory;
                return outcome;
            }

            string text;
            try
            {
                var info = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"")
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using (var process = Process.Start(info))
                {
                    // Đóng stdin để lệnh hỏi xác nhận (như npx cài gói) nhận EOF thay vì treo.
                    process.StandardInput.Close();
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        outcome.Error = $"Quá {timeoutMs / 1000} giây chưa xong. Thử chạy lệnh trợ giúp trong terminal trước (có thể đang cài gói).";
                        return outcome;
                    }
                    process.WaitForExit();
                    text = stdout.Result + "\n" + stderr.Result;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                outcome.Error = "Không chạy được lệnh: " + ex.Message;
                return outcome;
            }

            outcome.Entries = HelpParser.Parse(text);
            if (includeSubcommands)
                outcome.Entries.AddRange(HelpParser.ParseSubcommands(text));
            outcome.Args = HelpParser.ParseArguments(text);
            outcome.Fingerprint = Fingerprint(text);
            if (outcome.Entries.Count == 0 && outcome.Args.Count == 0)
            {
                // Kèm đầu output để biết vì sao: lệnh lỗi (thiếu cài đặt, file cấu hình sai) hay tool không có trợ giúp chuẩn.
                var head = (text ?? "").Trim();
                if (head.Length > 200)
                    head = head.Substring(0, 200) + "…";
                outcome.Error = "Không đọc được hậu tố nào." + (head.Length > 0 ? " Đầu ra: " + head.Replace("\r", " ").Replace("\n", " ") : " Lệnh không in gì.");
            }
            return outcome;
        }

        /// <summary>
        /// Quét lệnh trợ giúp của <paramref name="scope"/> rồi ghi kết quả vào bảng khả năng của <paramref name="kind"/>.
        /// Chạy được trên luồng nền; bảng hỏng thì trả lỗi trong <see cref="ScanOutcome.Error"/> chứ không ghi đè.
        /// </summary>
        public static ScanOutcome ScanAndStore(string directory, string kind, string scope, string command, string workingDirectory, bool includeSubcommands)
        {
            var outcome = Run(command, workingDirectory, DefaultTimeoutMs, includeSubcommands);
            try
            {
                var data = CapabilityStore.Load(directory, kind);
                CapabilityStore.ApplyScan(data, scope ?? "", outcome, DateTime.Now);
                CapabilityStore.Save(directory, data);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                outcome.Error = ex.Message;
            }
            return outcome;
        }

        private static string Fingerprint(string text)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Đọc cờ từ văn bản trợ giúp kiểu GNU/POSIX: dòng bắt đầu bằng thụt đầu dòng rồi <c>-x</c> hoặc <c>--ten</c>.
    /// Có nhận giá trị hay không suy từ phần sau tên cờ (<c>--port number</c>, <c>--port=&lt;n&gt;</c>); đây là suy đoán,
    /// người dùng xem đoạn gốc và sửa được.
    /// </summary>
    public static class HelpParser
    {
        private static readonly Regex FlagLine = new Regex(@"^(\s*)(?:-[A-Za-z]{1,2}(?:,\s*|\|))?(--?[A-Za-z][A-Za-z0-9_-]*)(.*)$", RegexOptions.Compiled);

        /// <summary>Giá trị ngay sau tên: "=x" hoặc một khoảng trắng rồi một từ (không phải hai khoảng trắng, vì sau đó là mô tả).</summary>
        private static readonly Regex ValueAfter = new Regex(@"^(=|\s(?!\s))(<[^>]*>|\[[^\]]*\]|\{[^}]*\}|[A-Za-z0-9][\w.|<>\[\]-]*)(\s|$)", RegexOptions.Compiled);

        public static List<CapabilityEntry> Parse(string text)
        {
            var result = new List<CapabilityEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = (text ?? "").Replace("\r", "").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var m = FlagLine.Match(lines[i]);
                if (!m.Success)
                    continue;
                var indent = m.Groups[1].Value.Length;
                var name = m.Groups[2].Value;
                var rest = m.Groups[3].Value;
                if (rest.Length > 0 && !(rest[0] == ' ' || rest[0] == '=' || rest[0] == ',' || rest[0] == '\t'))
                    continue;
                if (!seen.Add(name))
                    continue;

                var value = ValueAfter.Match(rest);
                var tail = value.Success ? rest.Substring(value.Length) : rest;
                var description = tail.Trim();
                var excerpt = lines[i].TrimEnd();

                // Mô tả có thể nằm ở dòng thụt sâu hơn ngay dưới tên cờ.
                if (description.Length == 0 && i + 1 < lines.Length)
                {
                    var next = lines[i + 1];
                    if (next.Trim().Length > 0 && LeadingSpaces(next) > indent && !FlagLine.IsMatch(next))
                    {
                        description = next.Trim();
                        excerpt += "\n" + next.TrimEnd();
                    }
                }

                if (excerpt.Length > 240)
                    excerpt = excerpt.Substring(0, 240) + "…";
                result.Add(new CapabilityEntry
                {
                    Name = name,
                    TakesValue = value.Success,
                    Description = description.Length == 0 ? null : description,
                    Excerpt = excerpt,
                    Source = "scan",
                    Role = "",
                    Choices = value.Success ? ExtractChoices(value.Groups[2].Value + " " + description) : null,
                });
            }

            ParseUsageFlags(text, result);
            return result;
        }

        /// <summary>Cờ đứng trong dòng "usage: …" (kiểu git, argparse): nằm trong [ ] hoặc sau | nên không đứng đầu dòng.</summary>
        private static readonly Regex UsageHead = new Regex(@"^\s*usage:", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UsageFlag = new Regex(@"(?<=[\[\s|])(--?[A-Za-z][A-Za-z0-9_-]*)", RegexOptions.Compiled);
        /// <summary>Sau tên cờ có giá trị: "=x", "[=x]", "&lt;x&gt;", hoặc một từ viết hoa như PORT.</summary>
        private static readonly Regex UsageValue = new Regex(@"^(=|\[=|\s+\[?<|\s+[A-Z][A-Z0-9_]*(\s|\]|$))", RegexOptions.Compiled);

        /// <summary>
        /// Đọc cờ từ dòng "usage:" và các dòng thụt tiếp theo. Chỉ thêm cờ chưa có ở phần mô tả; không có mô tả riêng nên Description để trống.
        /// Cờ có giá trị được nhận ra từ ký hiệu ngay sau tên (=x, &lt;x&gt;, [=x], PORT). Trùng tên được tính theo đúng chữ hoa/thường
        /// (git có cả -c và -C).
        /// </summary>
        private static void ParseUsageFlags(string text, List<CapabilityEntry> result)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!UsageHead.IsMatch(lines[i]))
                    continue;
                for (var j = i; j < lines.Length; j++)
                {
                    var line = lines[j];
                    if (j > i && (line.Length == 0 || !char.IsWhiteSpace(line[0])))
                        break;
                    foreach (Match m in UsageFlag.Matches(line))
                    {
                        var name = m.Groups[1].Value;
                        if (!CapabilityStore.IsFlag(name) || result.Any(r => r.Name == name))
                            continue;
                        var after = line.Substring(m.Index + m.Length);
                        var excerpt = line.Trim();
                        result.Add(new CapabilityEntry
                        {
                            Name = name,
                            TakesValue = UsageValue.IsMatch(after),
                            Description = null,
                            Excerpt = excerpt.Length > 240 ? excerpt.Substring(0, 240) + "…" : excerpt,
                            Source = "scan",
                            Role = "",
                        });
                    }
                }
                return;
            }
        }

        private static readonly Regex Braces = new Regex(@"\{([^{}]{3,200})\}", RegexOptions.Compiled);
        private static readonly Regex AngleList = new Regex(@"[<\[]([^<>\[\]]{3,200})[>\]]", RegexOptions.Compiled);
        private static readonly Regex Phrase = new Regex(
            @"(?:allowed|possible|valid|accepted|supported|available)\s+(?:values?|options?|choices?)(?:\s+(?:are|is))?\s*[:=]?\s*(?<list>[^\r\n]+)|(?:one\s+of|choices?)\s*[:=]\s*(?<list>[^\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChoiceToken = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.:@+-]*$", RegexOptions.Compiled);
        private static readonly Regex Abbrev = new Regex(@"^([A-Za-z0-9][A-Za-z0-9_.:@+-]*)\[([A-Za-z0-9_.:@+-]+)\]$", RegexOptions.Compiled);

        /// <summary>
        /// Giá trị cho phép trong đoạn trợ giúp của một cờ/đối số: <c>{a,b}</c>, <c>&lt;a|b|c&gt;</c>, "Allowed values are …", "one of: …".
        /// Chỉ nhận danh sách mà MỌI mục đều là một từ hợp lệ (tránh nuốt câu văn); <c>q[uiet]</c> được mở thành <c>quiet</c>.
        /// Không đủ chắc chắn thì trả null để dropdown không đưa gợi ý sai.
        /// </summary>
        public static List<string> ExtractChoices(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            foreach (Match m in Braces.Matches(text))
            {
                var list = CleanChoices(m.Groups[1].Value, requireMixedCase: false);
                if (list != null)
                    return list;
            }
            foreach (Match m in AngleList.Matches(text))
            {
                if (m.Groups[1].Value.IndexOf('|') < 0)
                    continue;
                var list = CleanChoices(m.Groups[1].Value, requireMixedCase: true);
                if (list != null)
                    return list;
            }
            foreach (Match m in Phrase.Matches(text))
            {
                var tail = m.Groups["list"].Value;
                var cut = tail.IndexOf(". ", StringComparison.Ordinal);
                var list = CleanChoices((cut >= 0 ? tail.Substring(0, cut) : tail).TrimEnd().TrimEnd('.'), requireMixedCase: false);
                if (list != null)
                    return list;
            }
            return null;
        }

        private static List<string> CleanChoices(string raw, bool requireMixedCase)
        {
            var parts = Regex.Split(raw, @"\s*(?:\||,|;|/|\bor\b|\band\b)\s*", RegexOptions.IgnoreCase)
                .Select(p => p.Trim().Trim('\'', '"', '`', '.'))
                .Where(p => p.Length > 0).ToList();
            if (parts.Count < 2 || parts.Count > 40)
                return null;
            var result = new List<string>();
            foreach (var part in parts)
            {
                var abbrev = Abbrev.Match(part);
                var value = abbrev.Success ? abbrev.Groups[1].Value + abbrev.Groups[2].Value : part;
                if (!ChoiceToken.IsMatch(value) || value.Length > 40)
                    return null;
                if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                    result.Add(value);
            }
            // <PROJECT | SOLUTION | FILE> là mô tả kiểu (toàn chữ hoa), không phải giá trị cố định.
            if (requireMixedCase && result.All(v => v.Equals(v.ToUpperInvariant(), StringComparison.Ordinal)))
                return null;
            return result.Count >= 2 ? result : null;
        }

        private static readonly Regex ArgumentsHeading = new Regex(@"^\s{0,2}(?:arguments?|args|positional\s+arguments?|positionals?)\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArgLine = new Regex(@"^(\s+)(<[^>]+>|\[<[^>]+>\]|\{[^}]+\}|\[[^\]]+\]|[A-Za-z][\w.-]*)(\.\.\.)?(?:\s{2,}(\S.*))?\s*$", RegexOptions.Compiled);
        private static readonly Regex PathWord = new Regex(@"\b(path|paths|file|files|dir|directory|directories|folder|folders|project|projects|solution|sln)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DirWord = new Regex(@"\b(dir|directory|directories|folder|folders)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Đọc mục "Arguments:" (hoặc "positional arguments:"): mỗi dòng thụt đầu dòng là một đối số vị trí, các dòng thụt sâu hơn là mô tả tiếp.
        /// Đối số có tên gợi ý đường dẫn (path/file/dir/project/solution) được đánh dấu để gợi ý file trong thư mục chạy.
        /// </summary>
        public static List<ArgHint> ParseArguments(string text)
        {
            var result = new List<ArgHint>();
            var inSection = false;
            var baseIndent = -1;
            ArgHint last = null;
            var tokens = new Dictionary<ArgHint, string>();
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0)
                {
                    last = null;
                    continue;
                }
                if (!char.IsWhiteSpace(raw[0]) || ArgumentsHeading.IsMatch(raw))
                {
                    inSection = ArgumentsHeading.IsMatch(raw);
                    baseIndent = -1;
                    last = null;
                    continue;
                }
                if (!inSection)
                    continue;

                var indent = LeadingSpaces(raw);
                if (last != null && baseIndent >= 0 && indent > baseIndent)
                {
                    last.Description = ((last.Description ?? "") + " " + raw.Trim()).Trim();
                    continue;
                }
                var m = ArgLine.Match(raw);
                if (!m.Success)
                    continue;
                if (baseIndent < 0)
                    baseIndent = indent;
                var token = m.Groups[2].Value;
                var name = token.Trim('<', '>', '[', ']', '{', '}').Trim();
                if (name.Length == 0 || result.Any(a => a.Name == name))
                    continue;
                last = new ArgHint
                {
                    Name = name,
                    Description = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null,
                    Variadic = m.Groups[3].Success,
                };
                result.Add(last);
                tokens[last] = token;
            }
            foreach (var arg in result)
            {
                arg.Choices = ExtractChoices(tokens[arg] + " " + arg.Description);
                arg.IsPath = arg.Choices == null && PathWord.IsMatch(arg.Name + " " + arg.Description);
                arg.DirectoryOnly = arg.IsPath && DirWord.IsMatch(arg.Name) && !Regex.IsMatch(arg.Name, @"\b(file|files|project|projects|solution|sln)\b", RegexOptions.IgnoreCase);
            }
            return result;
        }

        /// <summary>Dòng tiêu đề nhóm lệnh con: không thụt đầu dòng và nhắc tới "command"/"lệnh", ví dụ "Commands:" hay "SDK commands:".</summary>
        private static readonly Regex CommandsHeading = new Regex(@"^(?=\S).*?\b(commands?|lệnh)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Một lệnh con: thụt đầu dòng, một từ (có thể kèm &lt;tham số&gt;), hai khoảng trắng trở lên rồi mô tả.</summary>
        private static readonly Regex SubcommandLine = new Regex(@"^(\s+)([A-Za-z][A-Za-z0-9_.:@-]*)(?:\s*,\s*[A-Za-z][\w.-]*)*(?:\s+(?:<[^>]*>|\[[^\]]*\]))*\s{2,}(\S.*)$", RegexOptions.Compiled);

        /// <summary>
        /// Đọc lệnh con từ các nhóm có tiêu đề nhắc tới "commands". Trợ giúp không có nhóm như vậy (ví dụ <c>git --help</c>)
        /// thì không đọc ra gì và bạn nhập tay; đoán thêm sẽ lẫn mô tả vào danh sách.
        /// </summary>
        public static List<CapabilityEntry> ParseSubcommands(string text)
        {
            var result = new List<CapabilityEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inGroup = false;
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0)
                    continue;
                if (!char.IsWhiteSpace(raw[0]))
                {
                    inGroup = CommandsHeading.IsMatch(raw);
                    continue;
                }
                if (!inGroup)
                    continue;
                var m = SubcommandLine.Match(raw);
                if (!m.Success || !CapabilityStore.SubcommandName.IsMatch(m.Groups[2].Value) || !seen.Add(m.Groups[2].Value))
                    continue;
                var excerpt = raw.TrimEnd();
                result.Add(new CapabilityEntry
                {
                    Name = m.Groups[2].Value,
                    TakesValue = false,
                    Description = m.Groups[3].Value.Trim(),
                    Excerpt = excerpt.Length > 240 ? excerpt.Substring(0, 240) + "…" : excerpt,
                    Source = "scan",
                    Role = "",
                });
            }
            return result;
        }

        private static int LeadingSpaces(string line)
        {
            var n = 0;
            while (n < line.Length && (line[n] == ' ' || line[n] == '\t'))
                n++;
            return n;
        }
    }
}
