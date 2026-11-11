using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Sửa danh sách cặp tham số của một profile đã lưu. Chỉ dựng <see cref="Result"/>; bên gọi ghi vào workspace.</summary>
    public partial class PairsSheet : SheetBase
    {
        public override double SheetWidth => 540;

        public PairsSheet(string profileLabel, IEnumerable<ParamPair> initial, IEnumerable<CapabilityEntry> suggestions, string note)
        {
            InitializeComponent();
            SheetTitle = "Tham số của profile";
            Heading.Text = "Tham số của \"" + profileLabel + "\"";
            Note.Text = note ?? "";
            Editor.SetSuggestions(suggestions);
            Editor.Load(initial);
            OnChanged(null, EventArgs.Empty);
        }

        /// <summary>Danh sách cặp sau khi sửa (rỗng nếu xoá hết).</summary>
        public List<ParamPair> Result { get; private set; }

        private void OnChanged(object sender, EventArgs e) => OkButton.IsEnabled = Editor.Error == null;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Editor.Error != null)
                return;
            Result = Editor.GetPairs();
            DialogResult = true;
        }
    }
}
