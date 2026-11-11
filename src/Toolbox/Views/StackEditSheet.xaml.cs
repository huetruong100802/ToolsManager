using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Thêm hoặc sửa một workflow của workspace.</summary>
    public partial class StackEditSheet : SheetBase
    {
        private readonly Workspace _workspace;
        private readonly StackDef _existing;
        private readonly List<StepTarget> _targets = new List<StepTarget>();
        private readonly ObservableCollection<StepEditRow> _rows = new ObservableCollection<StepEditRow>();

        /// <param name="existing">Có giá trị thì là sửa (giữ nguyên Id); null là thêm mới.</param>
        public StackEditSheet(Workspace workspace, StackDef existing = null)
        {
            InitializeComponent();
            _workspace = workspace;
            _existing = existing;
            SheetTitle = existing == null ? "Thêm workflow" : "Sửa workflow";
            BuildTargets();
            StepList.ItemsSource = _rows;
            _rows.CollectionChanged += (s, e) => UpdateState();

            if (existing != null)
            {
                LabelBox.Text = existing.Label;
                foreach (var step in existing.Steps)
                    _rows.Add(new StepEditRow(workspace, _targets, step));
            }
            Renumber();
            Loaded += (s, e) => LabelBox.Focus();
            UpdateState();
        }

        public override double SheetWidth => 640;

        private void OnCancel(object sender, RoutedEventArgs e) => Close();

        /// <summary>Workflow sau khi lưu; Id giữ nguyên khi sửa, rỗng khi thêm mới (người gọi cấp).</summary>
        public StackDef Result { get; private set; }

        private void BuildTargets()
        {
            foreach (var binding in _workspace.Bindings)
            {
                var tool = ToolCatalog.Find(binding.ToolId);
                if (tool == null)
                    continue;
                foreach (var action in ToolCatalog.ActionsFor(_workspace, binding))
                    _targets.Add(new StepTarget { Kind = tool.Kind, ActionId = action.Id, Action = action, Display = $"{tool.Name} · {action.Label}" });
            }
            foreach (var command in _workspace.Customs)
                _targets.Add(new StepTarget { Kind = TaskKey.Custom, ActionId = command.Id, Display = "Lệnh riêng · " + command.Label });
        }

        private void Renumber()
        {
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].Number = i + 1;
        }

        private void UpdateState()
        {
            // Có thể gọi trong InitializeComponent khi các phần tử chưa dựng xong.
            if (OkButton == null || NoSteps == null)
                return;
            NoSteps.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoTargets.Visibility = _targets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AddStepButton.IsEnabled = _targets.Count > 0;
            OkButton.IsEnabled = LabelBox.Text.Trim().Length > 0 && _rows.Count > 0;
        }

        private void OnLabelChanged(object sender, TextChangedEventArgs e) => UpdateState();

        private void OnAddStep(object sender, RoutedEventArgs e)
        {
            _rows.Add(new StepEditRow(_workspace, _targets, null) { Number = _rows.Count + 1 });
        }

        private StepEditRow RowOf(object sender) => (sender as FrameworkElement)?.DataContext as StepEditRow;

        private void OnMoveUp(object sender, RoutedEventArgs e) => Move(RowOf(sender), -1);

        private void OnMoveDown(object sender, RoutedEventArgs e) => Move(RowOf(sender), 1);

        private void Move(StepEditRow row, int delta)
        {
            var from = row == null ? -1 : _rows.IndexOf(row);
            var to = from + delta;
            if (from < 0 || to < 0 || to >= _rows.Count)
                return;
            _rows.Move(from, to);
            Renumber();
        }

        private void OnRemoveStep(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null)
                return;
            _rows.Remove(row);
            Renumber();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var steps = new List<StackStep>();
            foreach (var row in _rows)
            {
                var step = row.ToStep(out var error);
                if (step == null)
                {
                    Dialogs.Error(Host, $"Bước {row.Number}: {error}");
                    return;
                }
                // Cùng bộ kiểm với lúc chạy: tham số hợp lệ, chờ cổng thì phải biết cổng.
                var resolved = StackResolver.Resolve(_workspace, step, row.Number - 1);
                if (resolved.IsBroken)
                {
                    Dialogs.Error(Host, $"Bước {row.Number} \"{resolved.Label}\": {resolved.Problem}");
                    return;
                }
                steps.Add(step);
            }

            Result = new StackDef { Id = _existing?.Id, Label = LabelBox.Text.Trim(), Steps = steps };
            DialogResult = true;
        }
    }
}
