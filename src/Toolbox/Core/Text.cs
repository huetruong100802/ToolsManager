using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Toolbox.Core
{
    internal static class Text
    {
        private static readonly Regex NonAlphanumeric = new Regex("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex ValidId = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        /// <summary>Bỏ dấu + chữ thường, để tìm kiếm không phân biệt dấu ("cong cu" khớp "Công cụ").</summary>
        public static string Fold(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var decomposed = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
            return new string(chars.ToArray()).Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>Sinh id kebab-case từ tên: "EasyModbus Server" → "easymodbus-server".</summary>
        public static string ToId(string name)
        {
            var id = NonAlphanumeric.Replace(Fold(name), "-").Trim('-');
            if (id.Length > 50)
                id = id.Substring(0, 50).TrimEnd('-');
            return id.Length == 0 ? "tool" : id;
        }

        /// <summary>Id hợp lệ không chứa ký tự đường dẫn nên dùng làm tên thư mục trong gói được.</summary>
        public static bool IsValidId(string id) => id != null && ValidId.IsMatch(id);
    }
}
