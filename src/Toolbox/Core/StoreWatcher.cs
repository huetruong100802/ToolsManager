using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Toolbox.Core
{
    /// <summary>
    /// Theo dõi các file dữ liệu dùng chung với agent (tools.json, workspaces.json, proposals.json, activity.jsonl) và báo khi file đổi.
    /// Gộp các sự kiện dồn dập (một lần ghi sinh vài sự kiện, ghi ra file tạm rồi thay thế) trong cửa sổ <see cref="Delay"/>
    /// thành một lần báo. <see cref="Changed"/> chạy trên luồng nền: người nghe tự chuyển về luồng giao diện.
    /// </summary>
    public sealed class StoreWatcher : IDisposable
    {
        public static readonly string[] DefaultFiles = { "tools.json", "workspaces.json", "proposals.json", "activity.jsonl" };

        private readonly object _gate = new object();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files;
        private readonly Timer _timer;
        private FileSystemWatcher _watcher;
        private bool _disposed;

        public StoreWatcher(string directory, TimeSpan delay, IEnumerable<string> files = null)
        {
            Delay = delay;
            _files = new HashSet<string>(files ?? DefaultFiles, StringComparer.OrdinalIgnoreCase);
            _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            try
            {
                Directory.CreateDirectory(directory);
                _watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    InternalBufferSize = 16 * 1024,
                };
                _watcher.Changed += OnEvent;
                _watcher.Created += OnEvent;
                _watcher.Deleted += OnEvent;
                _watcher.Renamed += (s, e) => { OnName(e.Name); OnName(e.OldName); };
                _watcher.Error += (s, e) => Raise(_files.ToList()); // tràn bộ đệm: coi như mọi file đều có thể đã đổi
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                // Không theo dõi được (thư mục không tạo được...): GUI vẫn dùng được, chỉ không tự cập nhật.
                _watcher?.Dispose();
                _watcher = null;
            }
        }

        /// <summary>Thời gian chờ gộp sự kiện.</summary>
        public TimeSpan Delay { get; }

        /// <summary>true nếu đang theo dõi được thư mục.</summary>
        public bool IsWatching => _watcher != null;

        /// <summary>Tên các file đã đổi (không trùng) sau một đợt sự kiện.</summary>
        public event Action<IReadOnlyList<string>> Changed;

        private void OnEvent(object sender, FileSystemEventArgs e) => OnName(e.Name);

        private void OnName(string name)
        {
            var file = Path.GetFileName(name ?? "");
            if (file.Length > 0 && _files.Contains(file))
                Raise(new[] { file });
        }

        /// <summary>Đánh dấu file đổi (cũng dùng cho test) và hẹn báo sau <see cref="Delay"/>.</summary>
        internal void Raise(IEnumerable<string> files)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                foreach (var file in files)
                    _pending.Add(file);
                _timer.Change(Delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void Flush()
        {
            List<string> batch;
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0)
                    return;
                batch = _pending.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                _pending.Clear();
            }
            try
            {
                Changed?.Invoke(batch);
            }
            catch (Exception)
            {
                // Lỗi của người nghe không được làm chết bộ hẹn giờ.
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            _watcher?.Dispose();
            _timer.Dispose();
        }
    }
}
