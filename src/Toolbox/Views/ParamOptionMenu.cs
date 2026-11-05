using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Toolbox.Views
{
    /// <summary>
    /// Danh sách gợi ý của một ô tham số, mở từ nút ▾ cạnh ô nhập: chọn mục nào thì điền vào ô đó, vẫn gõ tay được.
    /// Nguồn gợi ý do catalog khai báo, nên giao diện không biết tên project hay script nào.
    /// </summary>
    public static class ParamOptionMenu
    {
        /// <summary>Mở menu cho dòng tham số của nút (DataContext là <see cref="ParamRow"/>). Không có gợi ý thì không làm gì.</summary>
        public static void Open(Button button)
        {
            if (!(button.DataContext is ParamRow row) || row.Options.Count == 0)
                return;

            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var option in row.Options)
            {
                var item = new MenuItem { Header = option.Length == 0 ? "(để trống)" : option };
                item.Click += (s, e) => row.Value = option;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }
    }
}
