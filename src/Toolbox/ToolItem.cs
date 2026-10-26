using System.ComponentModel;
using System.Windows;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>Một dòng trong danh sách: tool + trạng thái chọn để đóng gói/xoá.</summary>
    public sealed class ToolItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        private (bool Exists, bool CanInstall) _state;

        public ToolItem(ToolEntry tool)
        {
            Tool = tool;
            _state = Probe();
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

        public string Warning => Tool.PathExists ? null
            : Tool.CanInstall ? "Chưa cài trên máy này — bấm Cài đặt để chạy bộ cài kèm theo"
            : "Không tìm thấy: " + Tool.ResolvedPath;

        public Visibility WarningVisibility => Tool.PathExists ? Visibility.Collapsed : Visibility.Visible;

        public bool CanInstall => Tool.CanInstall;

        public Visibility InstallVisibility => Tool.CanInstall ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RunVisibility => Tool.CanInstall ? Visibility.Collapsed : Visibility.Visible;

        public void Replace(ToolEntry tool)
        {
            Tool = tool;
            _state = Probe();
            OnPropertyChanged(null); // tất cả thuộc tính
        }

        /// <summary>Kiểm lại file (vd sau khi cài app hoặc file bị xoá bên ngoài). Trả về true nếu trạng thái đổi.</summary>
        public bool Refresh()
        {
            var state = Probe();
            if (state == _state)
                return false;
            _state = state;
            OnPropertyChanged(null);
            return true;
        }

        private (bool, bool) Probe() => (Tool.PathExists, Tool.CanInstall);

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
