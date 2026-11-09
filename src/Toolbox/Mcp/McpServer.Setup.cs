using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox.Mcp
{
    /// <summary>
    /// Nhóm tool để agent dùng cấu hình chạy của workspace: xem (list_setup), chạy (run_*), theo dõi và dừng.
    /// Chỉ có khi phiên gắn với một workspace. Dùng cùng luật resolve, cổng và bộ chạy với GUI.
    /// </summary>
    public sealed partial class McpServer
    {
        private const int DefaultTailLines = 30;
        private const int MaxTailLines = 200;

        private SessionRuns Session =>
            _session ?? (_session = new SessionRuns(new TaskHost(_runsDir ?? TaskHost.DefaultRunsDir)));

        private static object[] SetupToolDefinitions() => new object[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "list_setup",
                ["description"] = "Xem cấu hình chạy của workspace: tool và lệnh (action, biến thể, tham số), profile đã đặt tên, bộ chạy (stack), lệnh riêng. "
                    + "Gọi trước khi chạy để lấy đúng id. Bước của bộ chạy có problem khác rỗng thì bước đó sẽ không chạy được.",
                ["inputSchema"] = Schema(new Dictionary<string, object>()),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_profile",
                ["description"] = "Chạy một profile đã đặt tên (đúng tham số đã lưu). waitReady=true: chờ cổng của profile nghe được rồi mới báo xong. "
                    + "Trả về id lần chạy: dùng run_status để xem tiến độ và log.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id profile (lấy từ list_setup)."),
                    ["waitReady"] = Property("boolean", "true: chờ cổng sẵn sàng. Profile phải khai báo cổng."),
                    ["answers"] = Property("array", "Giá trị cho các tham số hỏi lúc chạy (ask), theo thứ tự xuất hiện. Profile có tham số ask thì bắt buộc truyền."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_custom",
                ["description"] = "Chạy một lệnh riêng của workspace. Muốn chờ cổng thì truyền port và waitReady=true.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id lệnh riêng (lấy từ list_setup)."),
                    ["port"] = Property("integer", "Cổng lệnh nghe, dùng để chờ và chống trùng cổng."),
                    ["waitReady"] = Property("boolean", "true: chờ cổng sẵn sàng."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_stack",
                ["description"] = "Chạy một bộ chạy: các bước theo thứ tự, mỗi bước chờ theo cấu hình của bước (cổng hoặc thoát). "
                    + "Trả về id lần chạy; dùng run_status để xem bước nào đang chờ, xong hay hỏng.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id bộ chạy (lấy từ list_setup)."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_action",
                ["description"] = "Chạy một lệnh của tool trong workspace với tham số tuỳ ý, không cần profile. Không chờ cổng: dùng run_status để kiểm tra.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["kind"] = Property("string", "Id tool đã gắn vào workspace (lấy từ list_setup)."),
                    ["action"] = Property("string", "Id action của tool (lấy từ list_setup)."),
                    ["variant"] = Property("string", "Id biến thể. Bỏ trống thì dùng biến thể mặc định."),
                    ["vals"] = Property("object", "Tham số theo key, ví dụ {\"port\": \"4200\"}. Key lấy từ params trong list_setup."),
                }, "kind", "action"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "prepare_worktree",
                ["description"] = "Chuẩn bị một worktree mới của workspace: đưa file local (không commit, ví dụ lockfile hay file môi trường) vào "
                    + "và chạy lệnh cài đặt đã cấu hình trong worktree. Gọi ngay sau khi tạo worktree, trước khi chạy e2e. "
                    + "Không ghi đè file đã khác; đọc status từng file trong kết quả. Lệnh cài đặt chạy như run_stack: dùng run_status để theo dõi.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["path"] = Property("string", "Đường dẫn tuyệt đối tới worktree (đã tạo bằng git worktree add)."),
                    ["install"] = Property("boolean", "false: chỉ đưa file, không chạy lệnh cài đặt. Mặc định true."),
                }, "path"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_status",
                ["description"] = "Trạng thái một lần chạy (run-…) hoặc một tác vụ: running/done/failed/stopped, exitCode, cổng, vài dòng log gần nhất. "
                    + "Chỉ thấy các việc do chính phiên này khởi chạy.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id lần chạy hoặc tác vụ."),
                    ["tail"] = Property("integer", $"Số dòng log gần nhất. Mặc định {DefaultTailLines}, tối đa {MaxTailLines}."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_stop",
                ["description"] = "Dừng một lần chạy (kèm các tác vụ của nó) hoặc một tác vụ do phiên này khởi chạy.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id lần chạy hoặc tác vụ."),
                }, "id"),
            },
        };

        private static readonly HashSet<string> ManageNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "add_tool", "remove_tool", "bind_library_tool",
            "create_profile", "update_profile", "delete_profile",
            "create_custom", "update_custom", "delete_custom",
            "create_stack", "update_stack", "delete_stack",
            "set_worktree",
        };

        private static object[] ManageToolDefinitions() => new object[]
        {
            Tool("add_tool", "Gắn một tool có sẵn trong thư viện (id) vào workspace, kèm thư mục chạy riêng nếu cần. Lệnh được dựng theo file dự án.",
                Props(("kind", "string", "Id tool trong thư viện."), ("cwd", "string", "Thư mục chạy, tương đối so với workspace. Bỏ trống là gốc.")), "kind"),
            Tool("remove_tool", "Gỡ một loại tool khỏi workspace. Từ chối nếu profile hoặc bộ chạy còn dùng.",
                Props(("kind", "string", "Loại tool cần gỡ.")), "kind"),
            Tool("bind_library_tool", "Gắn một tool có sẵn trong thư viện vào workspace. Không đổi bản thân tool. Id lấy từ list_tools.",
                Props(("id", "string", "Id tool trong thư viện.")), "id"),
            Tool("create_profile", "Tạo profile: cách chạy đặt tên cho một lệnh của tool trong workspace. Tham số kiểm đúng luật như khi chạy.",
                Props(("label", "string", "Tên profile."), ("kind", "string", "Loại tool."), ("action", "string", "Id lệnh (xem list_setup)."),
                    ("variant", "string", "Id biến thể. Bỏ trống là mặc định."), ("vals", "object", "Tham số theo key, ví dụ {\"port\": \"4200\"}."),
                    ("pairs", "array", "Tham số dạng cặp theo thứ tự: [{\"key\":\"--project\",\"value\":\"A.csproj\"},{\"key\":\"--dry-run\"},{\"key\":\"\",\"value\":\"all\"},{\"key\":\"--name\",\"ask\":true}]. key rỗng = đối số vị trí; value rỗng = công tắc; key lặp được; key \"--\" tách tham số của app; ask=true: hỏi giá trị lúc chạy.")),
                "label", "kind", "action"),
            Tool("update_profile", "Sửa tên, biến thể hoặc tham số của profile. Không đổi tool và lệnh; muốn đổi thì xoá rồi tạo lại.",
                Props(("id", "string", "Id profile."), ("label", "string", "Tên mới."), ("variant", "string", "Biến thể mới."),
                    ("vals", "object", "Tham số mới (thay toàn bộ)."),
                    ("pairs", "array", "Cặp tham số mới (thay toàn bộ; mảng rỗng là xoá). Tham số dạng cặp theo thứ tự: [{\"key\":\"--project\",\"value\":\"A.csproj\"},{\"key\":\"--dry-run\"},{\"key\":\"\",\"value\":\"all\"},{\"key\":\"--name\",\"ask\":true}]. key rỗng = đối số vị trí; value rỗng = công tắc; key lặp được; key \"--\" tách tham số của app; ask=true: hỏi giá trị lúc chạy.")), "id"),
            Tool("delete_profile", "Xoá profile. Từ chối nếu bộ chạy còn dùng.",
                Props(("id", "string", "Id profile.")), "id"),
            Tool("create_custom", "Tạo lệnh riêng: một dòng lệnh chạy trong thư mục của workspace.",
                Props(("label", "string", "Tên lệnh."), ("commandLine", "string", "Dòng lệnh."), ("cwd", "string", "Thư mục chạy, tương đối so với workspace."),
                    ("window", "boolean", "true: chạy trong cửa sổ terminal riêng.")), "label", "commandLine"),
            Tool("update_custom", "Sửa lệnh riêng. Chỉ các trường được truyền mới đổi.",
                Props(("id", "string", "Id lệnh riêng."), ("label", "string", "Tên mới."), ("commandLine", "string", "Dòng lệnh mới."),
                    ("cwd", "string", "Thư mục chạy mới."), ("window", "boolean", "Chạy trong cửa sổ riêng.")), "id"),
            Tool("delete_custom", "Xoá lệnh riêng. Từ chối nếu bộ chạy còn dùng.",
                Props(("id", "string", "Id lệnh riêng.")), "id"),
            Tool("create_stack", "Tạo bộ chạy: các bước theo thứ tự. Mỗi bước: kind (loại tool, hoặc custom), action (id lệnh; khi kind=custom là id lệnh riêng), "
                    + "variant, profile, wait (none|port|exit), port. Bước hỏng thì từ chối cả bộ.",
                Props(("label", "string", "Tên bộ chạy."), ("steps", "array", "Các bước, mỗi bước là object như mô tả ở trên.")), "label", "steps"),
            Tool("update_stack", "Sửa tên hoặc các bước của bộ chạy. Truyền steps thì thay toàn bộ các bước.",
                Props(("id", "string", "Id bộ chạy."), ("label", "string", "Tên mới."), ("steps", "array", "Các bước mới (thay toàn bộ).")), "id"),
            Tool("delete_stack", "Xoá bộ chạy.",
                Props(("id", "string", "Id bộ chạy.")), "id"),
            Tool("set_worktree", "Cấu hình worktree: file local đưa vào (files) và lệnh cài đặt (command). Truyền files=[] hoặc command=\"\" để xoá.",
                Props(("files", "array", "File tương đối so với workspace."), ("command", "string", "Lệnh cài đặt trong worktree."))),
        };

        private static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> properties, params string[] required) =>
            new Dictionary<string, object>
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = Schema(properties, required),
            };

        private static Dictionary<string, object> Props(params (string Key, string Type, string Description)[] props)
        {
            var map = new Dictionary<string, object>();
            foreach (var p in props)
                map[p.Key] = p.Type == "array"
                    ? (object)new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        // Bước của bộ chạy là object; còn lại là danh sách chuỗi (files).
                        ["items"] = new Dictionary<string, object> { ["type"] = p.Key == "steps" ? "object" : "string" },
                        ["description"] = p.Description,
                    }
                    : Property(p.Type, p.Description);
            return map;
        }

        private object SetupCall(string name, Dictionary<string, object> arguments, Workspace workspace)
        {
            if (workspace == null)
                return ToolResult("Chỉ dùng được khi kết nối agent với một workspace (toolbox --mcp --workspace <id>).", isError: true);

            if (ManageNames.Contains(name))
                return Manage(name, arguments, workspace);

            switch (name)
            {
                case "list_setup":
                    return ListSetup(workspace);
                case "run_profile":
                    return RunProfile(arguments, workspace);
                case "run_custom":
                    return RunCustom(arguments, workspace);
                case "run_stack":
                    return RunStack(arguments, workspace);
                case "run_action":
                    return RunAction(arguments, workspace);
                case "prepare_worktree":
                    return PrepareWorktree(arguments, workspace);
                case "run_status":
                    return RunStatus(arguments);
                default:
                    return RunStop(arguments);
            }
        }

        private object ListSetup(Workspace workspace)
        {
            var view = new Dictionary<string, object>
            {
                ["workspace"] = workspace.Name,
                ["tools"] = workspace.Bindings.Select(b => (object)ToolView(workspace, b)).ToArray(),
                ["profiles"] = workspace.Profiles.Select(p => (object)new Dictionary<string, object>
                {
                    ["id"] = p.Id,
                    ["label"] = p.Label,
                    ["kind"] = p.Kind,
                    ["action"] = p.ActionId,
                    ["variant"] = p.VariantId ?? "",
                    ["refId"] = p.RefId ?? "",
                    ["vals"] = p.Vals,
                }).ToArray(),
                ["stacks"] = workspace.Stacks.Select(s => (object)new Dictionary<string, object>
                {
                    ["id"] = s.Id,
                    ["label"] = s.Label,
                    ["steps"] = StackResolver.ResolveAll(workspace, s).Select(r => (object)new Dictionary<string, object>
                    {
                        ["index"] = r.Index + 1,
                        ["label"] = r.Label,
                        ["wait"] = r.Wait,
                        ["group"] = r.Group,
                        ["problem"] = r.Problem ?? "",
                    }).ToArray(),
                }).ToArray(),
                ["worktree"] = new Dictionary<string, object>
                {
                    ["files"] = (workspace.Worktree ?? new WorktreeSetup()).Files.ToArray(),
                    ["command"] = (workspace.Worktree ?? new WorktreeSetup()).Command ?? "",
                },
                ["customs"] = workspace.Customs.Select(c => (object)new Dictionary<string, object>
                {
                    ["id"] = c.Id,
                    ["label"] = c.Label,
                    ["commandLine"] = c.CommandLine,
                    ["cwd"] = workspace.ResolveCwd(c.Cwd),
                    ["window"] = c.Window,
                }).ToArray(),
            };
            return ToolResult(_json.Serialize(view), isError: false);
        }

        private static Dictionary<string, object> ToolView(Workspace workspace, ToolBinding binding)
        {
            var tool = ToolCatalog.Find(binding.ToolId);
            return new Dictionary<string, object>
            {
                ["kind"] = binding.ToolId,
                ["name"] = tool?.Name ?? binding.ToolId,
                ["cwd"] = workspace.ResolveCwd(binding.Cwd),
                ["actions"] = ToolCatalog.ActionsFor(workspace, binding).Select(a => (object)new Dictionary<string, object>
                {
                    ["id"] = a.Id,
                    ["label"] = a.Label,
                    ["variants"] = a.Variants.Select(v => (object)new Dictionary<string, object>
                    {
                        ["id"] = v.Id,
                        ["label"] = v.Label,
                        ["params"] = v.Params.Select(p => (object)new Dictionary<string, object>
                        {
                            ["key"] = p.Key,
                            ["prompt"] = p.Prompt,
                            ["default"] = p.Default ?? "",
                            ["isPort"] = p.IsPort,
                        }).ToArray(),
                    }).ToArray(),
                }).ToArray(),
            };
        }

        private object RunProfile(Dictionary<string, object> arguments, Workspace workspace)
        {
            var id = GetString(arguments, "id");
            var profile = workspace.Profiles.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
                return ToolResult($"Không có profile \"{id}\". Dùng list_setup để xem id.", isError: true);

            var answers = GetStrings(arguments, "answers");
            var pending = profile.WithAnswers(answers).PendingAsks;
            if (pending.Count > 0)
            {
                var all = profile.PendingAsks.Select((p, i) => $"{i + 1}. " + (string.IsNullOrWhiteSpace(p.Key) ? "đối số vị trí" : p.Key.Trim()));
                return ToolResult("Profile cần giá trị lúc chạy. Truyền answers (mảng chuỗi) theo thứ tự: " + string.Join("; ", all) + ".", isError: true);
            }

            var step = new StackStep
            {
                Kind = profile.Kind,
                ActionId = profile.ActionId,
                VariantId = profile.VariantId,
                ProfileId = profile.Id,
                Port = IntArg(arguments, "port"),
                Wait = WaitOf(arguments),
                Answers = answers,
            };
            return LaunchStack(workspace, new StackDef { Id = profile.Id, Label = profile.Label, Steps = { step } });
        }

        /// <summary>
        /// Trong khối using, bước tool và kiểm profile tra thư viện trong kho của server (không phải file mặc định).
        /// Kho hỏng thì bỏ qua và để bộ resolve báo lỗi.
        /// </summary>
        private IDisposable LibraryScope()
        {
            ToolStore store = null;
            try { store = _loadStore?.Invoke(); }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException) { }
            return StackResolver.UseLibrary(store);
        }

        /// <summary>Gộp tham số "args" (của lời gọi save) vào Vals["args"] cho profile chạy lệnh gốc hoặc lệnh riêng.</summary>
        private static Dictionary<string, string> WithArgs(Dictionary<string, object> a, Dictionary<string, string> vals, IDictionary<string, string> current = null)
        {
            var args = GetString(a, ProfileKind.ArgsKey);
            if (args == null)
                return vals;
            var merged = new Dictionary<string, string>(vals ?? current ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            merged[ProfileKind.ArgsKey] = args;
            return merged;
        }

        private object RunCustom(Dictionary<string, object> arguments, Workspace workspace)
        {
            var id = GetString(arguments, "id");
            var command = workspace.Customs.Find(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (command == null)
                return ToolResult($"Không có lệnh riêng \"{id}\". Dùng list_setup để xem id.", isError: true);

            var step = new StackStep
            {
                Kind = TaskKey.Custom,
                ActionId = command.Id,
                Port = IntArg(arguments, "port"),
                Wait = WaitOf(arguments),
            };
            return LaunchStack(workspace, new StackDef { Id = command.Id, Label = command.Label, Steps = { step } });
        }

        private object RunStack(Dictionary<string, object> arguments, Workspace workspace)
        {
            var id = GetString(arguments, "id");
            var stack = workspace.Stacks.Find(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (stack == null)
                return ToolResult($"Không có bộ chạy \"{id}\". Dùng list_setup để xem id.", isError: true);
            return LaunchStack(workspace, stack);
        }

        private object RunAction(Dictionary<string, object> arguments, Workspace workspace)
        {
            var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (arguments.TryGetValue("vals", out var raw) && raw is Dictionary<string, object> map)
                foreach (var pair in map)
                    vals[pair.Key] = pair.Value?.ToString() ?? "";

            var resolved = StackResolver.ResolveAdHoc(
                workspace, GetString(arguments, "kind") ?? "", GetString(arguments, "action") ?? "", GetString(arguments, "variant"), vals);
            try
            {
                var task = Session.StartResolved(resolved, out var reused);
                var view = Session.Status(task.Id, 0);
                view["reused"] = reused;
                return ToolResult(_json.Serialize(view), isError: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is Win32Exception)
            {
                return ToolResult(ex.Message, isError: true);
            }
        }

        private object RunStatus(Dictionary<string, object> arguments)
        {
            var id = GetString(arguments, "id");
            var tail = Math.Max(0, Math.Min(MaxTailLines, IntArg(arguments, "tail", DefaultTailLines)));
            var view = Session.Status(id, tail);
            return view == null
                ? ToolResult($"Phiên này không có lần chạy hay tác vụ \"{id}\". Chỉ thấy việc do chính phiên này khởi chạy.", isError: true)
                : ToolResult(_json.Serialize(view), isError: false);
        }

        private object RunStop(Dictionary<string, object> arguments)
        {
            var id = GetString(arguments, "id");
            return Session.Stop(id)
                ? ToolResult($"Đã yêu cầu dừng \"{id}\".", isError: false)
                : ToolResult($"Phiên này không có \"{id}\" để dừng.", isError: true);
        }

        /// <summary>
        /// Ghi cấu hình workspace: đọc-sửa-ghi trong khoá liên tiến trình, trên file mới nhất. Lỗi thì không ghi gì.
        /// </summary>
        private object Write(Func<Workspace, string> change, Func<string> success)
        {
            var store = _loadWorkspaces?.Invoke();
            if (store == null)
                return ToolResult("Không ghi được cấu hình workspace.", isError: true);

            string error = null;
            // LibraryScope đọc kho tool (khoá tool). Mở nó TRƯỚC khi giữ khoá workspace: WriteTool khoá theo thứ tự
            // tool rồi workspace, nên đảo thứ tự ở đây làm hai luồng chờ nhau tới hết hạn khoá (10 giây).
            using (LibraryScope())
            {
                WorkspaceStore.Update(store.FilePath, fresh =>
                {
                    var target = fresh.Find(_workspaceId);
                    if (target == null)
                    {
                        error = $"Workspace \"{_workspaceId}\" chưa được đăng ký trong Toolbox.";
                        return false;
                    }
                    error = change(target);
                    return error == null;
                });
            }
            return error != null ? ToolResult(error, isError: true) : ToolResult(success(), isError: false);
        }

        private object Manage(string name, Dictionary<string, object> a, Workspace workspace)
        {
            switch (name)
            {
                case "add_tool":
                    return Write(ws => WorkspaceEditor.AddTool(ws, GetString(a, "kind"), GetString(a, "cwd")),
                        () => $"Đã thêm tool {GetString(a, "kind")} vào workspace.");
                case "remove_tool":
                    return Write(ws => WorkspaceEditor.RemoveTool(ws, GetString(a, "kind")),
                        () => $"Đã gỡ tool {GetString(a, "kind")} khỏi workspace.");
                case "bind_library_tool":
                {
                    var id = GetString(a, "id");
                    return Write(ws =>
                    {
                        var tool = _loadStore().Find(id ?? "");
                        return tool == null
                            ? $"Không có tool \"{id}\" trong thư viện. Dùng list_tools để xem id."
                            : WorkspaceEditor.BindLibrary(ws, tool);
                    }, () => $"Đã gắn \"{id}\" vào workspace.");
                }
                case "create_profile":
                {
                    var profile = new Profile
                    {
                        Label = GetString(a, "label"),
                        Kind = GetString(a, "kind"),
                        ActionId = GetString(a, "action"),
                        VariantId = GetString(a, "variant"),
                        RefId = GetString(a, "refId"),
                        Vals = ParseVals(a),
                        Pairs = ParsePairs(a),
                    };
                    if (ProfileKind.IsRef(profile.Kind) || string.IsNullOrWhiteSpace(profile.ActionId))
                        profile.Vals = WithArgs(a, profile.Vals);
                    return Write(ws => WorkspaceEditor.CreateProfile(ws, profile),
                        () => $"Đã tạo profile \"{profile.Label}\" (id {profile.Id}).");
                }
                case "update_profile":
                    return Write(ws =>
                    {
                        var current = ws.Profiles.Find(p => string.Equals(p.Id, GetString(a, "id"), StringComparison.OrdinalIgnoreCase));
                        var vals = ParseVals(a);
                        if (current != null && (ProfileKind.IsCustom(current.Kind) || string.IsNullOrEmpty(current.ActionId)))
                            vals = WithArgs(a, vals, current.Vals);
                        return WorkspaceEditor.UpdateProfile(ws, GetString(a, "id"), GetString(a, "label"), GetString(a, "variant"), vals, ParsePairs(a));
                    }, () => $"Đã cập nhật profile {GetString(a, "id")}.");
                case "delete_profile":
                    return Write(ws => WorkspaceEditor.DeleteProfile(ws, GetString(a, "id")), () => $"Đã xoá profile {GetString(a, "id")}.");
                case "create_custom":
                {
                    var command = new CustomCommand
                    {
                        Label = GetString(a, "label"),
                        CommandLine = GetString(a, "commandLine"),
                        Cwd = GetString(a, "cwd"),
                        Window = BoolArg(a, "window") ?? false,
                    };
                    return Write(ws => WorkspaceEditor.CreateCustom(ws, command),
                        () => $"Đã tạo lệnh riêng \"{command.Label}\" (id {command.Id}).");
                }
                case "update_custom":
                    return Write(ws => WorkspaceEditor.UpdateCustom(ws, GetString(a, "id"), GetString(a, "label"), GetString(a, "commandLine"),
                        GetString(a, "cwd"), BoolArg(a, "window")), () => $"Đã cập nhật lệnh riêng {GetString(a, "id")}.");
                case "delete_custom":
                    return Write(ws => WorkspaceEditor.DeleteCustom(ws, GetString(a, "id")), () => $"Đã xoá lệnh riêng {GetString(a, "id")}.");
                case "create_stack":
                {
                    var stack = new StackDef { Label = GetString(a, "label"), Steps = ParseSteps(a) ?? new List<StackStep>() };
                    return Write(ws => WorkspaceEditor.CreateStack(ws, stack),
                        () => $"Đã tạo bộ chạy \"{stack.Label}\" (id {stack.Id}).");
                }
                case "update_stack":
                    return Write(ws => WorkspaceEditor.UpdateStack(ws, GetString(a, "id"), GetString(a, "label"), ParseSteps(a)),
                        () => $"Đã cập nhật bộ chạy {GetString(a, "id")}.");
                case "delete_stack":
                    return Write(ws => WorkspaceEditor.DeleteStack(ws, GetString(a, "id")), () => $"Đã xoá bộ chạy {GetString(a, "id")}.");
                default:
                    return Write(ws =>
                    {
                        WorkspaceEditor.SetWorktree(ws, GetStrings(a, "files"), GetString(a, "command"));
                        return null;
                    }, () => "Đã lưu cấu hình worktree.");
            }
        }

        /// <summary>Danh sách cặp tham số; null nếu không truyền. Phần tử sai kiểu thì báo lỗi đầu vào.</summary>
        private static List<ParamPair> ParsePairs(Dictionary<string, object> a)
        {
            if (!a.TryGetValue("pairs", out var raw) || !(raw is IEnumerable items) || raw is string)
                return null;
            var pairs = new List<ParamPair>();
            foreach (var item in items)
            {
                if (!(item is Dictionary<string, object> d))
                    throw new ArgumentException("Mỗi cặp phải là object, ví dụ {\"key\":\"--project\",\"value\":\"A.csproj\"}.");
                pairs.Add(new ParamPair(GetString(d, "key"), GetString(d, "value"), BoolArg(d, "ask") ?? false));
            }
            return pairs;
        }

        private static Dictionary<string, string> ParseVals(Dictionary<string, object> a)
        {
            if (!a.TryGetValue("vals", out var raw) || !(raw is Dictionary<string, object> map))
                return null;
            var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
                vals[pair.Key] = pair.Value?.ToString() ?? "";
            return vals;
        }

        /// <summary>Danh sách bước; null nếu không truyền. Bước sai kiểu thì báo lỗi đầu vào.</summary>
        private static List<StackStep> ParseSteps(Dictionary<string, object> a)
        {
            if (!a.TryGetValue("steps", out var raw) || !(raw is IEnumerable items) || raw is string)
                return null;
            var steps = new List<StackStep>();
            foreach (var item in items)
            {
                if (!(item is Dictionary<string, object> d))
                    throw new ArgumentException("Mỗi bước phải là object, ví dụ {\"kind\":\"<id tool>\",\"action\":\"start\",\"wait\":\"port\",\"port\":4200}.");
                steps.Add(new StackStep
                {
                    Kind = GetString(d, "kind"),
                    ActionId = GetString(d, "action"),
                    VariantId = GetString(d, "variant"),
                    ProfileId = GetString(d, "profile"),
                    Wait = GetString(d, "wait"),
                    Port = IntArg(d, "port"),
                    Group = IntArg(d, "group"),
                });
            }
            return steps;
        }

        private static bool? BoolArg(Dictionary<string, object> a, string key) =>
            a.TryGetValue(key, out var value) && value is bool b ? (bool?)b : null;

        private object PrepareWorktree(Dictionary<string, object> arguments, Workspace workspace)
        {
            var path = GetString(arguments, "path");
            var report = WorktreePreparer.Prepare(workspace, path);
            if (report.Error != null)
                return ToolResult(report.Error, isError: true);

            var view = new Dictionary<string, object>
            {
                ["ok"] = report.Ok,
                ["files"] = report.Lines.Select(l => (object)new Dictionary<string, object>
                {
                    ["file"] = l.File,
                    ["status"] = l.Status,
                    ["detail"] = l.Detail,
                }).ToArray(),
            };

            var command = workspace.Worktree?.Command;
            var install = !arguments.TryGetValue("install", out var raw) || !(raw is bool flag) || flag;
            var installOk = true;
            if (install && !string.IsNullOrWhiteSpace(command))
            {
                var full = Path.GetFullPath(path);
                try
                {
                    var task = Session.StartSpec(new TaskSpec
                    {
                        WorkspaceId = workspace.Id,
                        Label = "Cài đặt worktree",
                        CommandLine = command,
                        Cwd = full,
                        Key = string.Join("|", workspace.Id, "worktree", "install", full, ""),
                    }, out var reused);
                    var status = Session.Status(task.Id, 0);
                    status["reused"] = reused;
                    view["install"] = status;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is Win32Exception)
                {
                    installOk = false;
                    view["installError"] = ex.Message;
                }
            }
            return ToolResult(_json.Serialize(view), isError: !report.Ok || !installOk);
        }

        private object LaunchStack(Workspace workspace, StackDef stack)
        {
            string id;
            try
            {
                using (LibraryScope())
                    id = Session.StartStack(workspace, stack);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is Win32Exception)
            {
                return ToolResult(ex.Message, isError: true);
            }

            var view = Session.Status(id, 0);
            var failed = string.Equals(view["state"] as string, "failed", StringComparison.Ordinal);
            return ToolResult(_json.Serialize(view), isError: failed);
        }

        private static string WaitOf(Dictionary<string, object> arguments) =>
            arguments.TryGetValue("waitReady", out var v) && v is bool b && b ? WaitKind.Port : WaitKind.None;

        private static int IntArg(Dictionary<string, object> arguments, string key, int fallback = 0)
        {
            if (!arguments.TryGetValue(key, out var value) || value == null)
                return fallback;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return fallback;
            }
        }
    }
}
