using System;
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

        /// <summary>"45 giây", "12 phút", "2 giờ 5 phút": thời gian đã chạy, hiện trên dòng tool.</summary>
        public static string Duration(TimeSpan span)
        {
            var seconds = (int)Math.Max(0, span.TotalSeconds);
            if (seconds < 60)
                return seconds + " giây";
            var minutes = seconds / 60;
            return minutes < 60 ? minutes + " phút" : minutes / 60 + " giờ " + minutes % 60 + " phút";
        }

        /// <summary>"vừa xong", "30 giây trước", "5 phút trước", "3 giờ trước", "2 ngày trước".</summary>
        public static string Ago(TimeSpan span)
        {
            var seconds = (int)Math.Max(0, span.TotalSeconds);
            if (seconds < 10)
                return "vừa xong";
            if (seconds < 60)
                return seconds + " giây trước";
            if (seconds < 3600)
                return seconds / 60 + " phút trước";
            return seconds < 86400 ? seconds / 3600 + " giờ trước" : seconds / 86400 + " ngày trước";
        }

        /// <summary>Id hợp lệ không chứa ký tự đường dẫn nên dùng làm tên thư mục trong gói được.</summary>
        public static bool IsValidId(string id) => id != null && ValidId.IsMatch(id);
    }
}
