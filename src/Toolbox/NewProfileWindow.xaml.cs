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
        private enum Source { Builtin, Library, Custom }

        private sealed class Choice
        {
            public Source Source;
            public string Display { get; set; }
            public ToolDef Tool;
            public ToolBinding Binding;
            public LibraryBinding Library;
            public ToolEntry Entry;
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

        public NewProfileWindow(Workspace workspace, ToolStore library, Func<IReadOnlyDictionary<int, string>> usedPorts)
        {
            _workspace = workspace;
            _usedPorts = usedPorts;
            InitializeComponent();

            var choices = new List<Choice>();
            foreach (var binding in workspace.Bindings)
            {
                var tool = ToolCatalog.Find(binding.Kind);
                if (tool != null)
                    choices.Add(new Choice { Source = Source.Builtin, Tool = tool, Binding = binding, Display = tool.Name + "  ·  dựng sẵn" });
            }
            foreach (var lib in workspace.LibraryBindings)
            {
                var entry = library?.Find(lib.ToolId);
                var name = entry?.Name ?? lib.Name ?? lib.ToolId;
                choices.Add(new Choice
                {
                    Source = Source.Library, Library = lib, Entry = entry,
                    Display = name + "  ·  thư viện" + (entry == null ? "  (đã bị xoá khỏi thư viện)" : ""),
                    Problem = entry == null ? "Tool này không còn trong thư viện nên chưa tạo được profile." : null,
                });
            }
            foreach (var custom in workspace.Customs)
                choices.Add(new Choice { Source = Source.Custom, Custom = custom, Display = custom.Label + "  ·  lệnh riêng" });

            ToolList.ItemsSource = choices;
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
            ToolList.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            CustomToolLink.Visibility = ToolList.Visibility;
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
                        ? "Workspace chưa có tool nào. Thêm tool dựng sẵn, tool thư viện hoặc lệnh riêng trước."
                        : "Profile chạy được với tool dựng sẵn, tool thư viện đã gắn và lệnh riêng của workspace.";
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
                if (_choice.Source == Source.Builtin)
                {
                    StepHint.Text = "Chọn lệnh của " + _choice.Tool.Name + ".";
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
                    StepHint.Text = "Lệnh của " + (_choice.Source == Source.Library ? "tool thư viện" : "lệnh riêng") + " đã cố định.";
                    FixedCommandNote.Text = _choice.Source == Source.Library
                        ? "Tool thư viện chạy theo cách đăng ký trong thư viện. Bước sau có thể nối thêm tham số."
                        : "Chạy: " + _choice.Custom.CommandLine + "\nBước sau có thể nối thêm tham số.";
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
                _choice.Binding == null ? null : _workspace.ResolveCwd(_choice.Binding.Cwd));
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
                ExtraBox.Text = "";
                ExtraError.Visibility = Visibility.Collapsed;
                if (_choice.Source == Source.Builtin)
                {
                    ParamList.ItemsSource = _row.Params;
                    ExtraPanel.Visibility = Visibility.Collapsed;
                    foreach (var p in _row.Params)
                        p.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ParamRow.Value)) Refresh(); };
                    _row.Revalidate();
                }
                else
                {
                    ParamList.ItemsSource = null;
                    ExtraPanel.Visibility = Visibility.Visible;
                    ExtraLabel.Text = _choice.Source == Source.Library
                        ? "Tham số thêm (nối sau tham số của tool)"
                        : "Tham số thêm (nối sau lệnh)";
                }
                NameBox.Text = SuggestName();
            }
            finally
            {
                _suppress = false;
            }
            Refresh();
        }

        private void OnExtraChanged(object sender, TextChangedEventArgs e) => Refresh();

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
                case Source.Builtin:
                    var name = $"{_choice.Tool.Name} · {_row.Action.Label}";
                    if (_row.Action.Variants.Count > 1)
                        name += " · " + _row.Variant.Label;
                    var port = CommandBuilder.PortOf(_row.Variant, CommandBuilder.Effective(_row.Variant, _row.Values()));
                    return port.HasValue ? name + " · :" + port.Value : name;
                case Source.Library:
                    return _choice.Entry.Name;
                default:
                    return _choice.Custom.Label;
            }
        }

        /// <summary>Tính lại dòng "Lệnh sẽ chạy", lỗi tham số và nút.</summary>
        private void Refresh()
        {
            if (_choice == null || _step != 2)
                return;
            string preview;
            string problem = null;
            switch (_choice.Source)
            {
                case Source.Builtin:
                    _row.Revalidate();
                    preview = _row.Preview;
                    break;
                default:
                    var extra = ExtraBox.Text.Trim();
                    problem = CommandBuilder.CheckExtraArgs(extra);
                    try
                    {
                        preview = _choice.Source == Source.Library
                            ? CommandBuilder.LibraryCommandLine(_choice.Entry,
                                CommandBuilder.JoinArgs(_choice.Library.EffectiveArgs(_choice.Entry), extra))
                            : CommandBuilder.CustomLine(_choice.Custom, extra);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        preview = "(chưa dựng được lệnh: " + ex.Message + ")";
                    }
                    break;
            }
            PreviewText.Text = preview;
            ExtraError.Text = problem;
            ExtraError.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            // Tên gợi ý đi theo tham số (vd. cổng) cho tới khi người dùng tự sửa tên.
            if (!_nameEdited && _choice.Source == Source.Builtin)
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
            if (_choice.Source == Source.Builtin)
                return !CommandBuilder.Validate(_row.Variant, _row.Values()).Any();
            return CommandBuilder.CheckExtraArgs(ExtraBox.Text.Trim()) == null;
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
                case Source.Builtin:
                    profile.Kind = _choice.Tool.Kind;
                    profile.ActionId = _row.Action.Id;
                    profile.VariantId = _row.Variant.Id;
                    profile.Vals = new Dictionary<string, string>(CommandBuilder.Effective(_row.Variant, _row.Values()));
                    break;
                case Source.Library:
                    profile.Kind = ProfileKind.Library;
                    profile.RefId = _choice.Library.ToolId;
                    profile.Vals = ExtraVals();
                    break;
                default:
                    profile.Kind = ProfileKind.Custom;
                    profile.RefId = _choice.Custom.Id;
                    profile.Vals = ExtraVals();
                    break;
            }
            Result = profile;
            RunAfter = run;
            DialogResult = true;
        }

        private Dictionary<string, string> ExtraVals()
        {
            var vals = new Dictionary<string, string>();
            var extra = ExtraBox.Text.Trim();
            if (extra.Length > 0)
                vals[ProfileKind.ArgsKey] = extra;
            return vals;
        }
    }
}
