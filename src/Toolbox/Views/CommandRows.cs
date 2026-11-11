using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Đổi đường dẫn người dùng chọn thành dạng tương đối so với gốc workspace khi có thể.</summary>
    public static class WorkspacePaths
    {
        public static string ToRelative(string root, string picked)
        {
            try
            {
                var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var p = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase))
                    return "";
                if (p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return p.Substring(r.Length + 1);
                return p;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return picked;
            }
        }
    }

    /// <summary>Phần chung của một dòng có nút Chạy/Dừng: theo dõi tác vụ đang chạy khớp khoá của dòng.</summary>
    public abstract class RunRow : INotifyPropertyChanged
    {
        private bool _canRun = true;
        private TaskInfo _running;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsRunning => _running != null;

        public string RunningTaskId => _running?.Id;

        public bool CanRun
        {
            get => _canRun;
            set
            {
                if (_canRun == value)
                    return;
                _canRun = value;
                Raise(nameof(CanRun));
            }
        }

        public Visibility RunVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

        public Visibility StopVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

        public string RunningText => _running?.Port is int port ? "Đang chạy · :" + port : "Đang chạy";

        /// <summary>Khoá (đã tách) của tác vụ này có thuộc dòng không.</summary>
        protected abstract bool Matches(string[] key);

        /// <summary>Cập nhật theo danh sách tác vụ đang chạy; chỉ báo giao diện khi có đổi.</summary>
        public void UpdateRunning(IEnumerable<TaskInfo> running)
        {
            var found = running.FirstOrDefault(t => Matches(TaskKey.Split(t.Spec.Key)));
            var oldPort = _running?.Port;
            var changed = !ReferenceEquals(found, _running) || found?.Port != oldPort;
            _running = found;
            if (!changed)
                return;
            Raise(nameof(IsRunning));
            Raise(nameof(RunningTaskId));
            Raise(nameof(RunVisibility));
            Raise(nameof(StopVisibility));
            Raise(nameof(RunningText));
            OnRunningChanged();
        }

        protected virtual void OnRunningChanged()
        {
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Một tham số của biến thể đang chọn (cổng, bộ lọc…). Đổi giá trị thì dòng chủ tính lại lỗi và lệnh xem trước.</summary>
    public sealed class ParamRow : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private string _value;
        private string _error;

        public ParamRow(ParamDef def, string value, Action changed)
        {
            Def = def;
            // Form mới điền sẵn mặc định, để ô hiện đúng giá trị lệnh sẽ dùng (không ẩn). Profile đã lưu giữ nguyên giá trị của nó.
            _value = value ?? def.Default ?? "";
            _changed = changed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ParamDef Def { get; }

        public string Key => Def.Key;

        public string Prompt => Def.Prompt;

        /// <summary>Gợi ý mờ khi để trống: giá trị mặc định của tham số.</summary>
        public string Default => Def.Default;

        /// <summary>Danh sách gợi ý đọc từ thư mục chạy (ô "" đầu = mặc định). Rỗng nghĩa là ô nhập tay.</summary>
        public IReadOnlyList<string> Options { get; set; } = new string[0];

        public Visibility OptionsVisibility => Options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string Value
        {
            get => _value;
            set
            {
                value = value ?? "";
                if (_value == value)
                    return;
                _value = value;
                Raise(nameof(Value));
                _changed?.Invoke();
            }
        }

        public string Error
        {
            get => _error;
            set
            {
                if (_error == value)
                    return;
                _error = value;
                Raise(nameof(Error));
                Raise(nameof(ErrorVisibility));
            }
        }

        public Visibility ErrorVisibility => string.IsNullOrEmpty(_error) ? Visibility.Collapsed : Visibility.Visible;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Một action của tool (Build, Chạy dev…) cùng biến thể đang chọn và form tham số.</summary>
    public sealed class ActionRow : RunRow
    {
        private readonly Func<IReadOnlyDictionary<int, string>> _usedPorts;
        private VariantDef _variant;
        private bool _hasErrors;

        /// <summary>Thư mục chạy tuyệt đối của binding, để đọc danh sách gợi ý (vd project trong angular.json).</summary>
        private readonly string _workingDirectory;

        public ActionRow(ToolDef tool, ToolBinding binding, ActionDef action, Func<IReadOnlyDictionary<int, string>> usedPorts, string workingDirectory = null)
        {
            Tool = tool;
            Binding = binding;
            Action = action;
            _usedPorts = usedPorts;
            _workingDirectory = workingDirectory;
            _variant = action.Default;
            Editor = new PairEditor();
            Editor.Changed += (s, e) =>
            {
                Revalidate();
                // Gõ thêm lệnh con (migrations add) thì cờ của nó mới là gợi ý đúng; chỉ nạp lại khi phạm vi đổi.
                if (!string.Equals(Scope(), _scope, StringComparison.OrdinalIgnoreCase))
                    RefreshSuggestions();
            };
            Editor.ScanRequested += async (s, e) => await ScanAsync();
            RefreshSuggestions();
            BuildParams(null);
        }

        private string _scope = "";

        /// <summary>
        /// Phạm vi trợ giúp của dòng: id của action cộng các đối số vị trí đứng đầu (key trống), vd <c>ef migrations add</c>.
        /// Cờ như <c>--project</c> thường chỉ có trong help của lệnh lá, nên quét và gợi ý theo phạm vi này chứ không chỉ lệnh gốc.
        /// Dừng ở cặp đầu tiên có key, hoặc token không giống tên lệnh con (đường dẫn, số…).
        /// </summary>
        internal static string ScopeOf(string actionId, IEnumerable<ParamPair> pairs)
        {
            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(actionId))
                tokens.Add(actionId.Trim());
            foreach (var pair in pairs)
            {
                if (pair.Key.Length > 0)
                    break;
                var value = (pair.Value ?? "").Trim();
                if (value.Length == 0 || !CapabilityStore.SubcommandName.IsMatch(value))
                    break;
                tokens.Add(value);
            }
            return string.Join(" ", tokens);
        }

        private string Scope() => ScopeOf(Action.Id, Editor.GetPairs());

        /// <summary>Trình sửa cặp tham số của thẻ lệnh (bấm ＋ để thêm; gợi ý key từ bảng lệnh đã quét).</summary>
        public PairEditor Editor { get; }

        /// <summary>Đọc lại gợi ý key (sau khi quét trợ giúp ở "Lệnh…").</summary>
        public void RefreshSuggestions()
        {
            _scope = Scope();
            var suggestions = PairEditor.SuggestionsFor(Tool.Kind, _scope, out var hasOwn);
            if (!hasOwn && !string.Equals(_scope, Action.Id ?? "", StringComparison.OrdinalIgnoreCase))
            {
                // Phạm vi sâu hơn action (migrations add) chưa quét: lùi về cờ của action, rồi cờ chung. Đã quét thì ghi đè hết.
                var outer = PairEditor.SuggestionsFor(Tool.Kind, Action.Id, out _);
                suggestions = suggestions.Concat(outer).ToList();
            }
            Editor.SetSuggestions(suggestions);
            Editor.SetPositionalSource(PairEditor.LoadCapabilities(Tool.Kind), Action.Id, _workingDirectory);
            Editor.EnableScan(!string.IsNullOrEmpty(Tool.HelpCommand(_scope)) && !string.IsNullOrEmpty(_workingDirectory), hasOwn);
        }

        /// <summary>Chạy lệnh trợ giúp của action này (nền), lưu vào bảng lệnh rồi nạp lại dropdown.</summary>
        private async System.Threading.Tasks.Task ScanAsync()
        {
            var scope = Scope();
            var command = Tool.HelpCommand(scope);
            if (command == null)
                return;
            Editor.SetScanning(true, null);
            var kind = Tool.Kind;
            var cwd = _workingDirectory;
            var outcome = await System.Threading.Tasks.Task.Run(() =>
                CapabilityScanner.ScanAndStore(CapabilityStore.DefaultDirectory, kind, scope, command, cwd, includeSubcommands: true));
            RefreshSuggestions();
            Editor.SetScanning(false, outcome.Error ?? $"Đã đọc {outcome.Entries.Count(en => CapabilityStore.IsFlag(en.Name))} cờ từ: {command}");
        }

        /// <summary>Các cặp tham số đang nhập (bỏ dòng trống).</summary>
        public List<ParamPair> Pairs => Editor.GetPairs();

        /// <summary>Tham số của biến thể trừ ô {args} dạng chuỗi (đã nhường chỗ cho <see cref="Editor"/>).</summary>
        public ObservableCollection<ParamRow> VisibleParams { get; } = new ObservableCollection<ParamRow>();

        public Visibility VisibleParamsVisibility => VisibleParams.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public ToolDef Tool { get; }

        public ToolBinding Binding { get; }

        public ActionDef Action { get; }

        public string Title => Action.Label;

        public IReadOnlyList<VariantDef> Variants => Action.Variants;

        public ObservableCollection<ParamRow> Params { get; } = new ObservableCollection<ParamRow>();

        public Visibility VariantPickerVisibility => Variants.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ParamsVisibility => Params.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public VariantDef Variant
        {
            get => _variant;
            set
            {
                if (value == null || ReferenceEquals(_variant, value))
                    return;
                _variant = value;
                BuildParams(null);
                Raise(nameof(Variant));
                Raise(nameof(ParamsVisibility));
                Revalidate();
            }
        }

        /// <summary>Dòng lệnh sẽ chạy; khi tham số chưa hợp lệ thì hiện mẫu để người dùng vẫn thấy hình dạng lệnh.</summary>
        public string Preview
        {
            get
            {
                try
                {
                    var pairs = Pairs;
                    return pairs.Count > 0 ? CommandBuilder.BuildWithPairs(_variant, Values(), pairs) : CommandBuilder.Build(_variant, Values());
                }
                catch (ArgumentException)
                {
                    return _variant.Template;
                }
            }
        }

        /// <summary>Giá trị người dùng đã điền (ô trống = dùng mặc định).</summary>
        public Dictionary<string, string> Values() =>
            Params.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        /// <summary>Điền sẵn form (vd. nạp lại từ profile). Biến thể phải thuộc action này.</summary>
        public void Apply(VariantDef variant, IDictionary<string, string> vals)
        {
            _variant = variant;
            BuildParams(vals);
            Raise(nameof(Variant));
            Raise(nameof(ParamsVisibility));
            Revalidate();
        }

        /// <summary>Kiểm tra tham số (cả trùng cổng), hiện lỗi dưới từng ô. True nếu có lỗi.</summary>
        public bool Revalidate()
        {
            var used = _usedPorts?.Invoke();
            var errors = CommandBuilder.Validate(_variant, Values(), used);
            foreach (var p in Params)
            {
                if (!errors.TryGetValue(p.Key, out var message))
                {
                    p.Error = null;
                    continue;
                }
                p.Error = p.Def.IsPort ? message + SuggestPort(p, used) : message;
            }
            _hasErrors = errors.Count > 0 || Editor.Error != null;
            Raise(nameof(Preview));
            return _hasErrors;
        }

        private string SuggestPort(ParamRow row, IReadOnlyDictionary<int, string> used)
        {
            var effective = CommandBuilder.Effective(_variant, Values())[row.Key];
            if (!PortRules.TryParse(effective, out var port) || PortRules.Conflict(port, used) == null)
                return "";
            var free = PortRules.NextFree(port + 1, used);
            return free > 0 ? $" Gợi ý: {free}." : "";
        }

        protected override bool Matches(string[] key) =>
            string.Equals(key[1], Tool.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(key[2], Action.Id, StringComparison.OrdinalIgnoreCase)
            && key[4].Length == 0;

        private void BuildParams(IDictionary<string, string> vals)
        {
            Params.Clear();
            VisibleParams.Clear();
            foreach (var def in _variant.Params)
            {
                string value = null;
                vals?.TryGetValue(def.Key, out value);
                var row = new ParamRow(def, value, () => Revalidate());
                var suggestions = ToolCatalog.OptionsFor(def, _workingDirectory);
                if (suggestions.Count > 0)
                    row.Options = new[] { "" }.Concat(suggestions).ToList();
                Params.Add(row);
                if (!string.Equals(def.Key, ProfileKind.ArgsKey, StringComparison.OrdinalIgnoreCase))
                    VisibleParams.Add(row);
            }
            Raise(nameof(VisibleParamsVisibility));
        }
    }

    /// <summary>Một profile đã lưu: cách chạy có tên. Hỏng khi tool/action/biến thể của nó không còn.</summary>
    public sealed class ProfileRow : RunRow
    {
        /// <summary>Agent vừa thêm hoặc sửa profile này và bạn chưa xem.</summary>
        public bool IsNew { get; set; }

        public Visibility NewVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;

        public ProfileRow(Workspace workspace, Profile profile)
        {
            Profile = profile;
            if (ProfileKind.IsCustom(profile.Kind))
            {
                // Lệnh riêng: không thuộc tool nào; kiểm bằng bộ resolve của workflow.
                var resolved = StackResolver.Resolve(workspace, new StackStep { Kind = profile.Kind, ProfileId = profile.Id }, 0);
                Problem = resolved.Problem;
                RefSummary = "Lệnh riêng · " + (workspace.Customs.Find(c => string.Equals(c.Id, profile.RefId, StringComparison.OrdinalIgnoreCase))?.Label ?? "(đã xoá)");
                if (profile.ExtraArgs.Length > 0)
                    RefSummary += " · " + profile.ExtraArgs;
                CanRun = Problem == null;
                return;
            }
            Binding = workspace.Binding(profile.Kind);
            Tool = ToolCatalog.Find(profile.Kind);
            Action = Binding == null ? null : ToolCatalog.FindAction(workspace, Binding, profile.ActionId);
            // Profile chạy lệnh gốc của tool (ActionId rỗng) không có biến thể riêng: dùng biến thể mặc định.
            Variant = string.IsNullOrEmpty(profile.VariantId)
                ? Action?.Default
                : Action?.Variants.FirstOrDefault(v => string.Equals(v.Id, profile.VariantId, StringComparison.OrdinalIgnoreCase));

            if (Binding == null)
                Problem = "Workspace không còn gắn tool này.";
            else if (Action == null)
                Problem = "Lệnh này không còn trong tool (script đã đổi tên hoặc bị xoá).";
            else if (Variant == null)
                Problem = "Biến thể của lệnh này không còn.";
            CanRun = Problem == null;
        }

        public Profile Profile { get; }

        public ToolBinding Binding { get; }

        public ToolDef Tool { get; }

        public ActionDef Action { get; }

        public VariantDef Variant { get; }

        /// <summary>Lý do không chạy được; null nếu dùng được.</summary>
        public string Problem { get; }

        public string Title => Profile.Label;

        /// <summary>Mô tả của profile loại library/custom (tên tool hoặc lệnh riêng + tham số thêm); null với loại dựng sẵn.</summary>
        public string RefSummary { get; }

        public Visibility ProblemVisibility => Problem == null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Mô tả ngắn: lệnh · biến thể · tham số, ví dụ "Chạy dev · Dev · cổng 4201".</summary>
        public string Summary
        {
            get
            {
                if (RefSummary != null)
                    return RefSummary;
                if (Action == null || Variant == null)
                    return $"{Profile.ActionId} · {Profile.VariantId}";
                var parts = new List<string> { Action.Label };
                if (Action.Variants.Count > 1)
                    parts.Add(Variant.Label);
                var effective = CommandBuilder.Effective(Variant, Profile.Vals);
                foreach (var p in Variant.Params)
                    parts.Add(p.IsPort ? "cổng " + effective[p.Key] : p.Prompt + ": " + effective[p.Key]);
                return string.Join(" · ", parts);
            }
        }

        protected override bool Matches(string[] key) =>
            string.Equals(key[4], Profile.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Một lệnh riêng của workspace.</summary>
    public sealed class CustomRow : RunRow
    {
        public CustomRow(CustomCommand command)
        {
            Command = command;
        }

        public CustomCommand Command { get; }

        public string Title => Command.Label;

        public string CommandLine => Command.CommandLine;

        public string CwdText => string.IsNullOrWhiteSpace(Command.Cwd) ? "thư mục gốc" : Command.Cwd;

        public Visibility WindowVisibility => Command.Window ? Visibility.Visible : Visibility.Collapsed;

        protected override bool Matches(string[] key) =>
            string.Equals(key[1], TaskKey.Custom, StringComparison.OrdinalIgnoreCase)
            && string.Equals(key[2], Command.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tiêu đề một nhóm trong danh sách lệnh của tab Chạy: tool đã gắn (có menu ⋯), "Lệnh riêng" hoặc "Lệnh đã ghim".</summary>
    public sealed class GroupRow
    {
        public GroupRow(string title, ToolBinding binding = null, string note = null)
        {
            Title = title;
            Binding = binding;
            Note = note;
        }

        public string Title { get; }

        /// <summary>Binding của tool (null với nhóm không phải tool). Có thì hiện menu ⋯ của tool.</summary>
        public ToolBinding Binding { get; }

        /// <summary>Dòng chú dưới tiêu đề: thư mục chạy không có, tool chưa có lệnh…; null nếu không có gì cần nói.</summary>
        public string Note { get; }

        public Visibility MenuVisibility => Binding == null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Một tab cố định của trang workspace: Chạy, Workflow, Chia sẻ.</summary>
    public sealed class TabRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        /// <summary>Id của tab "Lệnh riêng" và "Workflow" (không trùng kind nào của catalog).</summary>
        public const string CustomId = "";
        public const string StacksId = ":stacks";
        public const string RunId = ":run";
        public const string ToolsId = ":tools";

        public TabRow(string title, ToolBinding binding, string id = null)
        {
            Title = title;
            Binding = binding;
            Id = id ?? binding?.ToolId ?? CustomId;
        }

        public static TabRow Stacks() => new TabRow("Workflow", null, StacksId);

        /// <summary>Tab đầu tiên: workflow và profile của cả workspace, để chạy ngay.</summary>
        public static TabRow Run() => new TabRow("Chạy", null, RunId);

        /// <summary>Kind của tool, hoặc <see cref="CustomId"/> / <see cref="StacksId"/>; dùng để nhớ tab đang chọn.</summary>
        public string Id { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; }

        public ToolBinding Binding { get; }

        public bool IsCustom => Id == CustomId;

        public bool IsStacks => Id == StacksId;

        public bool IsRun => Id == RunId;

        public bool IsTools => Id == ToolsId;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
    /// <summary>Một bước trong thẻ workflow: tên, kiểu chờ, trạng thái lần chạy gần nhất hoặc lý do hỏng.</summary>
    public sealed class StackStepRow
    {
        public StackStepRow(ResolvedStep resolved, StepProgress progress)
        {
            Title = $"{resolved.Index + 1}. {resolved.Label}";
            Problem = resolved.Problem;
            var status = progress?.Status ?? StepStatus.Pending;
            if (resolved.IsBroken)
                status = StepStatus.Failed;
            Kind = status.ToString();
            Glyph = status == StepStatus.Done ? "✓" : status == StepStatus.Failed ? "✗" : status == StepStatus.Running ? "▶" : "○";
            IsBroken = resolved.IsBroken;
            Note = resolved.IsBroken ? "Hỏng: " + resolved.Problem : progress?.Detail;
            WaitText = WaitTextFor(resolved);
            TaskId = progress?.TaskId;
            Status = status;
            if (status == StepStatus.Running && progress != null)
                Elapsed = Core.Text.Duration(DateTime.UtcNow - progress.Since);
            Index = resolved.Index;
        }

        public string Title { get; }
        public string Problem { get; }
        public bool IsBroken { get; }
        /// <summary>Tên <see cref="StepStatus"/>; XAML đổi màu theo đó.</summary>
        public string Kind { get; }
        public string Glyph { get; }
        public string Note { get; }
        public string WaitText { get; }
        public string TaskId { get; }
        public StepStatus Status { get; }
        public int Index { get; }
        /// <summary>"45 giây" khi bước đang chạy hoặc đang chờ cổng; rỗng nếu không.</summary>
        public string Elapsed { get; }
        public string ElapsedText => string.IsNullOrEmpty(Elapsed) ? "" : "đã " + Elapsed;

        private static string WaitTextFor(ResolvedStep r)
        {
            switch (r.Wait)
            {
                case WaitKind.Port:
                    var port = r.Spec?.Port ?? (r.Step.Port > 0 ? r.Step.Port : (int?)null);
                    return port.HasValue ? "chờ cổng :" + port.Value : "chờ cổng";
                case WaitKind.Exit:
                    return "chờ lệnh thoát";
                default:
                    return "";
            }
        }
    }

    /// <summary>Thẻ một workflow: các bước, kết quả lần chạy gần nhất và nút Chạy/Dừng.</summary>
    public sealed class StackRow : INotifyPropertyChanged
    {
        /// <summary>Agent vừa thêm hoặc sửa workflow này và bạn chưa xem.</summary>
        public bool IsNew { get; set; }

        public Visibility NewVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;

        private readonly Workspace _workspace;
        private List<StackStepRow> _steps = new List<StackStepRow>();
        private StackRun _run;
        private int _brokenCount;

        public StackRow(Workspace workspace, StackDef stack)
        {
            _workspace = workspace;
            Stack = stack;
            Update(null);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public StackDef Stack { get; }

        public string Title => Stack.Label;

        public List<StackStepRow> Steps => _steps;

        public StackRun Run => _run;

        public string Summary
        {
            get
            {
                if (Stack.Steps.Count == 0)
                    return "Chưa có bước nào";
                return _brokenCount > 0 ? $"{Stack.Steps.Count} bước · {_brokenCount} bước hỏng" : $"{Stack.Steps.Count} bước";
            }
        }

        /// <summary>Đang chạy các bước, hoặc còn tác vụ do workflow này bật.</summary>
        public bool CanStop => _run != null && (_run.IsActive || _run.HasRunningTasks);

        public bool CanEdit => _run == null || !_run.IsActive;

        public bool CanRun { get; private set; }

        public Visibility RunVisibility => CanStop ? Visibility.Collapsed : Visibility.Visible;

        public Visibility StopVisibility => CanStop ? Visibility.Visible : Visibility.Collapsed;

        public string Message => _run?.Message;

        public Visibility MessageVisibility => string.IsNullOrEmpty(Message) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>"ok", "error" hoặc "info"; XAML đổi màu theo đó.</summary>
        public string MessageKind
        {
            get
            {
                switch (_run?.State)
                {
                    case RunState.Succeeded: return "ok";
                    case RunState.Failed: return "error";
                    default: return "info";
                }
            }
        }

        /// <summary>Chỉ số bước lỗi của lần chạy gần nhất, hoặc -1.</summary>
        public int FailedIndex => _run == null || _run.IsActive ? -1 : _run.FirstFailedIndex;

        public bool HasFailedStep => FailedIndex >= 0;

        public Visibility FailedVisibility => HasFailedStep ? Visibility.Visible : Visibility.Collapsed;

        public string RetryText => HasFailedStep ? $"Chạy lại từ bước {FailedIndex + 1}" : "";

        public string FailedTaskId => HasFailedStep ? _run.Steps[FailedIndex].TaskId : null;

        public Visibility FailedLogVisibility => FailedTaskId != null ? Visibility.Visible : Visibility.Collapsed;

        public string RunTip => _brokenCount > 0 ? "Có bước hỏng; sửa workflow trước khi chạy" : "Chạy lần lượt các bước";

        /// <summary>Đối chiếu lại các bước với workspace (tool/profile/lệnh có thể đã đổi) và áp kết quả lần chạy.</summary>
        public void Update(StackRun run)
        {
            _run = run;
            var resolved = StackResolver.ResolveAll(_workspace, Stack);
            _brokenCount = resolved.Count(r => r.IsBroken);
            _steps = resolved
                .Select(r => new StackStepRow(r, run != null && r.Index < run.Steps.Count ? run.Steps[r.Index] : null))
                .ToList();
            CanRun = _workspace.PathExists && Stack.Steps.Count > 0 && _brokenCount == 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
