using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Toolbox
{
    /// <summary>Một dòng trong <see cref="CheckListWindow"/>. Key để tìm lại dòng sau khi người dùng tick.</summary>
    public sealed class CheckItem
    {
        public CheckItem(string key, string title, string detail, bool isChecked, bool isEnabled = true)
        {
            Key = key;
            Title = title;
            Detail = detail;
            IsChecked = isChecked;
            IsEnabled = isEnabled;
        }

        public string Key { get; }
        public string Title { get; }
        public string Detail { get; }
        public bool IsChecked { get; set; }
        public bool IsEnabled { get; }
    }

    /// <summary>Hộp thoại chọn nhiều mục: dùng để gắn tool vào workspace hoặc chọn workspace cho tool.</summary>
    public partial class CheckListWindow : Window
    {
        public CheckListWindow(string title, string hint, string okText, IEnumerable<CheckItem> items, string emptyText)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            HintText.Text = hint;
            OkButton.Content = okText;

            Items = items.ToList();
            ItemList.ItemsSource = Items;
            Search.Attach(Items, o => ((CheckItem)o).Title + " " + ((CheckItem)o).Detail);
            if (Items.Count == 0)
            {
                Search.Visibility = Visibility.Collapsed;
                EmptyText.Text = emptyText;
                EmptyText.Visibility = Visibility.Visible;
                ItemList.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = false;
            }
        }

        /// <summary>Mọi mục, kèm trạng thái tick sau khi người dùng bấm lưu.</summary>
        public IReadOnlyList<CheckItem> Items { get; }

        private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
