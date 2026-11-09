using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Toolbox.Views
{
    /// <summary>
    /// Ô tìm kiếm dùng chung (kính lúp + chữ gợi ý). Esc xoá chữ. <see cref="Attach"/> lọc một danh sách theo chữ đã gõ:
    /// không phân biệt hoa thường và dấu, khớp bất kỳ đâu trong chuỗi do bên gọi cung cấp.
    /// </summary>
    public partial class SearchField : UserControl
    {
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(SearchField), new PropertyMetadata("Tìm…", (d, e) => ((SearchField)d).ShowPlaceholder()));

        public SearchField()
        {
            InitializeComponent();
            ShowPlaceholder();
        }

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public string Text
        {
            get => Box.Text;
            set => Box.Text = value ?? "";
        }

        public event EventHandler TextChanged;

        public new bool Focus() => Box.Focus();

        private void ShowPlaceholder()
        {
            if (PlaceholderText == null)
                return;
            PlaceholderText.Text = Placeholder;
            PlaceholderText.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnBoxChanged(object sender, TextChangedEventArgs e)
        {
            ShowPlaceholder();
            TextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            // Esc chỉ xoá chữ khi có chữ; ô trống thì để Esc đóng hộp thoại như thường.
            if (e.Key == Key.Escape && Box.Text.Length > 0)
            {
                Box.Clear();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Lọc <paramref name="items"/> theo chữ trong ô; <paramref name="haystack"/> trả chuỗi để khớp của mỗi mục.
        /// Trả về view đang lọc (null nếu nguồn không có view, vd. đang rỗng).
        /// </summary>
        public ICollectionView Attach(System.Collections.IEnumerable items, Func<object, string> haystack, Action afterFilter = null)
        {
            var view = CollectionViewSource.GetDefaultView(items);
            if (view == null)
                return null;
            view.Filter = o =>
            {
                var query = Core.Text.Fold((Text ?? "").Trim());
                return query.Length == 0 || Core.Text.Fold(haystack(o) ?? "").Contains(query);
            };
            TextChanged += (s, e) =>
            {
                view.Refresh();
                afterFilter?.Invoke();
            };
            return view;
        }
    }
}
