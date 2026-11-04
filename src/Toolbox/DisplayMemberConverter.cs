using System;
using System.Globalization;
using System.Windows.Data;

namespace Toolbox
{
    /// <summary>
    /// Hiển thị ô chọn của ComboBox theo <c>DisplayMemberPath</c>. Template tự dựng của ComboBox (App.xaml) không áp
    /// DisplayMemberPath cho ô chọn nên sẽ hiện tên kiểu (vd "Toolbox.Core.Workspaces.ToolDef").
    /// values[0] = mục đang chọn, values[1] = DisplayMemberPath của ComboBox (có thể dạng "A.B").
    /// </summary>
    public sealed class DisplayMemberConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var item = values.Length > 0 ? values[0] : null;
            var path = values.Length > 1 ? values[1] as string : null;
            if (item == null || string.IsNullOrEmpty(path))
                return item;

            object current = item;
            foreach (var part in path.Split('.'))
            {
                if (current == null)
                    return null;
                var property = current.GetType().GetProperty(part);
                if (property == null)
                    return item;
                current = property.GetValue(current, null);
            }
            return current;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
