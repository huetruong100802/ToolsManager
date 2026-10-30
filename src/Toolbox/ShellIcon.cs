using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Toolbox
{
    /// <summary>Icon Windows gắn với file (lấy từ trong exe hoặc theo đuôi file).</summary>
    public static class ShellIcon
    {
        /// <summary>Trả về null nếu file không có icon. Kết quả đã Freeze nên dùng được trên thread nền.</summary>
        public static ImageSource Load(string path)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                        return null;
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is COMException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
