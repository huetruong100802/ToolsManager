using System;
using System.Windows;
using System.Windows.Controls;

namespace Toolbox.Views
{
    /// <summary>
    /// Trạng thái trống dùng chung: biểu tượng, tiêu đề, lời nhắc và các nút việc tiếp theo (mỗi trạng thái trống có một việc để làm).
    /// Khai báo Glyph, Title, Body trong XAML rồi gọi <see cref="AddAction"/> để thêm nút; nút đầu là nút chính.
    /// </summary>
    public partial class EmptyState : UserControl
    {
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", (d, e) => ((EmptyState)d).GlyphText.Text = (string)e.NewValue));
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", (d, e) => ((EmptyState)d).TitleText.Text = (string)e.NewValue));
        public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", (d, e) => ((EmptyState)d).BodyText.Text = (string)e.NewValue));

        public EmptyState()
        {
            InitializeComponent();
            GlyphText.Text = Glyph;
        }

        public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Body { get => (string)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

        /// <summary>Thêm một nút hành động; nút đầu tiên là nút chính.</summary>
        public Button AddAction(string text, Action onClick)
        {
            var primary = Actions.Children.Count == 0;
            var button = new Button
            {
                Content = text,
                Margin = new Thickness(primary ? 0 : 8, 0, 0, 0),
                Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"),
            };
            button.Click += (s, e) => onClick();
            Actions.Children.Add(button);
            return button;
        }
    }
}
