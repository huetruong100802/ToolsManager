using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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

    /// <summary>
    /// Nạp icon ở luồng nền và nhớ theo đường dẫn. Trích icon từ exe là việc chậm (đọc đĩa, có khi bị trình diệt virus quét),
    /// nên danh sách chỉ xin icon cho dòng đang hiện và không bao giờ chờ trên luồng giao diện. Yêu cầu mới nhất được làm trước,
    /// để cuộn nhanh thì dòng đang nhìn thấy có icon trước các dòng đã cuộn qua.
    /// </summary>
    public static class IconCache
    {
        private sealed class Job
        {
            public string Path;
            public Action<ImageSource> Done;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Stack<Job> Pending = new Stack<Job>();
        private static Thread _worker;

        /// <summary>Thay hàm nạp trong test.</summary>
        internal static Func<string, ImageSource> Loader = path => File.Exists(path) ? ShellIcon.Load(path) : null;

        /// <summary>True nếu đã nạp đường dẫn này (icon có thể null: file không có icon).</summary>
        public static bool TryGet(string path, out ImageSource icon)
        {
            lock (Gate)
                return Cache.TryGetValue(path ?? "", out icon);
        }

        /// <summary>Nạp ở luồng nền; <paramref name="done"/> được gọi trên luồng nền với icon (hoặc null).</summary>
        public static void Request(string path, Action<ImageSource> done)
        {
            if (string.IsNullOrEmpty(path))
            {
                done(null);
                return;
            }
            lock (Gate)
            {
                Pending.Push(new Job { Path = path, Done = done });
                if (_worker == null)
                {
                    _worker = new Thread(Work) { IsBackground = true, Name = "IconCache", Priority = ThreadPriority.BelowNormal };
                    _worker.Start();
                }
                Monitor.Pulse(Gate);
            }
        }

        /// <summary>Quên icon của một đường dẫn (file vừa được cài hoặc thay thế).</summary>
        public static void Invalidate(string path)
        {
            lock (Gate)
                Cache.Remove(path ?? "");
        }

        private static void Work()
        {
            while (true)
            {
                Job request;
                ImageSource icon;
                bool cached;
                lock (Gate)
                {
                    while (Pending.Count == 0)
                        Monitor.Wait(Gate);
                    request = Pending.Pop();
                    cached = Cache.TryGetValue(request.Path, out icon);
                }

                if (!cached)
                {
                    try
                    {
                        icon = Loader(request.Path);
                    }
                    catch (Exception)
                    {
                        icon = null; // icon không đáng làm hỏng app
                    }
                    lock (Gate)
                        Cache[request.Path] = icon;
                }

                try
                {
                    request.Done(icon);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
