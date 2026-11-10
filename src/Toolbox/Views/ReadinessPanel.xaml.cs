using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    public sealed class ReadinessRow
    {
        public ReadinessRow(ReadinessIssue issue)
        {
            Issue = issue;
        }

        public ReadinessIssue Issue { get; }
        public string Title => Issue.Title;
        public string Detail => Issue.Detail;
        public string FixText => Issue.FixText;
        public Visibility FixVisibility => Issue.Fix == ReadinessFix.None ? Visibility.Collapsed : Visibility.Visible;
        public string Glyph => Issue.Level == ReadinessLevel.Error ? "" : "";
        public Brush GlyphBrush => (Brush)Application.Current.FindResource(Issue.Level == ReadinessLevel.Error ? "Danger" : "WarnInk");
    }

    /// <summary>
    /// Khung "Chưa sẵn sàng" ở đầu tab Chạy: liệt kê thứ máy này còn thiếu (thư mục, tool, lệnh, profile hỏng) kèm nút sửa.
    /// Tự ẩn khi workspace đã sẵn sàng.
    /// </summary>
    public partial class ReadinessPanel : UserControl
    {
        public ReadinessPanel()
        {
            InitializeComponent();
        }

        public event Action<ReadinessIssue> FixRequested;

        public int Count { get; private set; }

        public void Show(IReadOnlyList<ReadinessIssue> issues)
        {
            Count = issues?.Count ?? 0;
            Visibility = Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (Count == 0)
                return;
            var errors = issues.Count(i => i.Level == ReadinessLevel.Error);
            Heading.Text = errors > 0 ? $"Chưa sẵn sàng: {Count} việc cần làm ({errors} chặn việc chạy)" : $"Gần sẵn sàng: {Count} việc nên kiểm";
            List.ItemsSource = issues.OrderByDescending(i => i.Level).Select(i => new ReadinessRow(i)).ToList();
        }

        private void OnFix(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ReadinessIssue issue)
                FixRequested?.Invoke(issue);
        }
    }
}
