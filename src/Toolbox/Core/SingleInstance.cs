using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Toolbox.Core
{
    /// <summary>
    /// Giữ một cửa sổ Toolbox cho mỗi thư mục dữ liệu. Mở lần hai (bấm shortcut, double-click file .toolbox) thì bản đang chạy
    /// được đưa lên trước và nhận file cần mở qua named pipe; bản mới thoát. Hai cửa sổ cùng ghi một bộ dữ liệu và cùng giữ
    /// tác vụ ngầm riêng là nguồn của đủ thứ lỗi khó hiểu, nên chặn từ gốc.
    /// MCP server (<c>--mcp</c>) không đi qua đây: mỗi agent vẫn có tiến trình riêng.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly string _pipeName;
        private volatile bool _disposed;

        private SingleInstance(Mutex mutex, string pipeName)
        {
            _mutex = mutex;
            _pipeName = pipeName;
        }

        /// <summary>Tên ổn định theo thư mục dữ liệu, để bản chạy thử với TOOLBOX_DATA khác không đụng bản thật.</summary>
        public static string KeyFor(string dataDir)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).TrimEnd('\\', '/').ToLowerInvariant()));
                return "Toolbox.Gui." + BitConverter.ToString(hash, 0, 8).Replace("-", "");
            }
        }

        /// <summary>Trả về đối tượng giữ chỗ nếu đây là bản đầu tiên; null nếu đã có bản khác đang chạy với cùng khoá.</summary>
        public static SingleInstance TryAcquire(string key)
        {
            var mutex = new Mutex(initiallyOwned: false, name: @"Local\" + key);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true; // bản trước chết đột ngột: chỗ thuộc về ta
            }
            if (!owned)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstance(mutex, key);
        }

        /// <summary>Nghe tin nhắn từ các lần mở sau. <paramref name="onMessage"/> được gọi trên luồng nền.</summary>
        public void Listen(Action<string> onMessage)
        {
            var thread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            server.WaitForConnection();
                            using (var reader = new StreamReader(server, Encoding.UTF8))
                            {
                                var message = reader.ReadToEnd();
                                if (!_disposed)
                                    onMessage(message);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is UnauthorizedAccessException)
                    {
                        Thread.Sleep(200); // pipe lỗi tạm thời: thử nghe lại, không để vòng lặp quay cuồng
                    }
                }
            })
            { IsBackground = true, Name = "SingleInstance" };
            thread.Start();
        }

        /// <summary>Gửi tin nhắn cho bản đang chạy. False nếu không kết nối được trong thời hạn.</summary>
        public static bool Send(string key, string message, int timeoutMs = 3000)
        {
            try
            {
                AllowSetForegroundWindow(-1); // cho bản đang chạy quyền tự đưa cửa sổ lên trước
                using (var client = new NamedPipeClientStream(".", key, PipeDirection.Out))
                {
                    client.Connect(timeoutMs);
                    var bytes = Encoding.UTF8.GetBytes(message ?? "");
                    client.Write(bytes, 0, bytes.Length);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Dispose gọi từ luồng khác luồng đã lấy mutex: hệ điều hành tự nhả khi tiến trình thoát.
            }
            _mutex.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
