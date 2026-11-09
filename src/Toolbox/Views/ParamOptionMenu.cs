using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Toolbox.Views
{
    /// <summary>
    /// Danh sách gợi ý của một ô tham số, mở từ nút ▾ cạnh ô nhập: chọn mục nào thì điền vào ô đó, vẫn gõ tay được.
    /// Nguồn gợi ý do catalog khai báo, nên giao diện không biết tên project hay script nào.
    /// Danh sách dài (hơn <see cref="SearchThreshold"/> mục) mở dạng hộp có ô tìm kiếm; ngắn thì là menu thường.
    /// </summary>
    public static class ParamOptionMenu
    {
        public const int SearchThreshold = 8;

        private sealed class Option
        {
            public string Value { get; set; }
            public string Label => Value.Length == 0 ? "(để trống)" : Value;
        }

        /// <summary>Mở menu cho dòng tham số của nút (DataContext là <see cref="ParamRow"/>). Không có gợi ý thì không làm gì.</summary>
        public static void Open(Button button)
        {
            if (!(button.DataContext is ParamRow row) || row.Options.Count == 0)
                return;
            Open(button, row.Options, v => row.Value = v, null);
        }

        /// <summary>
        /// Mở menu cho một danh sách giá trị bất kỳ. <paramref name="browse"/> (nếu có) là nút "Chọn file…": trả đường dẫn đã chọn, hoặc null nếu huỷ.
        /// </summary>
        public static void Open(Button button, System.Collections.Generic.IReadOnlyList<string> values, Action<string> pick, Func<string> browse)
        {
            if (values.Count == 0 && browse == null)
                return;

            if (values.Count > SearchThreshold)
            {
                OpenSearchable(button, values, pick, browse);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            if (browse != null)
            {
                var browseItem = new MenuItem { Header = "Chọn file…" };
                browseItem.Click += (s, e) => { var chosen = browse(); if (chosen != null) pick(chosen); };
                menu.Items.Add(browseItem);
                if (values.Count > 0)
                    menu.Items.Add(new Separator());
            }
            foreach (var option in values)
            {
                var item = new MenuItem { Header = option.Length == 0 ? "(để trống)" : option };
                item.Click += (s, e) => pick(option);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private static void OpenSearchable(Button button, System.Collections.Generic.IReadOnlyList<string> values, Action<string> pick, Func<string> browse)
        {
            var options = values.Select(o => new Option { Value = o }).ToList();
            var search = new SearchField { Placeholder = "Tìm…", Margin = new Thickness(0, 0, 0, 6) };
            var list = new ListBox { DisplayMemberPath = nameof(Option.Label), ItemsSource = options, MaxHeight = 240, BorderThickness = new Thickness(0) };
            search.Attach(options, o => ((Option)o).Value);

            var panel = new DockPanel { Width = 320 };
            Popup popupRef = null;
            if (browse != null)
            {
                var browseButton = new Button { Content = "Chọn file…", Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
                browseButton.Click += (s, e) =>
                {
                    popupRef.IsOpen = false;
                    var chosen = browse();
                    if (chosen != null)
                        pick(chosen);
                };
                DockPanel.SetDock(browseButton, Dock.Top);
                panel.Children.Add(browseButton);
            }
            DockPanel.SetDock(search, Dock.Top);
            panel.Children.Add(search);
            panel.Children.Add(list);
            var popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = (System.Windows.Media.Brush)Application.Current.Resources["Surface"],
                    BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["Line"],
                    Child = panel,
                },
            };

            popupRef = popup;
            // Chọn bằng chuột hoặc Enter; mũi tên xuống từ ô tìm kiếm nhảy vào danh sách.
            list.SelectionChanged += (s, e) =>
            {
                if (list.SelectedItem is Option picked)
                {
                    popup.IsOpen = false;
                    pick(picked.Value);
                }
            };
            search.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down && list.Items.Count > 0)
                {
                    list.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && list.Items.Count == 1)
                {
                    list.SelectedIndex = 0;
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                }
            };
            popup.Opened += (s, e) => search.Focus();
            popup.IsOpen = true;
        }
    }
}
