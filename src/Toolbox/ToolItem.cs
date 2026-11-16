using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>Trạng thái của tool, hiện bằng chấm màu trên dòng.</summary>
    public enum ToolStatus
    {
        /// <summary>File có sẵn, chạy được.</summary>
        Ready,
        /// <summary>App chưa cài trên máy này nhưng có bộ cài đi kèm.</summary>
        NotInstalled,
        /// <summary>Không tìm thấy file.</summary>
        Missing,
    }

    /// <summary>
    /// Một dòng trong danh sách: tool + trạng thái chọn để đóng gói/xoá. Không thuộc tính nào đọc đĩa: trạng thái file do
    /// <see cref="ApplyProbe"/> đẩy vào sau khi kiểm ở luồng nền, icon nạp nền khi dòng thật sự hiện ra.
    /// </summary>
    public sealed class ToolItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        private bool? _exists;
        private bool _canInstall;
        private ImageSource _icon;
        private bool _iconRequested;
        private string _iconPath;
        private string[] _foldedTags = new string[0];

        public ToolItem(ToolEntry tool)
        {
            Tool = tool;
            Index();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ToolEntry Tool { get; private set; }

        /// <summary>Biểu tượng ghim (đang ghim: bỏ ghim) và gợi ý của nút ghim lên Trang chủ.</summary>
        public string PinGlyph => Tool.Pinned ? "" : "";

        public string PinTip => Tool.Pinned ? "Bỏ ghim khỏi Trang chủ" : "Ghim lên Trang chủ để mở nhanh";

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }

        /// <summary>Chữ cái đầu tên tool, hiện trong ô icon của card.</summary>
        public string Initial => string.IsNullOrWhiteSpace(Tool.Name) ? "?" : Tool.Name.Trim().Substring(0, 1).ToUpperInvariant();

        // ---------- Icon ----------

        /// <summary>Icon của file; null thì hiện chữ cái đầu. Lần đọc đầu tiên mới xin nạp, và nạp ở luồng nền.</summary>
        public ImageSource Icon
        {
            get
            {
                // Chỉ dòng đang hiện mới được hỏi icon (danh sách ảo hoá), nên 1000 tool cũng chỉ nạp vài chục icon.
                if (!_iconRequested)
                    RequestIcon();
                return _icon;
            }
        }

        public Visibility IconVisibility => _icon == null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InitialVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;

        private void RequestIcon()
        {
            _iconRequested = true;
            var path = Tool.ResolvedPath;
            _iconPath = path;
            if (string.IsNullOrWhiteSpace(path) || Tool.IsCommand)
                return;
            if (IconCache.TryGet(path, out var cached))
            {
                _icon = cached;
                return;
            }
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            IconCache.Request(path, icon => dispatcher.BeginInvoke(new Action(() =>
            {
                if (icon == null || !string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase))
                    return;
                _icon = icon;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(InitialVisibility));
            })));
        }

        private void ResetIcon()
        {
            _icon = null;
            _iconRequested = false;
            _iconPath = null;
        }

        // ---------- Trạng thái file ----------

        /// <summary>File còn không. Chưa kiểm xong thì coi là còn, để dòng không nháy cảnh báo lúc mới mở.</summary>
        public bool Exists => _exists ?? true;

        /// <summary>Đã kiểm file ít nhất một lần.</summary>
        public bool IsProbed => _exists.HasValue;

        /// <summary>Tool có vấn đề về đường dẫn: mất file hoặc app chưa cài.</summary>
        public bool HasProblem => !Exists;

        public ToolStatus Status => Exists ? ToolStatus.Ready
            : _canInstall ? ToolStatus.NotInstalled
            : ToolStatus.Missing;

        /// <summary>Xanh: chạy được. Vàng: chưa cài. Đỏ: không tìm thấy.</summary>
        public Brush StatusBrush => Status switch
        {
            ToolStatus.Ready => Resource("Ok"),
            ToolStatus.NotInstalled => Resource("WarnInk"),
            _ => Resource("Danger"),
        };

        public string StatusTip => Status switch
        {
            ToolStatus.Ready => "Sẵn sàng",
            ToolStatus.NotInstalled => "Chưa cài trên máy này",
            _ => "Không tìm thấy file",
        };

        public string Warning => Exists ? null
            : _canInstall ? "Chưa cài trên máy này — bấm Cài đặt để chạy bộ cài kèm theo"
            : "Không tìm thấy: " + Tool.ResolvedPath;

        public Visibility WarningVisibility => Exists ? Visibility.Collapsed : Visibility.Visible;

        public bool CanInstall => _canInstall;

        public Visibility InstallVisibility => _canInstall ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Lệnh hoặc đường dẫn đầy đủ, hiện khi rê chuột vào tên.</summary>
        public string PathTip => Tool.IsCommand
            ? Tool.Command + (string.IsNullOrWhiteSpace(Tool.Path) ? "" : "\nTại: " + Tool.ResolvedPath)
            : Tool.ResolvedPath;

        /// <summary>Kiểm file trên đĩa. Gọi ở luồng nền: ổ mạng hoặc ổ đang ngủ có thể mất vài giây.</summary>
        public static (bool Exists, bool CanInstall) Probe(ToolEntry tool)
        {
            // Tool lệnh không có thư mục riêng thì chạy tại thư mục workspace: không có gì để mất.
            if (tool.IsCommand && string.IsNullOrWhiteSpace(tool.Path))
                return (true, false);
            var exists = tool.PathExists;
            return (exists, !exists && tool.Pack == PackMode.Setup && File.Exists(tool.ResolvedSetup));
        }

        /// <summary>Nhận kết quả kiểm file. Trả về true nếu thứ đang hiện trên dòng đổi.</summary>
        public bool ApplyProbe(bool exists, bool canInstall)
        {
            var changed = exists != Exists || canInstall != _canInstall;
            var appeared = exists && _exists == false;
            _exists = exists;
            _canInstall = canInstall;
            if (!changed)
                return false;
            if (appeared)
            {
                // File vừa được cài xong thì có icon thật.
                IconCache.Invalidate(Tool.ResolvedPath);
                ResetIcon();
            }
            OnPropertyChanged(null);
            return true;
        }

        // ---------- Trạng thái chạy (do LibraryView đẩy vào từ RunTracker) ----------

        private RunInstance _current;
        private RunInstance _last;

        public void ApplyRun(RunInstance current, RunInstance last)
        {
            if (ReferenceEquals(current, _current) && ReferenceEquals(last, _last) && (current == null || current.IsRunning == _wasRunning))
                return;
            _current = current;
            _last = last;
            _wasRunning = current != null && current.IsRunning;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(RunVisibility));
            OnPropertyChanged(nameof(StopVisibility));
            Tick();
        }

        private bool _wasRunning;

        /// <summary>Làm mới chữ "chạy được 5 phút" / "3 phút trước" mỗi giây.</summary>
        public void Tick()
        {
            if (_current == null && _last == null)
                return;
            OnPropertyChanged(nameof(UptimeVisibility));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(ResultVisibility));
            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(ResultBrush));
        }

        public bool IsRunning => _current != null && _current.IsRunning;

        /// <summary>Đang chạy thì nút Chạy nhường chỗ cho nút Dừng.</summary>
        public Visibility RunVisibility => !Exists || IsRunning ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Mất file mà không có bộ cài: nút chính của dòng là "Trỏ lại…" thay cho "Chạy".</summary>
        public Visibility RelocateVisibility => Status == ToolStatus.Missing && !IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StopVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UptimeVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public string UptimeText => IsRunning ? "Đang chạy " + Text.Duration(DateTime.UtcNow - _current.StartedAt) : null;

        /// <summary>Kết quả lần chạy trước, chỉ hiện khi không còn chạy.</summary>
        public Visibility ResultVisibility => !IsRunning && _last != null ? Visibility.Visible : Visibility.Collapsed;

        public string ResultText
        {
            get
            {
                if (_last?.EndedAt == null)
                    return null;
                var ago = Text.Ago(DateTime.UtcNow - _last.EndedAt.Value);
                if (_last.Stopped)
                    return "Đã dừng · " + ago;
                return _last.ExitCode.HasValue && _last.ExitCode.Value != 0
                    ? "Lỗi mã " + _last.ExitCode.Value + " · " + ago
                    : "Đã thoát · " + ago;
            }
        }

        /// <summary>Đỏ chỉ khi thoát với mã lỗi; chữ luôn nói rõ nên màu không phải kênh duy nhất.</summary>
        public Brush ResultBrush => _last != null && !_last.Stopped && _last.ExitCode.HasValue && _last.ExitCode.Value != 0
            ? Resource("Danger")
            : Resource("Muted");

        // ---------- Tag ----------

        /// <summary>Số tag hiện trên dòng; phần còn lại gom thành chip "+n" để dòng không bị dài ra.</summary>
        public const int MaxVisibleTags = 3;

        private List<string> Tags => Tool.Tags ?? new List<string>();

        public IEnumerable<string> VisibleTags => Tags.Take(MaxVisibleTags);

        public Visibility MoreTagsVisibility => Tags.Count > MaxVisibleTags ? Visibility.Visible : Visibility.Collapsed;

        public string MoreTags => Tags.Count > MaxVisibleTags ? "+" + (Tags.Count - MaxVisibleTags) : null;

        /// <summary>Toàn bộ tag, hiện khi rê chuột vào vùng tag.</summary>
        public string AllTagsTip => Tags.Count == 0 ? null : string.Join(", ", Tags);

        // ---------- Tìm và sắp xếp ----------

        /// <summary>Id, tên, mô tả, tag đã bỏ dấu và viết thường; tính một lần để gõ tìm không phải tính lại cho từng tool.</summary>
        public string SearchText { get; private set; } = "";

        /// <summary>Lần cuối chạy (từ Toolbox hoặc agent); null nếu chưa chạy.</summary>
        public DateTime? LastUsed { get; private set; }

        private void Index()
        {
            SearchText = Text.Fold(string.Join(" ", new[] { Tool.Id, Tool.Name, Tool.Description }.Concat(Tags)));
            _foldedTags = Tags.Select(Text.Fold).ToArray();
            RefreshUsage();
        }

        /// <summary>Đọc lại lần dùng cuối (sau khi vừa chạy tool).</summary>
        public void RefreshUsage()
        {
            var gui = UsageStore.Shared.Get(UsageStore.Key("tool", Tool.Id))?.Last;
            LastUsed = gui.HasValue && Tool.LastUsedAt.HasValue
                ? (gui.Value > Tool.LastUsedAt.Value ? gui : Tool.LastUsedAt)
                : gui ?? Tool.LastUsedAt;
        }

        /// <summary>Mọi từ phải có trong id/tên/mô tả/tag; nếu có lọc tag thì tool phải mang ít nhất một tag trong đó.</summary>
        public bool Matches(string[] foldedTerms, ICollection<string> foldedTags)
        {
            if (foldedTags != null && foldedTags.Count > 0 && !_foldedTags.Any(foldedTags.Contains))
                return false;
            foreach (var term in foldedTerms)
                if (SearchText.IndexOf(term, StringComparison.Ordinal) < 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Gắn lại vào bản ghi mới của cùng tool sau khi danh sách được nạp lại. Giữ trạng thái tick, trạng thái chạy, kết quả kiểm file
        /// và icon (trừ khi đường dẫn đổi), nên sửa một tool không làm cả danh sách phải dựng lại.
        /// </summary>
        public void Rebind(ToolEntry tool)
        {
            if (ReferenceEquals(tool, Tool))
                return;
            var pathChanged = !string.Equals(tool.ResolvedPath, Tool.ResolvedPath, StringComparison.OrdinalIgnoreCase);
            Tool = tool;
            Index();
            if (pathChanged)
                ResetIcon();
            OnPropertyChanged(null); // tất cả thuộc tính
        }

        private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
