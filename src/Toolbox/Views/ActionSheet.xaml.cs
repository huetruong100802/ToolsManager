using System.Windows;

namespace Toolbox.Views
{
    /// <summary>
    /// Tham số của một lệnh trong tab Chạy: biến thể, ô giá trị, cặp tham số thêm. Dùng chung <see cref="ActionRow"/>
    /// với danh sách nên giá trị nhập ở đây là giá trị lần chạy kế tiếp. Chỉ chọn việc cần làm (<see cref="Outcome"/>);
    /// bên gọi chạy hoặc ghim.
    /// </summary>
    public partial class ActionSheet : SheetBase
    {
        public const string RunOutcome = "run";
        public const string PinOutcome = "pin";

        private readonly ActionRow _row;

        public override double SheetWidth => 520;

        public ActionSheet(ActionRow row)
        {
            InitializeComponent();
            _row = row;
            DataContext = row;
            SheetTitle = "Tham số của lệnh";
            Heading.Text = row.Title;
            Note.Text = row.Tool.Name;
            // PairEditor là điều khiển của dòng, chỉ gắn vào một cây hiển thị tại một thời điểm: gỡ khi sheet đóng.
            EditorHost.Content = row.Editor;
            Unloaded += (s, e) => EditorHost.Content = null;
        }

        /// <summary>"run" hoặc "pin" sau khi xác nhận.</summary>
        public string Outcome { get; private set; }

        private void OnPickOptions(object sender, RoutedEventArgs e) =>
            ParamOptionMenu.Open((System.Windows.Controls.Button)sender);

        private void OnRun(object sender, RoutedEventArgs e)
        {
            // Giữ sheet mở để lỗi dưới từng ô còn thấy được.
            if (_row.Revalidate())
                return;
            Outcome = RunOutcome;
            DialogResult = true;
        }

        private void OnPin(object sender, RoutedEventArgs e)
        {
            Outcome = PinOutcome;
            DialogResult = true;
        }
    }
}
