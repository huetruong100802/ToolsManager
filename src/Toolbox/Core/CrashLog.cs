using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Toolbox.Core
{
    /// <summary>
    /// Ghi lỗi không lường trước vào crash.log trong thư mục dữ liệu, để người dùng gửi kèm khi báo lỗi thay vì chỉ thấy app biến mất.
    /// Ghi log không bao giờ được ném lỗi tiếp.
    /// </summary>
    public static class CrashLog
    {
        private const long MaxBytes = 512 * 1024;

        public static string DefaultPath => Path.Combine(DataDir.Root, "crash.log");

        public static void Write(Exception exception, string context = null, string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                // File quá lớn (lỗi lặp lại nhiều lần): bắt đầu lại, chỉ các lỗi gần đây mới có ích.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Delete(path);
                var entry = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append("  Toolbox ").Append(typeof(CrashLog).Assembly.GetName().Version.ToString(3))
                    .Append(string.IsNullOrEmpty(context) ? "" : "  [" + context + "]")
                    .AppendLine()
                    .AppendLine(exception?.ToString() ?? "(không có thông tin lỗi)")
                    .AppendLine();
                File.AppendAllText(path, entry.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }
}
