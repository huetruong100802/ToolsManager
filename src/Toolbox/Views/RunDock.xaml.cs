using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Một chip trên dock: tên, cổng, và các nút Mở / Log / Dừng (hoặc Đóng nếu là chip lỗi).</summary>
    public sealed class DockChipRow
    {
        public DockChipRow(DockChip chip)
        {
            Chip = chip;
        }

        public DockChip Chip { get; }
        public TaskInfo Task => Chip.Task;
        public bool Failed => Chip.Failed;
        public string Title => Failed ? "Lỗi · " + Chip.Title : Chip.Title;
        public string PortText => Chip.Port.HasValue ? ":" + Chip.Port.Value : "";
        public Visibility OpenVisibility => Chip.Port.HasValue && !Failed ? Visibility.Visible : Visibility.Collapsed;
        public string LastActionText => Failed ? "Đóng" : "Dừng";
        public string Key => Chip.Task.Id + (Failed ? "!" : "");
    }

    /// <summary>
    /// Dock tác vụ ở đáy cửa sổ thay dòng trạng thái: mỗi tác vụ đang chạy là một chip, tác vụ vừa lỗi giữ chip đỏ tới khi xem.
    /// Quá <see cref="DockChips.MaxShown"/> chip thì phần còn lại gom ở "+n nữa".
    /// </summary>
    public partial class RunDock : UserControl
    {
        private readonly SeenFailures _seen = new SeenFailures();
        private List<DockChipRow> _rows = new List<DockChipRow>();
        private IReadOnlyList<TaskInfo> _last = new List<TaskInfo>();

        public RunDock()
        {
            InitializeComponent();
        }

        public event Action<TaskInfo> LogRequested;
        public event Action<TaskInfo> StopRequested;
        public event Action<int> OpenPortRequested;
        public event Action AllRequested;

        /// <summary>Các chip đang hiện (để test).</summary>
        public IReadOnlyList<DockChipRow> Rows => _rows;

        /// <summary>Đánh dấu lỗi của tác vụ này đã xem (vd. mở log ở nơi khác) rồi vẽ lại.</summary>
        public void MarkSeen(string taskId)
        {
            _seen.Mark(taskId);
            Update(_last);
        }

        public void Update(IReadOnlyList<TaskInfo> tasks)
        {
            _last = tasks;
            var chips = DockChips.Build(tasks, _seen, DateTime.UtcNow);
            var rows = chips.Take(DockChips.MaxShown).Select(c => new DockChipRow(c)).ToList();
            if (!rows.Select(r => r.Key).SequenceEqual(_rows.Select(r => r.Key)))
            {
                _rows = rows;
                Chips.ItemsSource = null;
                Chips.ItemsSource = _rows;
            }
            var running = chips.Count(c => !c.Failed);
            var hidden = chips.Count - rows.Count;
            Idle.Visibility = chips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Summary.Text = (hidden > 0 ? $"+{hidden} nữa" : "") + (hidden > 0 && running > 0 ? " · " : "") + (running > 0 ? $"{running} đang chạy" : "");
        }

        private static DockChipRow RowOf(object sender) => (sender as FrameworkElement)?.Tag as DockChipRow;

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row?.Chip.Port is int port)
                OpenPortRequested?.Invoke(port);
        }

        private void OnLog(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null)
                return;
            _seen.Mark(row.Task.Id);
            LogRequested?.Invoke(row.Task);
            Update(_last);
        }

        /// <summary>Nút cuối: Dừng với tác vụ đang chạy, Đóng (đã xem) với chip lỗi.</summary>
        private void OnLast(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null)
                return;
            if (row.Failed)
            {
                _seen.Mark(row.Task.Id);
                Update(_last);
            }
            else
                StopRequested?.Invoke(row.Task);
        }

        private void OnAll(object sender, RoutedEventArgs e) => AllRequested?.Invoke();
    }
}
