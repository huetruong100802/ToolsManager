using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Views;

namespace Toolbox
{
    /// <summary>
    /// Hộp thoại tạo profile ba bước: chọn tool (dựng sẵn, thư viện hoặc lệnh riêng), chọn lệnh, điền thông số.
    /// Chỉ dựng ra <see cref="Result"/>; việc ghi vào workspace do bên gọi làm.
    /// </summary>
    public partial class NewProfileWindow : Window
    {
        private enum Source { Tool, Custom }

        private sealed class Choice
        {
            public Source Source;
            public string Display { get; set; }
            public ToolDef Tool;
            public ToolBinding Binding;
            public CustomCommand Custom;
            /// <summary>Lý do không chọn được (vd. tool đã bị xoá khỏi thư viện).</summary>
            public string Problem;
        }

        private readonly Workspace _workspace;
        private readonly Func<IReadOnlyDictionary<int, string>> _usedPorts;
        private int _step;
        private Choice _choice;
        private ActionRow _row;
        private bool _nameEdited;
        private bool _suppress;

        public NewProfileWindow(Workspace workspace, Func<IReadOnlyDictionary<int, string>> usedPorts)
        {
            _workspace = workspace;
            _usedPorts = usedPorts;
            InitializeComponent();

            var choices = new List<Choice>();
            foreach (var binding in workspace.Bindings)
            {
                var tool = ToolCatalog.Find(binding.ToolId);
                choices.Add(new Choice
                {
                    Source = Source.Tool, Tool = tool, Binding = binding,
                    Display = (tool?.Name ?? binding.ToolId) + (tool == null ? "  (đã bị xoá khỏi thư viện)" : ""),
                    Problem = tool == null ? "Tool này không còn trong thư viện nên chưa tạo được profile." : null,
                });
            }
            foreach (var custom in workspace.Customs)
                choices.Add(new Choice { Source = Source.Custom, Custom = custom, Display = custom.Label + "  ·  lệnh riêng" });

            ToolList.ItemsSource = choices;
            ToolSearch.Attach(choices, o => ((Choice)o).Display);
            ShowStep(0);
        }

        /// <summary>Profile vừa dựng; null nếu huỷ.</summary>
        public Profile Result { get; private set; }

        /// <summary>True nếu người dùng bấm "Lưu và chạy".</summary>
        public bool RunAfter { get; private set; }

        // ---- điều hướng ----

        private void ShowStep(int step)
        {
            _step = step;
            ToolPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            CustomToolLink.Visibility = ToolPanel.Visibility;
            CommandPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            ParamsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
            NextButton.Visibility = step == 2 ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.Visibility = SaveRunButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

            switch (step)
            {
                case 0:
                    StepTitle.Text = "1 · Chọn tool";
                    StepHint.Text = ToolList.Items.Count == 0
                        ? "Workspace chưa có tool nào. Thêm tool hoặc lệnh riêng trước."
                        : "Profile chạy được với mọi tool đã gắn (lệnh gốc hoặc một hậu tố) và lệnh riêng của workspace.";
                    NextButton.IsEnabled = _choice != null && _choice.Problem == null;
                    break;
                case 1:
                    StepTitle.Text = "2 · Lệnh";
                    BuildCommandStep();
                    NextButton.IsEnabled = true;
                    break;
                default:
                    StepTitle.Text = "3 · Thông số";
                    StepHint.Text = "Điền thông số, kiểm lại \"Lệnh sẽ chạy\" rồi đặt tên.";
                    BuildParamsStep();
                    UpdateButtons();
                    break;
            }
        }

        /// <summary>Đúng khi người dùng chọn "+ Tool riêng…": đóng hộp thoại để WorkspaceView mở trình tạo lệnh riêng.</summary>
        public bool RequestCustomTool { get; private set; }

        /// <summary>Mở danh sách gợi ý của một ô tham số (nút ▾).</summary>
        private void OnPickOptions(object sender, RoutedEventArgs e) => Views.ParamOptionMenu.Open((System.Windows.Controls.Button)sender);

        private void OnCustomTool(object sender, RoutedEventArgs e)
        {
            RequestCustomTool = true;
            DialogResult = false;
        }

        private void OnBack(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

        private void OnNext(object sender, RoutedEventArgs e) => ShowStep(_step + 1);

        private void OnToolChanged(object sender, SelectionChangedEventArgs e)
        {
            _choice = ToolList.SelectedItem as Choice;
            if (_choice?.Problem != null)
                StepHint.Text = _choice.Problem;
            NextButton.IsEnabled = _choice != null && _choice.Problem == null;
        }

        // ---- bước 2 ----

        private void BuildCommandStep()
        {
            _suppress = true;
            try
            {
                if (_choice.Source == Source.Tool)
                {
                    StepHint.Text = "Chọn lệnh gốc hoặc một hậu tố của " + _choice.Tool.Name + ".";
                    FixedCommandNote.Visibility = Visibility.Collapsed;
                    ActionBox.Visibility = Visibility.Visible;
                    ActionBox.ItemsSource = ToolCatalog.ActionsFor(_workspace, _choice.Binding);
                    ActionBox.SelectedIndex = 0;
                    UseAction();
                }
                else
                {
                    ActionBox.Visibility = Visibility.Collapsed;
                    VariantPanel.Visibility = Visibility.Collapsed;
                    FixedCommandNote.Visibility = Visibility.Visible;
                    StepHint.Text = "Lệnh riêng đã cố định.";
                    FixedCommandNote.Text = "Chạy: " + _choice.Custom.CommandLine + "\nBước sau có thể nối thêm tham số.";
                }
            }
            finally
            {
                _suppress = false;
            }
        }

        private void OnActionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_suppress && ActionBox.SelectedItem is ActionDef)
                UseAction();
        }

        private void UseAction()
        {
            var action = ActionBox.SelectedItem as ActionDef;
            if (action == null)
                return;
            _row = new ActionRow(_choice.Tool, _choice.Binding, action, _usedPorts,
                _choice.Binding == null ? null : CommandBuilder.ToolCwd(_workspace, _choice.Binding, ToolCatalog.FindEntry(_choice.Binding.ToolId)));
            VariantBox.ItemsSource = action.Variants;
            VariantBox.SelectedItem = _row.Variant;
            VariantPanel.Visibility = action.Variants.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnVariantChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_suppress && _row != null && VariantBox.SelectedItem is VariantDef variant)
                _row.Variant = variant;
        }

        // ---- bước 3 ----

        private void BuildParamsStep()
        {
            _suppress = true;
            try
            {
                _nameEdited = false;
                if (_choice.Source == Source.Tool)
                {
                    // Ô {args} dạng chuỗi nhường chỗ cho trình sửa cặp bên dưới.
                    ParamList.ItemsSource = _row.Params.Where(p => !string.Equals(p.Key, ProfileKind.ArgsKey, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var p in _row.Params)
                        p.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ParamRow.Value)) Refresh(); };
                    _row.Revalidate();
                    Pairs.SetSuggestions(Suggestions());
                    PairLabel.Text = "Tham số (nối sau lệnh)";
                }
                else
                {
                    ParamList.ItemsSource = null;
                    Pairs.SetSuggestions(null);
                    PairLabel.Text = "Tham số thêm (nối sau lệnh)";
                }
                Pairs.Load(null);
                NameBox.Text = SuggestName();
            }
            finally
            {
                _suppress = false;
            }
            Refresh();
        }

        private void OnPairsChanged(object sender, EventArgs e)
        {
            if (!_suppress)
                Refresh();
        }

        /// <summary>Cờ đã quét của action đang chọn; rỗng nếu chưa quét.</summary>
        private IEnumerable<CapabilityEntry> Suggestions() => PairEditor.SuggestionsFor(_choice.Tool.Kind, _row.Action.Id);

        /// <summary>Dòng lệnh đầy đủ: lệnh của action (hoặc lệnh riêng) nối với các cặp.</summary>
        private string BuildLine()
        {
            var pairs = Pairs.GetPairs();
            if (_choice.Source == Source.Tool)
                return pairs.Count > 0 ? CommandBuilder.BuildWithPairs(_row.Variant, _row.Values(), pairs) : _row.Preview;
            return CommandBuilder.JoinArgs(_choice.Custom.CommandLine, PairLine.Render(pairs));
        }

        private void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppress)
                _nameEdited = true;
            UpdateButtons();
        }

        private string SuggestName()
        {
            switch (_choice.Source)
            {
                case Source.Tool:
                    var name = $"{_choice.Tool.Name} · {_row.Action.Label}";
                    if (_row.Action.Variants.Count > 1)
                        name += " · " + _row.Variant.Label;
                    var port = CommandBuilder.PortOf(_row.Variant, CommandBuilder.Effective(_row.Variant, _row.Values()));
                    return port.HasValue ? name + " · :" + port.Value : name;
                default:
                    return _choice.Custom.Label;
            }
        }

        /// <summary>Tính lại dòng "Lệnh sẽ chạy", lỗi tham số và nút.</summary>
        private void Refresh()
        {
            if (_choice == null || _step != 2)
                return;
            if (_choice.Source == Source.Tool)
                _row.Revalidate();
            string preview;
            try
            {
                preview = BuildLine();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                preview = "(chưa dựng được lệnh: " + ex.Message + ")";
            }
            var asks = PairLine.Pending(Pairs.GetPairs()).Count;
            PreviewText.Text = asks > 0 ? preview + $"   [+{asks} giá trị hỏi lúc chạy]" : preview;
            // Tên gợi ý đi theo tham số (vd. cổng) cho tới khi người dùng tự sửa tên.
            if (!_nameEdited && _choice.Source == Source.Tool)
            {
                _suppress = true;
                NameBox.Text = SuggestName();
                _suppress = false;
            }
            UpdateButtons();
        }

        private bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                return false;
            if (Pairs.Error != null)
                return false;
            return _choice.Source != Source.Tool || !CommandBuilder.Validate(_row.Variant, _row.Values()).Any();
        }

        private void UpdateButtons()
        {
            if (_step != 2 || _choice == null)
                return;
            var ok = IsValid();
            SaveButton.IsEnabled = ok;
            SaveRunButton.IsEnabled = ok;
        }

        // ---- kết quả ----

        private void OnSave(object sender, RoutedEventArgs e) => Finish(false);

        private void OnSaveRun(object sender, RoutedEventArgs e) => Finish(true);

        private void Finish(bool run)
        {
            if (!IsValid())
                return;
            var profile = new Profile { Id = WorkspaceStore.NewId(), Label = NameBox.Text.Trim() };
            switch (_choice.Source)
            {
                case Source.Tool:
                    profile.Kind = _choice.Tool.Kind;
                    profile.ActionId = _row.Action.Id;
                    // Lệnh gốc (ActionId rỗng) không có biến thể riêng.
                    profile.VariantId = _row.Action.Id.Length == 0 ? null : _row.Variant.Id;
                    profile.Vals = new Dictionary<string, string>(CommandBuilder.Effective(_row.Variant, _row.Values()));
                    break;
                default:
                    profile.Kind = ProfileKind.Custom;
                    profile.RefId = _choice.Custom.Id;
                    profile.Vals = new Dictionary<string, string>();
                    break;
            }
            var pairs = Pairs.GetPairs();
            profile.Pairs = pairs.Count > 0 ? pairs : null;
            Result = profile;
            RunAfter = run;
            DialogResult = true;
        }
    }
}
