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

    /// <summary>Một dòng trong danh sách: tool + trạng thái chọn để đóng gói/xoá.</summary>
    public sealed class ToolItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        private (bool Exists, bool CanInstall) _state;

        public ToolItem(ToolEntry tool)
        {
            Tool = tool;
            _state = Probe();
            Icon = LoadIcon();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ToolEntry Tool { get; private set; }

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

        /// <summary>Icon của file; null thì hiện chữ cái đầu.</summary>
        public ImageSource Icon { get; private set; }

        public Visibility IconVisibility => Icon == null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InitialVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

        public ToolStatus Status => Tool.PathExists ? ToolStatus.Ready
            : Tool.CanInstall ? ToolStatus.NotInstalled
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

        public string Warning => Tool.PathExists ? null
            : Tool.CanInstall ? "Chưa cài trên máy này — bấm Cài đặt để chạy bộ cài kèm theo"
            : "Không tìm thấy: " + Tool.ResolvedPath;

        public Visibility WarningVisibility => Tool.PathExists ? Visibility.Collapsed : Visibility.Visible;

        public bool CanInstall => Tool.CanInstall;

        public Visibility InstallVisibility => Tool.CanInstall ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RunVisibility => Tool.CanInstall ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Số tag hiện trên dòng; phần còn lại gom thành chip "+n" để dòng không bị dài ra.</summary>
        public const int MaxVisibleTags = 3;

        private List<string> Tags => Tool.Tags ?? new List<string>();

        public IEnumerable<string> VisibleTags => Tags.Take(MaxVisibleTags);

        public Visibility MoreTagsVisibility => Tags.Count > MaxVisibleTags ? Visibility.Visible : Visibility.Collapsed;

        public string MoreTags => Tags.Count > MaxVisibleTags ? "+" + (Tags.Count - MaxVisibleTags) : null;

        /// <summary>Toàn bộ tag, hiện khi rê chuột vào vùng tag.</summary>
        public string AllTagsTip => Tags.Count == 0 ? null : string.Join(", ", Tags);

        public void Replace(ToolEntry tool)
        {
            Tool = tool;
            _state = Probe();
            Icon = LoadIcon();
            OnPropertyChanged(null); // tất cả thuộc tính
        }

        /// <summary>Kiểm lại file (vd sau khi cài app hoặc file bị xoá bên ngoài). Trả về true nếu trạng thái đổi.</summary>
        public bool Refresh()
        {
            var state = Probe();
            if (state == _state)
                return false;
            _state = state;
            Icon = LoadIcon(); // file vừa cài xong thì có icon thật
            OnPropertyChanged(null);
            return true;
        }

        private (bool, bool) Probe() => (Tool.PathExists, Tool.CanInstall);

        private ImageSource LoadIcon() => Tool.PathExists && File.Exists(Tool.ResolvedPath) ? ShellIcon.Load(Tool.ResolvedPath) : null;

        private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
