using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Kết quả <see cref="VariableGroups.Resolve"/>: môi trường để truyền vào process và danh sách giá trị secret để che log.
    /// <see cref="Error"/> khác null nghĩa là không chạy được (thiếu giá trị secret, nhóm không còn, không giải mã được).
    /// </summary>
    public sealed class ResolvedVariables
    {
        /// <summary>Không gắn nhóm: không biến, không secret, không lỗi.</summary>
        public static ResolvedVariables None => new ResolvedVariables();

        /// <summary>Tên → giá trị plaintext (không phân biệt hoa thường như môi trường Windows). Rỗng khi có lỗi.</summary>
        public Dictionary<string, string> Env { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Giá trị secret đủ dài để che (>= <see cref="VariableGroups.MinMaskLength"/> ký tự), dài trước, không trùng.</summary>
        public List<string> Secrets { get; } = new List<string>();

        /// <summary>Thông báo lỗi D5 (hiển thị cho người dùng); null nếu ổn.</summary>
        public string Error { get; set; }

        public bool Ok => Error == null;
    }

    /// <summary>Nhóm biến của workspace: mã hoá secret bằng DPAPI, kiểm tên biến, dựng môi trường khi chạy, theo dõi nơi dùng.</summary>
    public static class VariableGroups
    {
        /// <summary>Secret ngắn hơn mức này không bị che khỏi output (tránh thay hàng loạt chuỗi vô nghĩa).</summary>
        public const int MinMaskLength = 4;

        /// <summary>Độ dài tối đa của tên biến.</summary>
        public const int MaxNameLength = 255;

        /// <summary>Thay thế cho secret trong output/log.</summary>
        public const string Mask = "***";

        // Entropy riêng của Toolbox: dữ liệu DPAPI của app khác cùng tài khoản không giải mã được bằng nó và ngược lại.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Toolbox.VariableGroups.v1");

        /// <summary>Tên dễ gây xung đột với môi trường của Toolbox/công cụ dựng (so không phân biệt hoa thường).</summary>
        private static readonly string[] Risky = { "PATH", "Configuration", "Platform", "OutDir", "TOOLBOX_DATA" };

        /// <summary>Mã hoá plaintext bằng DPAPI (CurrentUser + entropy riêng), trả về base64. Chỉ người dùng Windows này giải mã được.</summary>
        public static string Protect(string plain)
        {
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Giải mã chuỗi của <see cref="Protect"/>. Ném <see cref="CryptographicException"/> / <see cref="FormatException"/> nếu dữ liệu hỏng hoặc của máy/tài khoản khác.</summary>
        public static string Unprotect(string enc)
        {
            if (enc == null) throw new ArgumentNullException(nameof(enc));
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(enc), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Đặt giá trị cho biến secret (mã hoá vào Enc, xoá Value). plain rỗng → xoá giá trị (Cần nhập). Biến phải đã là Secret.
        /// </summary>
        public static void SetSecret(GroupVariable variable, string plain)
        {
            variable.Secret = true;
            variable.Value = null;
            variable.Enc = string.IsNullOrEmpty(plain) ? null : Protect(plain);
        }

        /// <summary>Lỗi của một tên biến (rỗng, có '=' hoặc ký tự điều khiển, quá dài); null nếu hợp lệ. Không xét trùng.</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Tên biến không được để trống.";
            if (name.Length > MaxNameLength)
                return "Tên biến \"" + Shorten(name) + "\" dài quá " + MaxNameLength + " ký tự.";
            if (name.IndexOf('=') >= 0)
                return "Tên biến \"" + name + "\" không được chứa dấu \"=\".";
            if (name.Any(char.IsControl))
                return "Tên biến \"" + Shorten(name) + "\" có ký tự điều khiển.";
            return null;
        }

        /// <summary>Tất cả lỗi của nhóm (tên nhóm trống, tên biến sai, tên biến trùng không phân biệt hoa thường). Rỗng nếu hợp lệ.</summary>
        public static List<string> Validate(VariableGroup group)
        {
            var errors = new List<string>();
            if (group == null)
            {
                errors.Add("Thiếu nhóm biến.");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(group.Label))
                errors.Add("Tên nhóm không được để trống.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in group.Variables ?? new List<GroupVariable>())
            {
                var error = ValidateName(v?.Name);
                if (error != null)
                {
                    errors.Add(error);
                    continue;
                }
                if (!seen.Add(v.Name.Trim()))
                    errors.Add("Tên biến trùng (không phân biệt hoa thường): \"" + v.Name + "\".");
            }
            return errors;
        }

        /// <summary>Cảnh báo (không chặn lưu) cho tên biến dễ xung đột; null nếu không có.</summary>
        public static string WarningFor(string name)
        {
            var n = (name ?? "").Trim();
            var hit = Risky.FirstOrDefault(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase));
            return hit == null ? null : "Biến \"" + n + "\" ghi đè biến cùng tên mà Toolbox/công cụ dựng đang dùng; chắc chắn mới đặt.";
        }

        /// <summary>Cảnh báo của cả nhóm (mỗi tên dễ xung đột một dòng).</summary>
        public static List<string> Warnings(VariableGroup group) =>
            (group?.Variables ?? new List<GroupVariable>())
                .Select(v => WarningFor(v?.Name))
                .Where(w => w != null)
                .ToList();

        /// <summary>Nhóm theo id (không phân biệt hoa thường); null nếu không có.</summary>
        public static VariableGroup Find(Workspace ws, string groupId) =>
            string.IsNullOrWhiteSpace(groupId)
                ? null
                : ws?.VariableGroups?.Find(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Dựng môi trường cho nhóm <paramref name="groupId"/> của <paramref name="ws"/>. groupId trống → <see cref="ResolvedVariables.None"/>.
        /// Lỗi (Error khác null): nhóm không còn; secret chưa có giá trị ("Nhóm biến X chưa có giá trị cho KEY, nhập ở tab Biến");
        /// secret không giải mã được. Không ném ngoại lệ.
        /// </summary>
        public static ResolvedVariables Resolve(Workspace ws, string groupId)
        {
            var result = new ResolvedVariables();
            if (string.IsNullOrWhiteSpace(groupId))
                return result;

            var group = Find(ws, groupId);
            if (group == null)
            {
                result.Error = "Nhóm biến \"" + groupId + "\" không còn trong workspace, gắn lại nhóm khác ở profile.";
                return result;
            }

            var secrets = new List<string>();
            foreach (var v in group.Variables ?? new List<GroupVariable>())
            {
                if (v == null || string.IsNullOrWhiteSpace(v.Name))
                    continue;
                string value;
                if (v.Secret)
                {
                    if (string.IsNullOrEmpty(v.Enc))
                    {
                        Fail(result, "Nhóm biến " + group.Label + " chưa có giá trị cho " + v.Name + ", nhập ở tab Biến");
                        return result;
                    }
                    try
                    {
                        value = Unprotect(v.Enc);
                    }
                    catch (Exception e) when (e is CryptographicException || e is FormatException)
                    {
                        Fail(result, "Nhóm biến " + group.Label + " không đọc được giá trị của " + v.Name
                            + " (lưu bởi tài khoản/máy khác), nhập lại ở tab Biến");
                        return result;
                    }
                    if (value.Length >= MinMaskLength)
                        secrets.Add(value);
                }
                else
                    value = v.Value ?? "";
                result.Env[v.Name.Trim()] = value;
            }
            result.Secrets.AddRange(secrets.Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length));
            return result;
        }

        private static void Fail(ResolvedVariables result, string error)
        {
            result.Env.Clear();
            result.Secrets.Clear();
            result.Error = error;
        }

        /// <summary>Thay mỗi giá trị trong <paramref name="secrets"/> xuất hiện trong <paramref name="text"/> bằng <see cref="Mask"/> (khớp chính xác, phân biệt hoa thường).</summary>
        public static string MaskSecrets(string text, IEnumerable<string> secrets)
        {
            if (string.IsNullOrEmpty(text) || secrets == null)
                return text;
            foreach (var s in secrets.Where(s => !string.IsNullOrEmpty(s) && s.Length >= MinMaskLength).OrderByDescending(s => s.Length))
                text = text.Replace(s, Mask);
            return text;
        }

        /// <summary>Các profile của workspace đang gắn nhóm <paramref name="groupId"/> (để chặn xoá nhóm đang dùng). Rỗng nếu không có.</summary>
        public static List<Profile> UsedBy(Workspace ws, string groupId) =>
            string.IsNullOrWhiteSpace(groupId)
                ? new List<Profile>()
                : (ws?.Profiles ?? new List<Profile>())
                    .Where(p => p != null && string.Equals(p.VariableGroupId, groupId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

        private static string Shorten(string s) => s.Length <= 40 ? s : s.Substring(0, 40) + "...";
    }
}
