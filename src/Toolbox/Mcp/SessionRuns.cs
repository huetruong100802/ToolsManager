using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Mcp
{
    /// <summary>
    /// Tác vụ do một phiên MCP khởi chạy. Sống trong tiến trình MCP và được dừng khi phiên kết thúc,
    /// nên không để lại dev server mồ côi. Dùng chung luật resolve, cổng và bộ chạy với GUI.
    /// </summary>
    public sealed class SessionRuns : IDisposable
    {
        /// <summary>Tiền tố id của lần chạy bộ chạy, để phân biệt với id tác vụ.</summary>
        public const string RunPrefix = "run-";

        private readonly object _sync = new object();
        private readonly Dictionary<string, StackRun> _runs = new Dictionary<string, StackRun>();
        private readonly TaskHost _host;
        private readonly StackRunner _stacks;
        private bool _disposed;

        public SessionRuns(TaskHost host)
        {
            _host = host;
            _stacks = new StackRunner(_host);
        }

        /// <summary>
        /// Chạy một bước đã resolve. Nếu đã có tác vụ cùng khoá đang chạy trong phiên thì dùng lại, không bật lần hai.
        /// Ném <see cref="InvalidOperationException"/> nếu bước hỏng, thư mục không còn, hoặc trùng cổng.
        /// </summary>
        public TaskInfo StartResolved(ResolvedStep resolved, out bool reused)
        {
            if (resolved.IsBroken)
                throw new InvalidOperationException(resolved.Problem);
            return StartSpec(resolved.Spec, out reused);
        }

        /// <summary>Bật một tác vụ từ spec. Dùng lại tác vụ cùng khoá đang chạy; kiểm thư mục và trùng cổng trước khi bật.</summary>
        public TaskInfo StartSpec(TaskSpec spec, out bool reused)
        {
            var running = _host.Running().FirstOrDefault(t => t.Spec.Key == spec.Key);
            if (running != null)
            {
                reused = true;
                return running;
            }

            if (!Directory.Exists(spec.Cwd))
                throw new InvalidOperationException($"Không tìm thấy thư mục chạy {spec.Cwd}.");
            if (spec.Port.HasValue)
            {
                var conflict = PortRules.Conflict(spec.Port.Value, _host.UsedPorts());
                if (conflict != null)
                    throw new InvalidOperationException(conflict);
            }

            EnsurePortFree(spec);
            reused = false;
            return _host.Start(spec);
        }

        /// <summary>
        /// Cổng đã có tiến trình khác nghe (app đang chạy trong GUI, terminal khác) thì từ chối. Nếu không, chờ cổng
        /// sẽ báo sẵn sàng nhầm, và lệnh mới sẽ lỗi bind. Tác vụ cùng khoá của phiên này đang chạy thì không kiểm.
        /// </summary>
        private void EnsurePortFree(TaskSpec spec)
        {
            if (!spec.Port.HasValue || _host.Running().Any(t => t.Spec.Key == spec.Key))
                return;
            if (StackRunner.IsListening(spec.Port.Value))
                throw new InvalidOperationException(
                    $"Cổng {spec.Port.Value} đã có tiến trình khác đang nghe (có thể từ Toolbox hoặc terminal khác). Dừng tiến trình đó hoặc đổi cổng.");
        }

        /// <summary>Chạy một bộ chạy. Trả về id để theo dõi bằng <see cref="Status"/>.</summary>
        public string StartStack(Workspace workspace, StackDef stack)
        {
            foreach (var step in StackResolver.ResolveAll(workspace, stack))
                if (!step.IsBroken)
                    EnsurePortFree(step.Spec);
            var run = _stacks.Start(workspace, stack);
            var id = RunPrefix + WorkspaceStore.NewId();
            lock (_sync)
                _runs[id] = run;
            return id;
        }

        /// <summary>Trạng thái một lần chạy bộ chạy hoặc một tác vụ; null nếu phiên này không có id đó.</summary>
        public Dictionary<string, object> Status(string id, int tailLines)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            StackRun run;
            lock (_sync)
                _runs.TryGetValue(id, out run);
            if (run != null)
                return StackView(id, run);

            var task = _host.Find(id);
            return task == null ? null : TaskView(task, tailLines);
        }

        /// <summary>Dừng một lần chạy bộ chạy (kèm tác vụ của nó) hoặc một tác vụ. False nếu phiên này không có id đó.</summary>
        public bool Stop(string id)
        {
            StackRun run;
            lock (_sync)
                _runs.TryGetValue(id ?? "", out run);
            if (run != null)
            {
                run.Stop();
                return true;
            }
            if (_host.Find(id ?? "") == null)
                return false;
            _host.Stop(id);
            return true;
        }

        /// <summary>Dừng mọi việc của phiên. Gọi khi MCP kết thúc để không bỏ lại tiến trình chạy ngầm.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (var run in _runs.Values)
                    run.Stop();
            }
            _host.StopAll();
        }

        private static Dictionary<string, object> TaskView(TaskInfo task, int tailLines)
        {
            var lines = task.Lines();
            return new Dictionary<string, object>
            {
                ["kind"] = "task",
                ["id"] = task.Id,
                ["label"] = task.Spec.Label,
                ["state"] = task.State.ToString().ToLowerInvariant(),
                ["exitCode"] = task.ExitCode.HasValue ? (object)task.ExitCode.Value : null,
                ["port"] = task.Port.HasValue ? (object)task.Port.Value : null,
                ["startedAt"] = task.StartedAt.ToString("o"),
                ["logPath"] = task.LogPath ?? "",
                ["tail"] = lines.Skip(Math.Max(0, lines.Length - Math.Max(0, tailLines))).ToArray(),
            };
        }

        private Dictionary<string, object> StackView(string id, StackRun run)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = "run",
                ["id"] = id,
                ["label"] = run.Label,
                ["state"] = run.State.ToString().ToLowerInvariant(),
                ["message"] = run.Message ?? "",
                ["steps"] = run.Steps.Select(s => (object)new Dictionary<string, object>
                {
                    ["index"] = s.Resolved.Index + 1,
                    ["label"] = s.Resolved.Label,
                    ["status"] = s.Status.ToString().ToLowerInvariant(),
                    ["detail"] = s.Detail ?? "",
                    ["reused"] = s.Reused,
                }).ToArray(),
                ["startedTaskIds"] = run.StartedTaskIds.ToArray(),
            };
        }
    }
}
