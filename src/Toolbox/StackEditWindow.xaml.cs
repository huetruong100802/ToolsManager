using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>Một lệnh có thể làm bước: action của tool đã gắn, hoặc lệnh riêng.</summary>
    public sealed class StepTarget
    {
        public string Kind { get; set; }
        public string ActionId { get; set; }
        public string Display { get; set; }
        /// <summary>Null với lệnh riêng.</summary>
        public ActionDef Action { get; set; }
    }

    public sealed class ProfileOption
    {
        /// <summary>Null là dùng tham số mặc định.</summary>
        public Profile Profile { get; set; }
        public string Text { get; set; }
    }

    public sealed class WaitOption
    {
        public string Kind { get; set; }
        public string Text { get; set; }
    }

    /// <summary>Một dòng bước trong trình sửa workflow; chọn lệnh thì danh sách profile/biến thể đổi theo.</summary>
    public sealed class StepEditRow : INotifyPropertyChanged
    {
        public static readonly IReadOnlyList<WaitOption> WaitChoices = new[]
        {
            new WaitOption { Kind = WaitKind.None, Text = "Bật rồi đi tiếp ngay" },
            new WaitOption { Kind = WaitKind.Port, Text = "Chờ cổng mở" },
            new WaitOption { Kind = WaitKind.Exit, Text = "Chờ lệnh thoát (mã 0)" },
        };

        private readonly Workspace _workspace;
        private StepTarget _target;
        private ProfileOption _profile;
        private VariantDef _variant;
        private WaitOption _wait = WaitChoices[0];
        private string _portText = "";
        private string _problem;
        private int _number;
        private IReadOnlyList<ProfileOption> _profiles = new ProfileOption[0];
        private IReadOnlyList<VariantDef> _variants = new VariantDef[0];

        public StepEditRow(Workspace workspace, IReadOnlyList<StepTarget> targets, StackStep step)
        {
            _workspace = workspace;
            Targets = targets;
            if (step == null)
                return;

            // Bước trỏ profile của lệnh riêng: đích là lệnh riêng mà profile tham chiếu (RefId).
            var actionId = step.ActionId;
            if (ProfileKind.IsCustom(step.Kind) && !string.IsNullOrEmpty(step.ProfileId))
                actionId = workspace.Profiles.Find(p => string.Equals(p.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase))?.RefId ?? actionId;
            _target = targets.FirstOrDefault(t => string.Equals(t.Kind, step.Kind, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(t.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            _wait = WaitChoices.FirstOrDefault(w => w.Kind == StackResolver.NormalizeWait(step.Wait)) ?? WaitChoices[0];
            _portText = step.Port > 0 ? step.Port.ToString() : "";
            Rebuild();
            if (_target != null)
            {
                _profile = _profiles.FirstOrDefault(p => p.Profile != null && string.Equals(p.Profile.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase))
                           ?? _profiles.FirstOrDefault();
                _variant = _variants.FirstOrDefault(v => string.Equals(v.Id, step.VariantId, StringComparison.OrdinalIgnoreCase))
                           ?? _variants.FirstOrDefault();
            }
            // Bước hỏng: giữ lý do để người dùng biết cần chọn lại gì.
            _problem = StackResolver.Resolve(workspace, step, 0).Problem;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IReadOnlyList<StepTarget> Targets { get; }

        public IReadOnlyList<WaitOption> WaitOptions => WaitChoices;

        public IReadOnlyList<ProfileOption> Profiles => _profiles;

        public IReadOnlyList<VariantDef> Variants => _variants;

        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                Raise(nameof(Number));
            }
        }

        public StepTarget Target
        {
            get => _target;
            set
            {
                if (ReferenceEquals(_target, value))
                    return;
                _target = value;
                _problem = null;
                Rebuild();
                // Đổi ItemsSource làm ComboBox xả lựa chọn (đẩy null về setter, bị bỏ qua): chọn lại sau khi đã báo danh sách mới.
                Raise(nameof(Profiles));
                Raise(nameof(Variants));
                _profile = _profiles.FirstOrDefault();
                _variant = _variants.FirstOrDefault();
                Raise(nameof(Profile));
                Raise(nameof(Variant));
                Raise(nameof(ProfileVisibility));
                Raise(nameof(VariantVisibility));
                Raise(nameof(Problem));
                Raise(nameof(ProblemVisibility));
            }
        }

        public ProfileOption Profile
        {
            get => _profile;
            set
            {
                if (value == null)
                    return;
                _profile = value;
                _problem = null;
                Raise(nameof(Profile));
                Raise(nameof(VariantVisibility));
                Raise(nameof(Problem));
                Raise(nameof(ProblemVisibility));
            }
        }

        public VariantDef Variant
        {
            get => _variant;
            set
            {
                if (value == null)
                    return;
                _variant = value;
                Raise(nameof(Variant));
            }
        }

        public WaitOption Wait
        {
            get => _wait;
            set
            {
                _wait = value ?? WaitChoices[0];
                Raise(nameof(Wait));
                Raise(nameof(PortVisibility));
            }
        }

        public string PortText
        {
            get => _portText;
            set
            {
                _portText = value ?? "";
                Raise(nameof(PortText));
            }
        }

        public string Problem => _problem;

        public Visibility ProblemVisibility => _problem == null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility ProfileVisibility => _profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Có profile thì biến thể do profile quyết định, không chọn ở đây.</summary>
        public Visibility VariantVisibility =>
            _variants.Count > 1 && (_profile == null || _profile.Profile == null) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility PortVisibility => _wait.Kind == WaitKind.Port ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Bước theo lựa chọn hiện tại; null nếu chưa chọn lệnh hoặc cổng gõ sai (<paramref name="error"/> nói rõ).</summary>
        public StackStep ToStep(out string error)
        {
            error = null;
            if (_target == null)
            {
                error = "chưa chọn lệnh.";
                return null;
            }
            var port = 0;
            if (_portText.Trim().Length > 0)
            {
                if (!PortRules.TryParse(_portText, out port))
                {
                    error = PortRules.Check(_portText);
                    return null;
                }
            }
            var byProfile = _profile?.Profile;
            return new StackStep
            {
                Kind = _target.Kind,
                ActionId = _target.ActionId,
                ProfileId = byProfile?.Id,
                VariantId = byProfile == null && _variants.Count > 0 ? _variant?.Id : null,
                Wait = _wait.Kind == WaitKind.None ? null : _wait.Kind,
                // Cổng chỉ có nghĩa khi chờ cổng.
                Port = _wait.Kind == WaitKind.Port ? port : 0,
            };
        }

        private void Rebuild()
        {
            if (_target != null && _target.Action == null && ProfileKind.IsCustom(_target.Kind))
            {
                // Lệnh riêng: không có action; profile chọn được là profile cùng loại trỏ tới đúng tool/lệnh này.
                var refOptions = new List<ProfileOption> { new ProfileOption { Text = "Tham số mặc định" } };
                refOptions.AddRange(_workspace.Profiles
                    .Where(p => string.Equals(p.Kind, _target.Kind, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(p.RefId, _target.ActionId, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new ProfileOption { Profile = p, Text = p.Label }));
                _profiles = refOptions;
                _variants = new VariantDef[0];
                return;
            }
            if (_target?.Action == null)
            {
                _profiles = new ProfileOption[0];
                _variants = new VariantDef[0];
                return;
            }
            var options = new List<ProfileOption> { new ProfileOption { Text = "Tham số mặc định" } };
            options.AddRange(_workspace.Profiles
                .Where(p => string.Equals(p.Kind, _target.Kind, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(p.ActionId, _target.ActionId, StringComparison.OrdinalIgnoreCase))
                .Select(p => new ProfileOption { Profile = p, Text = p.Label }));
            _profiles = options;
            _variants = _target.Action.Variants.ToList();
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Thêm hoặc sửa một workflow của workspace.</summary>
    public partial class StackEditWindow : Window
    {
        private readonly Workspace _workspace;
        private readonly StackDef _existing;
        private readonly List<StepTarget> _targets = new List<StepTarget>();
        private readonly ObservableCollection<StepEditRow> _rows = new ObservableCollection<StepEditRow>();

        /// <param name="existing">Có giá trị thì là sửa (giữ nguyên Id); null là thêm mới.</param>
        public StackEditWindow(Workspace workspace, StackDef existing = null)
        {
            InitializeComponent();
            _workspace = workspace;
            _existing = existing;
            BuildTargets();
            StepList.ItemsSource = _rows;
            _rows.CollectionChanged += (s, e) => UpdateState();

            if (existing != null)
            {
                Title = "Sửa workflow";
                LabelBox.Text = existing.Label;
                foreach (var step in existing.Steps)
                    _rows.Add(new StepEditRow(workspace, _targets, step));
            }
            Renumber();
            Loaded += (s, e) => LabelBox.Focus();
            UpdateState();
        }

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
                    Dialogs.Error(this, $"Bước {row.Number}: {error}");
                    return;
                }
                // Cùng bộ kiểm với lúc chạy: tham số hợp lệ, chờ cổng thì phải biết cổng.
                var resolved = StackResolver.Resolve(_workspace, step, row.Number - 1);
                if (resolved.IsBroken)
                {
                    Dialogs.Error(this, $"Bước {row.Number} \"{resolved.Label}\": {resolved.Problem}");
                    return;
                }
                steps.Add(step);
            }

            Result = new StackDef { Id = _existing?.Id, Label = LabelBox.Text.Trim(), Steps = steps };
            DialogResult = true;
        }
    }
}
