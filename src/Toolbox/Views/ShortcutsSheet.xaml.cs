using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toolbox.Views
{
    /// <summary>Một dòng trong bảng phím tắt.</summary>
    public sealed class ShortcutInfo
    {
        public ShortcutInfo(string keys, string description)
        {
            Keys = keys;
            Description = description;
        }

        public string Keys { get; }
        public string Description { get; }
    }

    /// <summary>Bảng phím tắt mở bằng F1. Danh sách <see cref="All"/> là nguồn duy nhất, test đối chiếu với phím thật của cửa sổ chính.</summary>
    public partial class ShortcutsSheet : UserControl
    {
        public static readonly IReadOnlyList<ShortcutInfo> All = new[]
        {
            new ShortcutInfo("Ctrl+K", "Tìm và chạy mọi thứ (profile, workflow, tool, workspace)"),
            new ShortcutInfo("Ctrl+F", "Tìm trong trang hiện tại (Thư viện)"),
            new ShortcutInfo("Ctrl+1 … 4", "Trang chủ, Thư viện, Tác vụ, Agent"),
            new ShortcutInfo("Ctrl+Tab", "Tab kế tiếp trong workspace (Ctrl+Shift+Tab: tab trước)"),
            new ShortcutInfo("F5", "Chạy mục đang chọn (dòng đang có con trỏ bàn phím)"),
            new ShortcutInfo("Shift+F5", "Dừng mục đang chọn"),
            new ShortcutInfo("Enter", "Trên dòng tool ở Thư viện: chạy"),
            new ShortcutInfo("Esc", "Đóng bảng, panel log, hoặc xoá ô tìm"),
            new ShortcutInfo("F1", "Mở bảng phím tắt này"),
        };

        public ShortcutsSheet()
        {
            InitializeComponent();
            List.ItemsSource = All;
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public void Open() => Visibility = Visibility.Visible;

        public void Close() => Visibility = Visibility.Collapsed;

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();

        private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }
}
