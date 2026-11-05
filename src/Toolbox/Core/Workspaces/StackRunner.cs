using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Một bước của bộ chạy sau khi đối chiếu với workspace: chạy được (<see cref="Spec"/>) hoặc hỏng (<see cref="Problem"/>).</summary>
    public sealed class ResolvedStep
    {
        public int Index { get; internal set; }
        public StackStep Step { get; internal set; }
        /// <summary>Tên hiện trên giao diện; bước hỏng có tên dựa trên dữ liệu đã lưu.</summary>
        public string Label { get; internal set; }
        public TaskSpec Spec { get; internal set; }
        /// <summary>Lý do không chạy được; null nếu ổn.</summary>
        public string Problem { get; internal set; }
        public bool IsBroken => Problem != null;
        /// <summary>Kiểu chờ đã chuẩn hoá (<see cref="WaitKind"/>).</summary>
        public string Wait { get; internal set; }
        /// <summary>Nhóm chạy song song (<see cref="StackStep.Group"/>); 0 = không nhóm.</summary>
        public int Group { get; internal set; }
    }

    /// <summary>Gom các bước thành "đợt": bước không nhóm là một đợt riêng, các bước liên tiếp cùng Group &gt; 0 chung một đợt chạy song song.</summary>
    public static class StackPlan
    {
        /// <summary>Lỗi nhóm (Group &lt; 0, hoặc cùng số nhóm xuất hiện ở hai đoạn không liền nhau); null nếu hợp lệ.</summary>
        public static string Validate(IEnumerable<StackStep> steps)
        {
            var list = steps.ToList();
            var closed = new HashSet<int>();
            var previous = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var group = list[i].Group;
                if (group < 0)
                    return $"Bước {i + 1}: Group phải >= 0 (đang là {group}).";
                if (group != previous)
                {
                    if (previous > 0)
                        closed.Add(previous);
                    if (group > 0 && closed.Contains(group))
                        return $"Bước {i + 1}: nhóm {group} bị tách (các bước cùng nhóm phải liền nhau).";
                }
                previous = group;
            }
            return null;
        }

        /// <summary>Danh sách đợt, mỗi đợt là các chỉ số bước (theo thứ tự). Nhóm không hợp lệ thì ném <see cref="ArgumentException"/>.</summary>
        public static List<List<int>> Batches(IEnumerable<StackStep> steps)
        {
            var list = steps.ToList();
            var problem = Validate(list);
            if (problem != null)
                throw new ArgumentException(problem);
            var batches = new List<List<int>>();
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0 && list[i].Group > 0 && list[i].Group == list[i - 1].Group)
                    batches[batches.Count - 1].Add(i);
                else
                    batches.Add(new List<int> { i });
            }
            return batches;
        }
    }

    /// <summary>Đổi <see cref="StackStep"/> (chỉ giữ id) thành lệnh cụ thể theo workspace hiện tại.</summary>
    public static class StackResolver
    {
        /// <summary>Nhãn của tác vụ chạy từ một action: "Angular · Chạy dev · :4200".</summary>
        public static string ActionLabel(ToolDef tool, ActionDef action, VariantDef variant, int? port)
        {
            var label = $"{tool.Name} · {action.Label}";
            if (action.Variants.Count > 1)
                label += " · " + variant.Label;
            if (port.HasValue)
                label += " · :" + port.Value;
            return label;
        }

        public static string NormalizeWait(string wait) =>
            string.IsNullOrWhiteSpace(wait) ? WaitKind.None : wait.Trim().ToLowerInvariant();

        [ThreadStatic]
        private static ToolStore _ambientLibrary;

        /// <summary>
        /// Trong khối using, bước loại "library" tra thư viện trong <paramref name="store"/> (cho luồng hiện tại) thay vì
        /// file thư viện mặc định. Dùng khi người gọi đã có sẵn kho (MCP, thử nghiệm).
        /// </summary>
        public static IDisposable UseLibrary(ToolStore store)
        {
            var previous = _ambientLibrary;
            _ambientLibrary = store;
            return new Scope(() => _ambientLibrary = previous);
        }

        private sealed class Scope : IDisposable
        {
            private Action _onDispose;

            public Scope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }

        internal static ToolStore LibraryStore(ToolStore explicitStore)
        {
            if (explicitStore != null)
                return explicitStore;
            if (_ambientLibrary != null)
                return _ambientLibrary;
            try
            {
                return ToolStore.Load(ToolStore.DefaultPath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// <paramref name="library"/>: kho tool dùng cho bước loại "library"; null thì lấy kho của <see cref="UseLibrary"/>
        /// hoặc file thư viện mặc định.
        /// </summary>
        public static ResolvedStep Resolve(Workspace ws, StackStep step, int index, ToolStore library = null)
        {
            var result = new ResolvedStep { Index = index, Step = step, Wait = NormalizeWait(step.Wait), Group = step.Group };
            result.Label = FallbackLabel(ws, step);

            if (result.Wait != WaitKind.None && result.Wait != WaitKind.Port && result.Wait != WaitKind.Exit)
                return Broken(result, $"Kiểu chờ \"{step.Wait}\" không hợp lệ.");

            // "custom" không kèm profile là bước lệnh riêng kiểu cũ (ActionId = id lệnh); có profile thì profile trỏ tới lệnh.
            if (ProfileKind.IsLibrary(step.Kind) || (ProfileKind.IsCustom(step.Kind) && !string.IsNullOrEmpty(step.ProfileId)))
                return ResolveRef(ws, step, result, library);
            return string.Equals(step.Kind, TaskKey.Custom, StringComparison.OrdinalIgnoreCase)
                ? ResolveCustom(ws, step, result)
                : ResolveAction(ws, step, result);
        }

        public static IReadOnlyList<ResolvedStep> ResolveAll(Workspace ws, StackDef stack, ToolStore library = null) =>
            stack.Steps.Select((s, i) => Resolve(ws, s, i, library)).ToList();

        /// <summary>
        /// Bước trỏ tới tool thư viện (Kind "library", ActionId = toolId nếu không có profile) hoặc profile loại library/custom.
        /// Tham số thêm của profile (Vals["args"]) nối sau tham số hiệu lực của tool / sau dòng lệnh riêng.
        /// </summary>
        private static ResolvedStep ResolveRef(Workspace ws, StackStep step, ResolvedStep result, ToolStore library)
        {
            Profile profile = null;
            if (!string.IsNullOrEmpty(step.ProfileId))
            {
                profile = ws.Profiles.Find(p => string.Equals(p.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                    return Broken(result, "Profile đã bị xoá.");
                if (!string.Equals(profile.Kind, step.Kind, StringComparison.OrdinalIgnoreCase))
                    return Broken(result, $"Profile \"{profile.Label}\" không còn thuộc loại này.");
            }

            var refId = profile != null ? profile.RefId : step.ActionId;
            if (string.IsNullOrWhiteSpace(refId))
                return Broken(result, "Thiếu tham chiếu tới tool hoặc lệnh riêng (refId).");

            var extra = profile?.ExtraArgs ?? "";
            var argsError = CommandBuilder.CheckExtraArgs(extra);
            if (argsError != null)
                return Broken(result, argsError);

            var port = step.Port > 0 ? step.Port : (int?)null;
            if (result.Wait == WaitKind.Port && port == null)
                return Broken(result, "Bước chờ cổng nhưng chưa khai báo cổng.");

            if (ProfileKind.IsCustom(step.Kind))
            {
                var command = ws.Customs.Find(c => string.Equals(c.Id, refId, StringComparison.OrdinalIgnoreCase));
                if (command == null)
                    return Broken(result, "Lệnh riêng đã bị xoá.");
                var customLabel = profile != null ? profile.Label : command.Label;
                result.Label = customLabel;
                result.Spec = new TaskSpec
                {
                    WorkspaceId = ws.Id,
                    Label = customLabel,
                    CommandLine = CommandBuilder.CustomLine(command, extra),
                    Cwd = ws.ResolveCwd(command.Cwd),
                    Window = command.Window,
                    Port = port,
                    Key = profile != null ? TaskKey.ForProfile(ws.Id, profile) : TaskKey.ForCustom(ws.Id, command.Id),
                };
                return result;
            }

            var binding = ws.FindLibrary(refId);
            if (binding == null)
                return Broken(result, $"Tool \"{refId}\" chưa được gắn vào workspace này.");
            var tool = LibraryStore(library)?.Find(refId);
            if (tool == null)
                return Broken(result, $"Tool \"{binding.Name ?? refId}\" không còn trong thư viện.");
            if (!tool.PathExists)
                return Broken(result, $"Không tìm thấy \"{tool.Name}\": {tool.ResolvedPath}");

            string commandLine;
            try
            {
                commandLine = CommandBuilder.LibraryCommandLine(tool, CommandBuilder.JoinArgs(binding.EffectiveArgs(tool), extra));
            }
            catch (NotSupportedException ex)
            {
                return Broken(result, ex.Message);
            }

            var label = profile != null ? profile.Label : tool.Name;
            result.Label = label;
            result.Spec = new TaskSpec
            {
                WorkspaceId = ws.Id,
                Label = label,
                CommandLine = commandLine,
                Cwd = CommandBuilder.LibraryCwd(tool),
                Port = port,
                Key = profile != null ? TaskKey.ForProfile(ws.Id, profile) : TaskKey.ForAction(ws.Id, ProfileKind.Library, tool.Id, ""),
            };
            return result;
        }

        /// <summary>
        /// Lệnh một lần theo action + tham số, không cần profile (dùng cho agent gọi trực tiếp).
        /// Cùng luật kiểm tra như bước bộ chạy; sai tên action/biến thể hoặc tham số thì trả về bước hỏng kèm lý do.
        /// </summary>
        public static ResolvedStep ResolveAdHoc(Workspace ws, string kind, string actionId, string variantId, IDictionary<string, string> vals)
        {
            var step = new StackStep { Kind = kind, ActionId = actionId, VariantId = variantId };
            var result = new ResolvedStep { Index = 0, Step = step, Wait = WaitKind.None, Label = kind + " · " + actionId };

            var tool = ToolCatalog.Find(kind);
            if (tool == null)
                return Broken(result, $"Tool \"{kind}\" không có trong danh mục.");
            var binding = ws.Binding(kind);
            if (binding == null)
                return Broken(result, $"Tool {tool.Name} chưa được gắn vào workspace này.");
            var action = ToolCatalog.FindAction(ws, binding, actionId);
            if (action == null)
                return Broken(result, $"Không có lệnh \"{actionId}\" của {tool.Name}.");
            var variant = string.IsNullOrWhiteSpace(variantId)
                ? action.Default
                : action.Variants.FirstOrDefault(v => string.Equals(v.Id, variantId, StringComparison.OrdinalIgnoreCase));
            if (variant == null)
                return Broken(result, $"Không có biến thể \"{variantId}\" của lệnh \"{actionId}\".");

            var errors = CommandBuilder.Validate(variant, vals);
            if (errors.Count > 0)
                return Broken(result, string.Join(" ", errors.Values));

            var port = CommandBuilder.PortOf(variant, vals);
            result.Label = ActionLabel(tool, action, variant, port);
            result.Spec = new TaskSpec
            {
                WorkspaceId = ws.Id,
                Label = result.Label,
                CommandLine = CommandBuilder.Build(variant, vals),
                Cwd = ws.ResolveCwd(binding.Cwd),
                Port = port,
                Key = TaskKey.ForAction(ws.Id, tool.Kind, action.Id, variant.Id),
            };
            return result;
        }

        private static ResolvedStep ResolveCustom(Workspace ws, StackStep step, ResolvedStep result)
        {
            var command = ws.Customs.Find(c => string.Equals(c.Id, step.ActionId, StringComparison.OrdinalIgnoreCase));
            if (command == null)
                return Broken(result, "Lệnh riêng đã bị xoá.");

            result.Label = command.Label;
            var port = step.Port > 0 ? step.Port : (int?)null;
            if (result.Wait == WaitKind.Port && port == null)
                return Broken(result, "Bước chờ cổng nhưng chưa khai báo cổng.");

            result.Spec = new TaskSpec
            {
                WorkspaceId = ws.Id,
                Label = command.Label,
                CommandLine = command.CommandLine,
                Cwd = ws.ResolveCwd(command.Cwd),
                Window = command.Window,
                Port = port,
                Key = TaskKey.ForCustom(ws.Id, command.Id),
            };
            return result;
        }

        private static ResolvedStep ResolveAction(Workspace ws, StackStep step, ResolvedStep result)
        {
            var tool = ToolCatalog.Find(step.Kind);
            if (tool == null)
                return Broken(result, $"Tool \"{step.Kind}\" không còn trong danh mục.");
            var binding = ws.Binding(step.Kind);
            if (binding == null)
                return Broken(result, $"Tool {tool.Name} đã bị gỡ khỏi workspace.");
            var action = ToolCatalog.FindAction(ws, binding, step.ActionId);
            if (action == null)
                return Broken(result, $"Lệnh \"{step.ActionId}\" của {tool.Name} không còn (script đã đổi hoặc bị xoá).");

            Profile profile = null;
            if (!string.IsNullOrEmpty(step.ProfileId))
            {
                profile = ws.Profiles.Find(p => string.Equals(p.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                    return Broken(result, "Profile đã bị xoá.");
                if (!string.Equals(profile.Kind, step.Kind, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(profile.ActionId, step.ActionId, StringComparison.OrdinalIgnoreCase))
                    return Broken(result, $"Profile \"{profile.Label}\" không còn thuộc lệnh này.");
            }

            var variant = action.Variant(profile != null ? profile.VariantId : step.VariantId);
            var vals = profile?.Vals;
            var errors = CommandBuilder.Validate(variant, vals);
            if (errors.Count > 0)
                return Broken(result, string.Join(" ", errors.Values));

            var port = CommandBuilder.PortOf(variant, vals) ?? (step.Port > 0 ? step.Port : (int?)null);
            if (result.Wait == WaitKind.Port && port == null)
                return Broken(result, "Bước chờ cổng nhưng lệnh không có cổng; hãy khai báo cổng.");

            var label = profile != null ? profile.Label : ActionLabel(tool, action, variant, port);
            result.Label = label;
            result.Spec = new TaskSpec
            {
                WorkspaceId = ws.Id,
                Label = label,
                CommandLine = CommandBuilder.Build(variant, vals),
                Cwd = ws.ResolveCwd(binding.Cwd),
                Port = port,
                Key = profile != null ? TaskKey.ForProfile(ws.Id, profile) : TaskKey.ForAction(ws.Id, tool.Kind, action.Id, variant.Id),
            };
            return result;
        }

        private static ResolvedStep Broken(ResolvedStep result, string problem)
        {
            result.Problem = problem;
            return result;
        }

        /// <summary>Tên dùng khi chưa/không đối chiếu được: dựa trên id đã lưu.</summary>
        private static string FallbackLabel(Workspace ws, StackStep step)
        {
            if (ProfileKind.IsRef(step.Kind) && !string.IsNullOrEmpty(step.ProfileId))
            {
                var profile = ws.Profiles.Find(p => string.Equals(p.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                    return profile.Label;
            }
            if (ProfileKind.IsLibrary(step.Kind))
            {
                var binding = ws.FindLibrary(step.ActionId ?? "");
                return "Tool thư viện · " + (binding?.Name ?? step.ActionId ?? "(chưa rõ)");
            }
            if (string.Equals(step.Kind, TaskKey.Custom, StringComparison.OrdinalIgnoreCase))
            {
                var command = ws.Customs.Find(c => string.Equals(c.Id, step.ActionId, StringComparison.OrdinalIgnoreCase));
                return "Lệnh riêng · " + (command?.Label ?? "(đã xoá)");
            }
            return (ToolCatalog.Find(step.Kind)?.Name ?? step.Kind) + " · " + step.ActionId;
        }
    }

    public enum StepStatus
    {
        Pending,
        /// <summary>Đã bật (hoặc dùng lại tác vụ đang chạy), đang chờ điều kiện của bước.</summary>
        Running,
        Done,
        Failed,
    }

    public enum RunState
    {
        Running,
        Succeeded,
        Failed,
        /// <summary>Người dùng dừng giữa chừng.</summary>
        Cancelled,
    }

    public sealed class StepProgress
    {
        internal StepProgress(ResolvedStep resolved)
        {
            Resolved = resolved;
        }

        public ResolvedStep Resolved { get; }
        public StepStatus Status { get; internal set; }
        /// <summary>Ghi chú ngắn: "đang chờ cổng 4200", lý do lỗi…</summary>
        public string Detail { get; internal set; }
        /// <summary>Bước này dùng lại tác vụ cùng khoá đang chạy thay vì bật mới.</summary>
        public bool Reused { get; internal set; }
    }

    /// <summary>Một lần chạy bộ chạy: tiến độ từng bước và kết quả. Đọc được từ mọi luồng; <see cref="Changed"/> bắn từ luồng nền.</summary>
    public sealed class StackRun
    {
        private readonly object _lock = new object();
        private readonly List<string> _started = new List<string>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskHost _host;
        private RunState _state = RunState.Running;
        private string _message;

        internal StackRun(TaskHost host, string workspaceId, StackDef stack, IReadOnlyList<ResolvedStep> steps)
        {
            _host = host;
            WorkspaceId = workspaceId;
            StackId = stack.Id;
            Label = stack.Label;
            Steps = steps.Select(s => new StepProgress(s)).ToList();
        }

        public string WorkspaceId { get; }
        public string StackId { get; }
        public string Label { get; }
        public IReadOnlyList<StepProgress> Steps { get; }
        public Task Completion { get; internal set; } = Task.FromResult(0);
        internal CancellationToken Token => _cts.Token;

        public event Action Changed;

        public RunState State { get { lock (_lock) return _state; } }

        /// <summary>Kết quả cho người dùng: thành công, hoặc bước nào lỗi vì sao.</summary>
        public string Message { get { lock (_lock) return _message; } }

        public bool IsActive => State == RunState.Running;

        /// <summary>Id các tác vụ lần chạy này đã bật (không gồm tác vụ dùng lại).</summary>
        public IReadOnlyList<string> StartedTaskIds { get { lock (_lock) return _started.ToArray(); } }

        /// <summary>Còn tác vụ do lần chạy này bật đang chạy.</summary>
        public bool HasRunningTasks => StartedTaskIds.Any(id => _host.Find(id)?.IsRunning == true);

        /// <summary>Thôi chờ và dừng các tác vụ lần chạy này đã bật.</summary>
        public void Stop()
        {
            _cts.Cancel();
            foreach (var id in StartedTaskIds)
                _host.Stop(id);
        }

        internal void AddStarted(string taskId)
        {
            lock (_lock)
                _started.Add(taskId);
        }

        internal void Set(int index, StepStatus status, string detail)
        {
            lock (_lock)
            {
                Steps[index].Status = status;
                Steps[index].Detail = detail;
            }
            Changed?.Invoke();
        }

        internal void Finish(RunState state, string message)
        {
            lock (_lock)
            {
                _state = state;
                _message = message;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Chạy một bộ chạy: các bước tuần tự bằng <see cref="TaskHost"/>. Kiểm hết trước khi bật gì (bước hỏng, thư mục, trùng cổng),
    /// bước đã có tác vụ cùng khoá đang chạy thì dùng lại thay vì bật lần hai. Bước lỗi hoặc hết giờ chờ thì dừng và báo bước nào;
    /// tác vụ đã bật được giữ nguyên để người dùng xem log.
    /// </summary>
    public sealed class StackRunner
    {
        private const string ReusedText = "dùng lại tác vụ đang chạy";

        private readonly TaskHost _host;
        private readonly Func<int, bool> _isListening;
        private readonly object _startLock = new object();

        public StackRunner(TaskHost host, Func<int, bool> isListening = null)
        {
            _host = host;
            _isListening = isListening ?? IsListening;
        }

        public TimeSpan PortTimeout { get; set; } = TimeSpan.FromSeconds(120);

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Bắt đầu chạy. Lỗi kiểm trước trả về run đã ở trạng thái <see cref="RunState.Failed"/>, không bật tiến trình nào.</summary>
        public StackRun Start(Workspace ws, StackDef stack)
        {
            var run = new StackRun(_host, ws.Id, stack, StackResolver.ResolveAll(ws, stack));
            var problem = Precheck(ws, run);
            if (problem != null)
            {
                run.Finish(RunState.Failed, problem);
                return run;
            }
            run.Completion = Task.Run(() => Execute(run));
            return run;
        }

        private string Precheck(Workspace ws, StackRun run)
        {
            var steps = run.Steps;
            if (steps.Count == 0)
                return "Bộ chạy chưa có bước nào.";
            var groupProblem = StackPlan.Validate(steps.Select(s => s.Resolved.Step));
            if (groupProblem != null)
                return groupProblem;
            if (!ws.PathExists)
                return "Không tìm thấy thư mục workspace: " + ws.Path + ". Hãy trỏ lại thư mục trước khi chạy.";

            var broken = steps.Where(s => s.Resolved.IsBroken).ToList();
            if (broken.Count > 0)
            {
                foreach (var s in broken)
                {
                    s.Status = StepStatus.Failed;
                    s.Detail = s.Resolved.Problem;
                }
                var first = broken[0].Resolved;
                return $"Bước {first.Index + 1} \"{first.Label}\" hỏng: {first.Problem}" + (broken.Count > 1 ? $" (và {broken.Count - 1} bước khác)" : "");
            }

            var used = _host.UsedPorts();
            var claimed = new Dictionary<int, string>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in steps)
            {
                var spec = s.Resolved.Spec;
                var n = s.Resolved.Index + 1;
                if (!Directory.Exists(spec.Cwd))
                    return $"Bước {n} \"{s.Resolved.Label}\": không tìm thấy thư mục chạy {spec.Cwd}.";

                // Bước sẽ dùng lại tác vụ đang chạy (hoặc của bước trước cùng khoá) thì không đụng cổng.
                var reused = !seenKeys.Add(spec.Key) || FindRunning(spec.Key) != null;
                if (reused || !spec.Port.HasValue)
                    continue;
                var conflict = PortRules.Conflict(spec.Port.Value, used);
                if (conflict != null)
                    return $"Bước {n} \"{s.Resolved.Label}\": {conflict}";
                if (claimed.TryGetValue(spec.Port.Value, out var other))
                    return $"Bước {n} \"{s.Resolved.Label}\" trùng cổng {spec.Port.Value} với \"{other}\".";
                claimed[spec.Port.Value] = s.Resolved.Label;
            }
            return null;
        }

        private TaskInfo FindRunning(string key) =>
            _host.Running().FirstOrDefault(t => string.Equals(t.Spec.Key, key, StringComparison.OrdinalIgnoreCase));

        private void Execute(StackRun run)
        {
            var current = 0;
            try
            {
                foreach (var batch in StackPlan.Batches(run.Steps.Select(s => s.Resolved.Step)))
                {
                    current = batch[0];
                    run.Token.ThrowIfCancellationRequested();
                    if (batch.Count > 1)
                    {
                        if (!RunGroup(run, batch))
                            return;
                        continue;
                    }
                    var step = run.Steps[current];
                    var failure = RunStep(run, step);
                    if (failure != null)
                    {
                        run.Set(current, StepStatus.Failed, failure);
                        run.Finish(RunState.Failed, $"Dừng ở bước {current + 1} \"{step.Resolved.Label}\": {failure}");
                        return;
                    }
                    run.Set(current, StepStatus.Done, step.Reused ? ReusedText : null);
                }
                run.Finish(RunState.Succeeded, "Đã chạy xong " + run.Steps.Count + " bước.");
            }
            catch (OperationCanceledException)
            {
                run.Finish(RunState.Cancelled, $"Đã dừng ở bước {current + 1}.");
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                var label = run.Steps[current].Resolved.Label;
                run.Set(current, StepStatus.Failed, ex.Message);
                run.Finish(RunState.Failed, $"Dừng ở bước {current + 1} \"{label}\": không chạy được lệnh: {ex.Message}");
            }
        }

        /// <summary>
        /// Chạy một nhóm: mọi bước bắt đầu cùng lúc, mỗi bước theo Wait của riêng nó. Đợi tất cả xong; bước lỗi không kill bước cùng nhóm
        /// nhưng bộ chạy dừng tại ranh giới nhóm. Trả false nếu đã kết thúc bộ chạy (lỗi).
        /// </summary>
        private bool RunGroup(StackRun run, List<int> batch)
        {
            var tasks = batch.Select(i => Task.Run(() => RunStepGuarded(run, run.Steps[i]))).ToArray();
            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException) when (run.Token.IsCancellationRequested)
            {
                throw new OperationCanceledException(run.Token);
            }

            var failed = new List<int>();
            for (var k = 0; k < batch.Count; k++)
            {
                var index = batch[k];
                var failure = tasks[k].Result;
                if (failure != null)
                {
                    failed.Add(index);
                    run.Set(index, StepStatus.Failed, failure);
                }
                else
                {
                    run.Set(index, StepStatus.Done, run.Steps[index].Reused ? ReusedText : null);
                }
            }
            if (failed.Count == 0)
                return true;

            var first = failed[0];
            var message = $"Dừng ở bước {first + 1} \"{run.Steps[first].Resolved.Label}\": {run.Steps[first].Detail}";
            if (failed.Count > 1)
                message += $" (và {failed.Count - 1} bước khác trong nhóm cũng lỗi)";
            run.Finish(RunState.Failed, message);
            return false;
        }

        private string RunStepGuarded(StackRun run, StepProgress step)
        {
            try
            {
                return RunStep(run, step);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                return "không chạy được lệnh: " + ex.Message;
            }
        }

        /// <summary>Chạy một bước; trả lý do lỗi hoặc null nếu bước hoàn tất (đã qua điều kiện chờ).</summary>
        private string RunStep(StackRun run, StepProgress step)
        {
            var resolved = step.Resolved;
            TaskInfo task;
            lock (_startLock)
            {
                task = FindRunning(resolved.Spec.Key);
                if (task == null)
                {
                    task = _host.Start(resolved.Spec.Clone());
                    run.AddStarted(task.Id);
                    // Stop() có thể đến đúng lúc đang bật: không để tác vụ vừa bật sót lại.
                    if (run.Token.IsCancellationRequested)
                        _host.Stop(task.Id);
                    run.Token.ThrowIfCancellationRequested();
                    run.Set(resolved.Index, StepStatus.Running, null);
                }
                else
                {
                    step.Reused = true;
                    run.Set(resolved.Index, StepStatus.Running, ReusedText);
                }
            }

            switch (resolved.Wait)
            {
                case WaitKind.Port:
                    return WaitPort(run, step, task);
                case WaitKind.Exit:
                    return WaitExit(run, step, task);
                default:
                    return null;
            }
        }

        private string WaitPort(StackRun run, StepProgress step, TaskInfo task)
        {
            var resolved = step.Resolved;
            var port = task.Port ?? resolved.Spec.Port ?? 0;
            run.Set(resolved.Index, StepStatus.Running, "đang chờ cổng " + port);
            var deadline = DateTime.UtcNow + PortTimeout;
            while (true)
            {
                if (_isListening(task.Port ?? port))
                    return null;
                if (!task.IsRunning)
                    return task.State == TaskState.Stopped
                        ? "tác vụ bị dừng trước khi mở cổng " + port + "."
                        : $"tác vụ đã thoát (mã {task.ExitCode?.ToString() ?? "?"}) trước khi mở cổng {port}.";
                if (DateTime.UtcNow >= deadline)
                    return $"hết {PortTimeout.TotalSeconds:0} giây chờ cổng {port} mở.";
                Pause(run.Token);
            }
        }

        private string WaitExit(StackRun run, StepProgress step, TaskInfo task)
        {
            run.Set(step.Resolved.Index, StepStatus.Running, "đang chờ lệnh kết thúc");
            while (task.IsRunning)
                Pause(run.Token);
            switch (task.State)
            {
                case TaskState.Done:
                    return null;
                case TaskState.Stopped:
                    return "lệnh bị dừng trước khi kết thúc.";
                default:
                    return $"lệnh thoát với mã {task.ExitCode?.ToString() ?? "?"}.";
            }
        }

        private void Pause(CancellationToken token)
        {
            if (token.WaitHandle.WaitOne(PollInterval))
                token.ThrowIfCancellationRequested();
        }

        /// <summary>Có tiến trình nghe cổng trên máy này (IPv4 hoặc IPv6 loopback; dev server hay chỉ nghe ::1).</summary>
        internal static bool IsListening(int port) =>
            TryConnect(IPAddress.Loopback, port) || TryConnect(IPAddress.IPv6Loopback, port);

        private static bool TryConnect(IPAddress address, int port)
        {
            try
            {
                using (var client = new TcpClient(address.AddressFamily))
                {
                    var pending = client.BeginConnect(address, port, null, null);
                    if (!pending.AsyncWaitHandle.WaitOne(300))
                        return false;
                    client.EndConnect(pending);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
