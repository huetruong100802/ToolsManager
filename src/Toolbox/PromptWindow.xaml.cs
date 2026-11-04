using System.Windows;
using System.Windows.Controls;

namespace Toolbox
{
    /// <summary>Hộp thoại nhập một dòng chữ (tên profile…). Không cho lưu khi để trống.</summary>
    public partial class PromptWindow : Window
    {
        public PromptWindow(string title, string prompt, string initial = "", string note = null)
        {
            InitializeComponent();
            Title = title;
            Prompt.Text = prompt;
            ValueBox.Text = initial ?? "";
            if (!string.IsNullOrEmpty(note))
            {
                Note.Text = note;
                Note.Visibility = Visibility.Visible;
            }
            Loaded += (s, e) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
            UpdateEnabled();
        }

        public string Value { get; private set; }

        private void OnValueChanged(object sender, TextChangedEventArgs e) => UpdateEnabled();

        private void UpdateEnabled() => OkButton.IsEnabled = ValueBox.Text.Trim().Length > 0;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Value = ValueBox.Text.Trim();
            DialogResult = true;
        }
    }
}
