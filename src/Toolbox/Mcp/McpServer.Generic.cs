using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;

namespace Toolbox.Mcp
{
    /// <summary>
    /// Bộ công cụ gom cho agent trong workspace: số công cụ cố định (12) dù workspace có 5 hay 500 profile.
    /// Mỗi công cụ nhận <c>type</c> (profile, stack, custom, tool, library) và <c>id</c>, rồi chuyển sang các công cụ cũ để dùng lại đúng luật kiểm tra.
    /// Tên cũ vẫn gọi được nhưng không còn được quảng bá trong tools/list.
    /// </summary>
    public sealed partial class McpServer
    {
        private const int DefaultSearchLimit = 20;
        private const int MaxSearchLimit = 100;
        private const int DefaultLogLines = 200;

        private static readonly string[] ResourceTypes = { "profile", "stack", "custom", "tool", "library", "variablegroup" };

        private static readonly HashSet<string> GenericNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "overview", "search", "get", "save", "delete", "attach", "run", "logs", "propose",
        };

        // Công cụ của bộ cũ được giữ lại trong bộ gom, không đổi.
        private static readonly string[] KeptNames = { "run_status", "run_stop", "prepare_worktree" };

        private static object[] GenericToolDefinitions()
        {
            var typeDoc = "Loại: profile, stack (bộ chạy), custom (lệnh riêng), tool (tool đã gắn vào workspace), library (cả thư viện), "
                + "variablegroup (nhóm biến do người dùng tạo; chỉ đọc, không có giá trị biến).";
            var generic = new object[]
            {
                Tool("overview", "Tóm tắt workspace: số tool, profile, bộ chạy, lệnh riêng, nhóm biến agent được dùng, đề xuất đang chờ duyệt, quyền chạy của agent. Gọi đầu tiên; không liệt kê chi tiết.",
                    Props()),
                Tool("search", "Tìm trong workspace hoặc thư viện, có phân trang. Dùng thay vì đọc cả danh sách: workspace có thể có hàng trăm profile.",
                    Props(("type", "string", typeDoc + " Bỏ trống: tìm trong profile, stack, custom, tool (nhóm biến chỉ hiện khi type=variablegroup)."), ("query", "string", "Từ khoá, không phân biệt dấu. Bỏ trống: liệt kê."),
                        ("limit", "integer", $"Số dòng tối đa. Mặc định {DefaultSearchLimit}, tối đa {MaxSearchLimit}."), ("offset", "integer", "Bỏ qua bao nhiêu dòng đầu, để xem trang tiếp."))),
                Tool("get", "Xem đầy đủ một mục theo id: profile, stack, custom hoặc tool. Không trả đường dẫn máy.",
                    Props(("type", "string", typeDoc), ("id", "string", "Id lấy từ search.")), "type", "id"),
                Tool("save", "Tạo hoặc sửa một mục trong workspace. Có id: sửa; không có id: tạo. Thêm, sửa, xoá trong workspace là việc agent tự làm được. "
                        + "type=tool mà có id thì chỉ gửi đề xuất sửa tool dùng chung để người dùng duyệt (kèm reason). "
                        + "Trường theo loại: profile (label, kind, action, variant, vals; kind = id tool, action rỗng = chạy lệnh gốc kèm args; kind=custom thì dùng refId và args), stack (label, steps), custom (label, commandLine, cwd, window), tool (name, path, args, description, tags, notes).",
                    Props(("type", "string", "profile, stack, custom hoặc tool."), ("id", "string", "Id mục cần sửa. Bỏ trống để tạo mới."),
                        ("label", "string", "Tên profile, bộ chạy hoặc lệnh riêng."), ("kind", "string", "Profile: id tool đã gắn vào workspace (search type=tool), hoặc custom (lệnh riêng)."),
                        ("refId", "string", "Profile custom: id lệnh riêng. Tham số thêm gửi ở args."),
                        ("action", "string", "Profile: hậu tố (id lệnh); bỏ trống = chạy lệnh gốc của tool."), ("variant", "string", "Profile: id biến thể."), ("vals", "object", "Profile: tham số theo key."),
                        ("pairs", "array", "Profile: Tham số dạng cặp theo thứ tự: [{\"key\":\"--project\",\"value\":\"A.csproj\"},{\"key\":\"--dry-run\"},{\"key\":\"\",\"value\":\"all\"},{\"key\":\"--name\",\"ask\":true}]. key rỗng = đối số vị trí; value rỗng = công tắc; key lặp được; key \"--\" tách tham số của app; ask=true: hỏi giá trị lúc chạy."),
                        ("variableGroup", "string", VariableGroupLinkDoc),
                        ("steps", "array", "Bộ chạy: các bước (kind, action, variant, profile, wait, port, group). group > 0: các bước liền nhau cùng số chạy song song; bước sau nhóm chờ mọi bước trong nhóm đạt wait."),
                        ("commandLine", "string", "Lệnh riêng: dòng lệnh."), ("cwd", "string", "Thư mục chạy, tương đối so với workspace."), ("window", "boolean", "Lệnh riêng: chạy trong cửa sổ riêng."),
                        ("name", "string", "Tool: tên hiển thị."), ("path", "string", "Tool mới: đường dẫn tuyệt đối."), ("args", "string", "Tool: tham số mặc định."),
                        ("description", "string", "Tool: mô tả."), ("tags", "array", "Tool: tag."), ("notes", "string", "Tool: ghi chú."),
                        ("reason", "string", "Khi sửa tool dùng chung: lý do để người dùng quyết định.")), "type"),
                Tool("delete", "Xoá một mục khỏi workspace. type=tool chỉ gỡ khỏi workspace, tool vẫn còn trong thư viện. Từ chối nếu bộ chạy còn dùng mục đó.",
                    Props(("type", "string", "profile, stack, custom hoặc tool."), ("id", "string", "Id mục cần xoá.")), "type", "id"),
                Tool("attach", "Gắn vào workspace một tool có sẵn trong thư viện (id hoặc kind, tìm bằng search type=library). Không đổi tool.",
                    Props(("id", "string", "Id tool trong thư viện."), ("kind", "string", "Như id, kèm cwd nếu cần thư mục chạy riêng."), ("cwd", "string", "Thư mục chạy cho kind, tương đối so với workspace."))),
                Tool("run", "Chạy một mục: profile, stack, custom, tool (tool thư viện đã gắn) hoặc action (lệnh của tool dựng sẵn, không cần profile). Trả về id lần chạy; dùng run_status hoặc logs để theo dõi.",
                    Props(("type", "string", "profile, stack, custom, tool hoặc action."), ("id", "string", "Id mục cần chạy (không cần với action)."),
                        ("waitReady", "boolean", "Chờ cổng nghe được rồi mới báo xong."), ("port", "integer", "Cổng, với custom."),
                        ("args", "string", "Tool thư viện: tham số thay cho mặc định."), ("kind", "string", "Action: loại tool."), ("action", "string", "Action: id lệnh."),
                        ("variant", "string", "Action: id biến thể."), ("vals", "object", "Action: tham số theo key."),
                        ("answers", "array", "Profile: giá trị cho các tham số hỏi lúc chạy (ask), theo thứ tự."),
                        ("variableGroup", "string", "Custom, action: " + VariableGroupRunDoc)), "type"),
                Tool("logs", $"Xem log gần nhất của một lần chạy hoặc tác vụ do phiên này khởi chạy. Mặc định {DefaultLogLines} dòng.",
                    Props(("id", "string", "Id lần chạy hoặc tác vụ."), ("tail", "integer", $"Số dòng. Tối đa {MaxTailLines}.")), "id"),
                Tool("propose", "Đề xuất sửa hoặc xoá một tool dùng chung trong thư viện. Không có hiệu lực cho tới khi người dùng duyệt trong Toolbox.",
                    Props(("change", "string", "update hoặc delete."), ("id", "string", "Id tool đã gắn vào workspace."), ("reason", "string", "Lý do."),
                        ("name", "string", "update: tên mới."), ("path", "string", "update: đường dẫn mới."), ("args", "string", "update: tham số mặc định mới."),
                        ("description", "string", "update: mô tả mới."), ("tags", "array", "update: tag mới."), ("notes", "string", "update: ghi chú mới.")), "change", "id"),
            };
            var kept = SetupToolDefinitions().Where(d => d is Dictionary<string, object> m && KeptNames.Contains((string)m["name"]));
            return generic.Concat(kept).ToArray();
        }

        private string GenericInstructions(Workspace workspace) =>
            $"Toolbox đang giới hạn trong workspace \"{workspace.Name}\". Bạn thêm, sửa, xoá tool riêng, profile, bộ chạy, lệnh riêng trong workspace này tuỳ ý. "
            + "Bắt đầu bằng overview, rồi search để tìm theo từ khoá thay vì đọc hết. Muốn dùng tool có sẵn trong thư viện: search type=library rồi attach. "
            + "Tool dùng chung trong thư viện thì không sửa trực tiếp: dùng propose, người dùng duyệt trong Toolbox. "
            + "Nhóm biến (biến môi trường, secret) do người dùng tự tạo ở tab Biến: bạn chỉ liệt kê được (search type=variablegroup, không thấy giá trị nào), "
            + "gắn vào profile bằng variableGroup, hoặc chạy lệnh kèm nhóm; không tạo, sửa, xoá nhóm. "
            + "Chạy bằng run, theo dõi bằng run_status hoặc logs. Tác vụ do agent khởi chạy dừng khi phiên kết thúc.";

        private object GenericCall(string name, Dictionary<string, object> a, Workspace workspace, AgentRecord record)
        {
            if (workspace == null)
                return ToolResult("Chỉ dùng được khi kết nối agent với một workspace (toolbox --mcp --workspace <id>).", isError: true);

            switch (name)
            {
                case "overview": return GOverview(workspace);
                case "search": return GSearch(a, workspace);
                case "get": return GGet(a, workspace);
                case "save": return GSave(a, workspace, record);
                case "delete": return GDelete(a, workspace, record);
                case "attach": return GAttach(a, workspace, record);
                case "run": return GRun(a, workspace, record);
                case "logs": return GLogs(a);
                default: return GPropose(a, workspace, record);
            }
        }

        private static string TypeArg(Dictionary<string, object> a, params string[] allowed)
        {
            var type = (GetString(a, "type") ?? "").Trim().ToLowerInvariant();
            if (!allowed.Contains(type))
                throw new ArgumentException($"type phải là một trong: {string.Join(", ", allowed)}.");
            return type;
        }

        private object GOverview(Workspace ws)
        {
            var pending = 0;
            if (!string.IsNullOrEmpty(_agent?.ProposalsPath))
                pending = ProposalStore.Load(_agent.ProposalsPath).Pending.Count(p => string.IsNullOrEmpty(_agent.AgentId) || p.AgentId == _agent.AgentId);
            var runMode = RunModes.Auto;
            if (!string.IsNullOrEmpty(_agent?.AgentId))
                runMode = RunModes.Normalize(AgentStore.Load(_agent.AgentsPath).Find(_agent.AgentId)?.RunMode);

            return ToolResult(_json.Serialize(new Dictionary<string, object>
            {
                ["workspace"] = ws.Name,
                ["counts"] = new Dictionary<string, object>
                {
                    ["tools"] = ws.Bindings.Count,
                    ["profiles"] = ws.Profiles.Count,
                    ["stacks"] = ws.Stacks.Count,
                    ["customs"] = ws.Customs.Count,
                    ["variableGroups"] = ws.VariableGroups.Count(g => g.AllowAgent),
                },
                ["pendingProposals"] = pending,
                ["runMode"] = runMode,
                ["hint"] = "Dùng search để tìm theo từ khoá; không có công cụ nào liệt kê hết.",
            }), isError: false);
        }

        private object GSearch(Dictionary<string, object> a, Workspace ws)
        {
            var typeText = (GetString(a, "type") ?? "").Trim().ToLowerInvariant();
            if (typeText.Length > 0 && !ResourceTypes.Contains(typeText))
                throw new ArgumentException($"type phải là một trong: {string.Join(", ", ResourceTypes)}, hoặc bỏ trống.");
            var query = Text.Fold(GetString(a, "query") ?? "");
            var limit = Math.Max(1, Math.Min(MaxSearchLimit, IntArg(a, "limit", DefaultSearchLimit)));
            var offset = Math.Max(0, IntArg(a, "offset", 0));

            var rows = new List<Dictionary<string, object>>();
            void Add(string type, string id, string label, string extra)
            {
                if (query.Length == 0 || Text.Fold((label ?? "") + " " + id + " " + (extra ?? "")).Contains(query))
                    rows.Add(new Dictionary<string, object> { ["type"] = type, ["id"] = id, ["label"] = label ?? id, ["info"] = extra ?? "" });
            }

            bool Want(string type) => typeText.Length == 0 ? type != "library" && type != "variablegroup" : typeText == type;
            if (Want("variablegroup"))
                foreach (var g in ws.VariableGroups.Where(x => x.AllowAgent))
                {
                    var names = string.Join(" ", g.Variables.Select(v => v.Name));
                    if (query.Length > 0 && !Text.Fold(g.Label + " " + g.Id + " " + g.Description + " " + names).Contains(query))
                        continue;
                    var row = GroupView(g);
                    row["type"] = "variablegroup";
                    rows.Add(row);
                }
            if (Want("profile"))
                foreach (var p in ws.Profiles)
                    Add("profile", p.Id, p.Label, p.Kind + " " + (ProfileKind.IsCustom(p.Kind) ? p.RefId : p.ActionId));
            if (Want("stack"))
                foreach (var s in ws.Stacks)
                    Add("stack", s.Id, s.Label, s.Steps.Count + " bước");
            if (Want("custom"))
                foreach (var c in ws.Customs)
                    Add("custom", c.Id, c.Label, c.CommandLine);
            if (Want("tool") || Want("library"))
            {
                var store = _loadStore();
                if (Want("tool"))
                {
                    foreach (var b in ws.Bindings)
                    {
                        var t = store.Find(b.ToolId);
                        Add("tool", b.ToolId, t?.Name ?? b.ToolId, t?.Description ?? "");
                    }
                }
                else
                {
                    foreach (var t in store.Tools)
                        Add("library", t.Id, t.Name, (t.Description ?? "") + " " + string.Join(" ", t.Tags ?? new List<string>()) + (IsBound(ws, t.Id) ? " (đã gắn)" : ""));
                }
            }

            var page = rows.Skip(offset).Take(limit).ToArray();
            var view = new Dictionary<string, object> { ["total"] = rows.Count, ["offset"] = offset, ["items"] = page };
            if (offset + limit < rows.Count)
                view["nextOffset"] = offset + limit;
            return ToolResult(_json.Serialize(view), isError: false);
        }

        private object GGet(Dictionary<string, object> a, Workspace ws)
        {
            var type = TypeArg(a, ResourceTypes);
            var id = GetString(a, "id");
            Dictionary<string, object> view = null;

            switch (type)
            {
                case "profile":
                    var p = ws.Profiles.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (p != null)
                        view = new Dictionary<string, object> { ["id"] = p.Id, ["label"] = p.Label, ["kind"] = p.Kind, ["action"] = p.ActionId, ["variant"] = p.VariantId, ["refId"] = p.RefId, ["args"] = p.ExtraArgs, ["vals"] = p.Vals, ["pairs"] = (p.Pairs ?? new List<ParamPair>()).Select(x => new Dictionary<string, object> { ["key"] = x.Key, ["value"] = x.Value, ["ask"] = x.Ask }).ToArray() };
                    break;
                case "stack":
                    var s = ws.Stacks.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (s != null)
                        view = new Dictionary<string, object>
                        {
                            ["id"] = s.Id, ["label"] = s.Label,
                            ["steps"] = s.Steps.Select(t =>
                            {
                                var step = new Dictionary<string, object>
                                    { ["kind"] = t.Kind, ["action"] = t.ActionId, ["variant"] = t.VariantId, ["profile"] = t.ProfileId, ["wait"] = t.Wait.ToString().ToLowerInvariant(), ["port"] = t.Port };
                                if (t.Group > 0)
                                    step["group"] = t.Group;
                                return (object)step;
                            }).ToArray(),
                        };
                    break;
                case "custom":
                    var c = ws.Customs.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (c != null)
                        view = new Dictionary<string, object> { ["id"] = c.Id, ["label"] = c.Label, ["commandLine"] = c.CommandLine, ["cwd"] = c.Cwd, ["window"] = c.Window };
                    break;
                case "variablegroup":
                    var group = AgentGroup(ws, id);
                    if (group != null)
                        view = GroupView(group);
                    break;
                default:
                    var tool = _loadStore().Find(id);
                    if (tool != null)
                        view = new Dictionary<string, object>
                        {
                            ["id"] = tool.Id, ["name"] = tool.Name, ["description"] = tool.Description, ["tags"] = tool.Tags, ["args"] = tool.Args, ["notes"] = tool.Notes,
                            ["store"] = tool.Store.ToString().ToLowerInvariant(), ["bound"] = IsBound(ws, tool.Id), ["exists"] = tool.PathExists,
                        };
                    break;
            }

            if (type == "profile" && view != null)
            {
                var linked = ws.Profiles.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                var linkedGroup = string.IsNullOrWhiteSpace(linked?.VariableGroupId) ? null : VariableGroups.Find(ws, linked.VariableGroupId);
                if (linkedGroup != null && linkedGroup.AllowAgent)
                    view["variableGroup"] = linkedGroup.Label;
            }

            return view == null
                ? ToolResult($"Không có {type} \"{id}\". Dùng search để tìm id.", isError: true)
                : ToolResult(_json.Serialize(view), isError: false);
        }

        /// <summary>Nhóm biến cho agent: id, tên, mô tả và từng biến {name, secret, hasValue}. Không bao giờ có giá trị.</summary>
        private static Dictionary<string, object> GroupView(VariableGroup g) => new Dictionary<string, object>
        {
            ["id"] = g.Id,
            ["label"] = g.Label,
            ["description"] = g.Description ?? "",
            ["variables"] = g.Variables.Select(v => (object)new Dictionary<string, object>
            {
                ["name"] = v.Name,
                ["secret"] = v.Secret,
                ["hasValue"] = v.Secret ? !v.NeedsInput : !string.IsNullOrEmpty(v.Value),
            }).ToArray(),
        };

        private static bool IsVariableGroupType(Dictionary<string, object> a) =>
            string.Equals((GetString(a, "type") ?? "").Trim(), "variablegroup", StringComparison.OrdinalIgnoreCase);

        private object GSave(Dictionary<string, object> a, Workspace ws, AgentRecord record)
        {
            if (IsVariableGroupType(a))
                return ToolResult(VariableGroupRefusal, isError: true);
            var type = TypeArg(a, "profile", "stack", "custom", "tool");
            var update = !string.IsNullOrWhiteSpace(GetString(a, "id"));
            var legacy = type == "tool" ? (update ? "update_tool" : "register_tool") : (update ? "update_" : "create_") + type;
            return Dispatch(legacy, a, ws, record);
        }

        private object GDelete(Dictionary<string, object> a, Workspace ws, AgentRecord record)
        {
            if (IsVariableGroupType(a))
                return ToolResult(VariableGroupRefusal, isError: true);
            var type = TypeArg(a, "profile", "stack", "custom", "tool");
            return Dispatch(type == "tool" ? "unregister_tool" : "delete_" + type, a, ws, record);
        }

        private object GAttach(Dictionary<string, object> a, Workspace ws, AgentRecord record)
        {
            if (!string.IsNullOrWhiteSpace(GetString(a, "id")))
                return Dispatch("bind_library_tool", a, ws, record);
            if (!string.IsNullOrWhiteSpace(GetString(a, "kind")))
                return Dispatch("add_tool", a, ws, record);
            throw new ArgumentException("Truyền id hoặc kind (id tool trong thư viện).");
        }

        private object GRun(Dictionary<string, object> a, Workspace ws, AgentRecord record)
        {
            var type = TypeArg(a, "profile", "stack", "custom", "tool", "action");
            var legacy = type == "tool" ? "run_tool" : "run_" + type;
            return Dispatch(legacy, a, ws, record);
        }

        private object GLogs(Dictionary<string, object> a)
        {
            var copy = new Dictionary<string, object>(a);
            if (!copy.ContainsKey("tail"))
                copy["tail"] = DefaultLogLines;
            return RunStatus(copy);
        }

        private object GPropose(Dictionary<string, object> a, Workspace ws, AgentRecord record)
        {
            var change = (GetString(a, "change") ?? "").Trim().ToLowerInvariant();
            if (change == "update")
                return ProposeToolUpdate(_loadStore(), a, ws, record);
            if (change != "delete")
                throw new ArgumentException("change phải là update hoặc delete.");

            var id = GetString(a, "id");
            if (!IsBound(ws, id))
                return NotInWorkspace(id);
            var tool = _loadStore().Find(id);
            if (tool == null)
                return ToolResult($"Không có tool \"{id}\".", isError: true);
            return SubmitProposal(ProposalKinds.DeleteTool, tool, new Dictionary<string, string>(), GetString(a, "reason"), ws, record);
        }
    }
}
