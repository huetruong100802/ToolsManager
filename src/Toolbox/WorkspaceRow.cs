using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>Một dòng workspace trên rail: tên, chấm màu, cảnh báo thư mục mất, số tác vụ đang chạy.</summary>
    public sealed class WorkspaceRow : INotifyPropertyChanged
    {
        // Thứ tự khớp Workspace.ColorIndex; đủ Workspace.ColorCount màu.
        private static readonly Brush[] Palette = Build(
            "#4F46E5", "#0D9488", "#D97706", "#E11D48", "#0284C7", "#7C3AED", "#16A34A", "#EA580C");

        private bool _isMissing;
        private bool _isSelected;
        private int _running;

        public WorkspaceRow(Workspace workspace)
        {
            Workspace = workspace;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Workspace Workspace { get; }

        public string Name => Workspace.Name;

        public string Path => Workspace.Path;

        public Brush ColorBrush => Palette[System.Math.Abs(Workspace.ColorIndex) % Palette.Length];

        /// <summary>Đang là trang hiện tại; <c>MainWindow</c> đặt, rail đọc để tô sáng.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                Raise(nameof(IsSelected));
            }
        }

        public bool IsMissing => _isMissing;

        public int RunningCount => _running;

        public Visibility MissingVisibility => _isMissing ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RunningVisibility => _running > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string Tip => _isMissing ? Path + "\nKhông tìm thấy thư mục này." : Path;

        /// <summary>Đọc lại trạng thái thư mục và số tác vụ đang chạy; báo giao diện nếu có đổi.</summary>
        public void Refresh(int running)
        {
            _isMissing = !Workspace.PathExists;
            _running = running;
            Raise(nameof(Name));
            Raise(nameof(Path));
            Raise(nameof(IsMissing));
            Raise(nameof(MissingVisibility));
            Raise(nameof(RunningCount));
            Raise(nameof(RunningVisibility));
            Raise(nameof(Tip));
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static Brush[] Build(params string[] hex)
        {
            var brushes = new Brush[hex.Length];
            for (var i = 0; i < hex.Length; i++)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex[i]));
                brush.Freeze();
                brushes[i] = brush;
            }
            return brushes;
        }
    }
}
