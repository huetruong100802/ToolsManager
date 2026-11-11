using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toolbox.Core;

namespace Toolbox.Views
{
    /// <summary>Một dòng kết quả của bảng lệnh.</summary>
    public sealed class PaletteRow
    {
        public PaletteRow(SearchEntry entry)
        {
            Entry = entry;
        }

        public SearchEntry Entry { get; }

        public string Glyph
        {
            get
            {
                switch (Entry.Kind)
                {
                    case SearchKind.Profile: return "";
                    case SearchKind.Workflow: return "";
                    case SearchKind.Custom: return "";
                    case SearchKind.Tool: return "";
                    case SearchKind.Workspace: return "";
                    default: return "";
                }
            }
        }

        /// <summary>Việc Enter sẽ làm với dòng này.</summary>
        public string Badge => Entry.Kind == SearchKind.Action ? "Hành động" : Entry.Runnable ? "Chạy" : "Mở";
    }

    /// <summary>
    /// Bảng lệnh Ctrl+K toàn app: gõ vài chữ (không dấu cũng được) rồi Enter để chạy profile, workflow, lệnh riêng,
    /// hoặc mở tool, workspace, hành động. Ô trống hiện các mục dùng gần đây. Ctrl+Enter chỉ mở nơi chứa mục đó mà không chạy.
    /// </summary>
    public partial class CommandPalette : UserControl
    {
        private Func<SearchIndex> _index;
        private UsageStore _usage = UsageStore.Shared;

        public CommandPalette()
        {
            InitializeComponent();
        }

        /// <summary>Enter trên mục chạy được: chạy; trên mục khác: mở.</summary>
        public event Action<SearchEntry> Activated;

        /// <summary>Ctrl+Enter: mở nơi chứa mục, không chạy.</summary>
        public event Action<SearchEntry> RevealRequested;

        /// <param name="index">Dựng chỉ mục mới mỗi lần mở (dữ liệu có thể đã đổi, agent có thể vừa thêm profile).</param>
        public void Initialize(Func<SearchIndex> index, UsageStore usage = null)
        {
            _index = index;
            _usage = usage ?? UsageStore.Shared;
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public SearchEntry Selected => (Results.SelectedItem as PaletteRow)?.Entry;

        public IReadOnlyList<SearchEntry> Shown => Results.Items.Cast<PaletteRow>().Select(r => r.Entry).ToList();

        public void Open()
        {
            Visibility = Visibility.Visible;
            Input.Text = "";
            Refresh();
            Input.Focus();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            Visibility = Visibility.Collapsed;
            Input.Text = "";
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>Đặt truy vấn từ ngoài (test, hoặc mở sẵn với chữ gợi ý).</summary>
        public void SetQuery(string text) => Input.Text = text;

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            Hint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (IsOpen)
                Refresh();
        }

        private void Refresh()
        {
            var hits = _index?.Invoke()?.Query(Input.Text, _usage) ?? new List<SearchEntry>();
            Results.ItemsSource = hits.Select(h => new PaletteRow(h)).ToList();
            NoResults.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (hits.Count > 0)
                Results.SelectedIndex = 0;
        }

        private void Move(int delta)
        {
            var count = Results.Items.Count;
            if (count == 0)
                return;
            Results.SelectedIndex = ((Results.SelectedIndex < 0 ? 0 : Results.SelectedIndex) + delta + count) % count;
            Results.ScrollIntoView(Results.SelectedItem);
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Commit((Keyboard.Modifiers & ModifierKeys.Control) != 0);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Chọn mục đang chọn: chạy/mở, hoặc chỉ mở nơi chứa nếu <paramref name="reveal"/>.</summary>
        public void Commit(bool reveal)
        {
            var entry = Selected;
            if (entry == null)
                return;
            Close();
            if (reveal)
                RevealRequested?.Invoke(entry);
            else
                Activated?.Invoke(entry);
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Selected != null)
                Commit(false);
        }

        private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();

        private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }
}
