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
    }

    /// <summary>Kết quả một lần quét: các mục đọc được, dấu vân tay output, lỗi nếu có.</summary>
    public sealed class ScanOutcome
    {
        public string Command { get; set; }
        public string Fingerprint { get; set; }
        public List<CapabilityEntry> Entries { get; set; } = new List<CapabilityEntry>();
        public string Error { get; set; }
    }

    /// <summary>Đọc, ghi và cập nhật bảng khả năng. Mọi thao tác nhận thư mục làm tham số để test không đụng dữ liệu thật.</summary>
    public static class CapabilityStore
    {
        /// <summary>Vai trò của cờ cổng; token <c>{portFlag}</c> trong mẫu lệnh được thay bằng cờ có vai trò này.</summary>
        public const string PortRole = "port";

        /// <summary>Tên cờ hợp lệ để ghép vào dòng lệnh: <c>-x</c> hoặc <c>--ten-co</c>.</summary>
        public static readonly Regex FlagName = new Regex(@"^--?[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);

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
                data.Entries = (data.Entries ?? new List<CapabilityEntry>()).Where(e => e != null && FlagName.IsMatch(e.Name ?? "")).ToList();
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
            if (!FlagName.IsMatch(name))
                throw new ArgumentException("Tên cờ phải như --port hoặc -p, chỉ gồm chữ, số, - và _.");
            if (data.Entries.Any(e => SameAction(e.Action, action) && e.Name == name))
                throw new ArgumentException($"{name} đã có trong lệnh này.");
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

        public static void Remove(ToolCapabilities data, string action, string name) =>
            data.Entries.RemoveAll(e => SameAction(e.Action, action) && e.Name == name);

        /// <summary>Cờ có vai trò cổng của action, hoặc null nếu chưa gán.</summary>
        public static CapabilityEntry PortFlag(ToolCapabilities data, string action) =>
            data.Entries.FirstOrDefault(e => SameAction(e.Action, action) && e.Role == PortRole && FlagName.IsMatch(e.Name ?? ""));

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

        public static ScanOutcome Run(string command, string workingDirectory, int timeoutMs = DefaultTimeoutMs)
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
            outcome.Fingerprint = Fingerprint(text);
            if (outcome.Entries.Count == 0)
            {
                // Kèm đầu output để biết vì sao: lệnh lỗi (thiếu cài đặt, file cấu hình sai) hay tool không có trợ giúp chuẩn.
                var head = (text ?? "").Trim();
                if (head.Length > 200)
                    head = head.Substring(0, 200) + "…";
                outcome.Error = "Không đọc được cờ nào." + (head.Length > 0 ? " Đầu ra: " + head.Replace("\r", " ").Replace("\n", " ") : " Lệnh không in gì.");
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
        private static readonly Regex FlagLine = new Regex(@"^(\s*)(?:-[A-Za-z],?\s+)?(--?[A-Za-z][A-Za-z0-9_-]*)(.*)$", RegexOptions.Compiled);

        /// <summary>Giá trị ngay sau tên: "=x" hoặc một khoảng trắng rồi một từ (không phải hai khoảng trắng, vì sau đó là mô tả).</summary>
        private static readonly Regex ValueAfter = new Regex(@"^(=|\s(?!\s))(<[^>]*>|\[[^\]]*\]|[A-Za-z0-9][\w.|<>\[\]-]*)(\s|$)", RegexOptions.Compiled);

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
