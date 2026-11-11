using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Toolbox
{
    /// <summary>
    /// Hộp xem trước "người nhận sẽ có gì" dùng chung cho nút Chia sẻ ở Thư viện và ở workspace.
    /// Bấm "Chia sẻ…" thì bên gọi mới hỏi nơi lưu file.
    /// </summary>
    public partial class ShareWindow : Window
    {
        public ShareWindow(string header, IEnumerable<string> lines, IEnumerable<string> warnings)
        {
            InitializeComponent();
            HeaderText.Text = header;
            Lines.Text = string.Join("\n", lines ?? Enumerable.Empty<string>());
            var warn = (warnings ?? Enumerable.Empty<string>()).ToList();
            WarningBox.Visibility = warn.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Warnings.Text = string.Join("\n", warn.Select(w => "⚠ " + w));
        }

        private void OnShare(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
