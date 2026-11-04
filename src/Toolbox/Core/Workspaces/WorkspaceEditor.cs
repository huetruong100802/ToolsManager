using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Sửa cấu hình một workspace: tool, profile, lệnh riêng, bộ chạy, worktree. Dùng chung cho MCP và GUI.
    /// Mỗi thao tác kiểm đầu vào và tham chiếu trước khi đổi. Trả về thông báo lỗi, null nếu thành công.
    /// Không ghi file: người gọi lưu bằng <see cref="WorkspaceStore.Update"/>.
    /// </summary>
    public static class WorkspaceEditor
    {
        // ----- Tool của workspace -----

        public static string AddTool(Workspace ws, string kind, string cwd)
        {
            var tool = ToolCatalog.Find(kind);
            if (tool == null)
                return $"Không có loại tool \"{kind}\". Các loại: " + string.Join(", ", ToolCatalog.All.Select(t => t.Kind)) + ".";
            if (ws.Binding(tool.Kind) != null)
                return $"Workspace đã có tool {tool.Name}.";
            var relative = IsBlank(cwd) ? "." : cwd.Trim();
            var full = ws.ResolveCwd(relative);
            if (!Directory.Exists(full))
                return "Thư mục chạy không tồn tại: " + full;
            ws.Bindings.Add(new ToolBinding { Kind = tool.Kind, Cwd = relative });
            return null;
        }

        public static string RemoveTool(Workspace ws, string kind)
        {
            var binding = ws.Binding(kind);
            if (binding == null)
                return $"Workspace chưa có tool \"{kind}\".";
            var users = ws.Profiles.Where(p => Same(p.Kind, kind)).Select(p => "profile \"" + p.Label + "\"")
                .Concat(ws.Stacks.Where(s => s.Steps.Any(st => Same(st.Kind, kind))).Select(s => "bộ chạy \"" + s.Label + "\""))
                .ToList();
            if (users.Count > 0)
                return "Tool đang được dùng bởi " + string.Join(", ", users) + ". Sửa hoặc xoá các mục đó trước.";
            ws.Bindings.Remove(binding);
            return null;
        }

        /// <summary>Gắn một tool có sẵn trong thư viện vào workspace. Không đổi bản thân tool.</summary>
        public static string BindLibrary(Workspace ws, ToolEntry tool)
        {
            if (ws.FindLibrary(tool.Id) != null)
                return $"\"{tool.Name}\" đã gắn vào workspace này.";
            ws.BindLibrary(tool);
            return null;
        }

        // ----- Profile -----

        public static string CreateProfile(Workspace ws, Profile profile)
        {
            if (IsBlank(profile.Label))
                return "Profile cần có tên (label).";
            profile.Id = IsBlank(profile.Id) ? WorkspaceStore.NewId() : profile.Id.Trim();
            if (ws.Profiles.Any(p => Same(p.Id, profile.Id)))
                return $"Đã có profile với id \"{profile.Id}\".";
            profile.Vals = profile.Vals ?? new Dictionary<string, string>();
            var problem = CheckProfile(ws, profile);
            if (problem != null)
                return problem;
            ws.Profiles.Add(profile);
            return null;
        }

        /// <summary>Sửa tên, biến thể hoặc tham số. Tool và lệnh của profile không đổi; muốn đổi thì xoá rồi tạo lại.</summary>
        public static string UpdateProfile(Workspace ws, string id, string label, string variantId, IDictionary<string, string> vals)
        {
            var current = ws.Profiles.Find(p => Same(p.Id, id));
            if (current == null)
                return NotFound("profile", id);
            var copy = new Profile
            {
                Id = current.Id,
                Label = IsBlank(label) ? current.Label : label.Trim(),
                Kind = current.Kind,
                ActionId = current.ActionId,
                VariantId = IsBlank(variantId) ? current.VariantId : variantId.Trim(),
                Vals = vals == null ? new Dictionary<string, string>(current.Vals) : new Dictionary<string, string>(vals),
            };
            var problem = CheckProfile(ws, copy);
            if (problem != null)
                return problem;
            current.Label = copy.Label;
            current.VariantId = copy.VariantId;
            current.Vals = copy.Vals;
            return null;
        }

        public static string DeleteProfile(Workspace ws, string id)
        {
            var profile = ws.Profiles.Find(p => Same(p.Id, id));
            if (profile == null)
                return NotFound("profile", id);
            var stacks = ws.Stacks.Where(s => s.Steps.Any(st => Same(st.ProfileId, id))).Select(s => "bộ chạy \"" + s.Label + "\"").ToList();
            if (stacks.Count > 0)
                return "Profile đang được dùng bởi " + string.Join(", ", stacks) + ". Sửa các bộ chạy đó trước.";
            ws.Profiles.Remove(profile);
            return null;
        }

        private static string CheckProfile(Workspace ws, Profile profile)
        {
            var binding = ws.Binding(profile.Kind);
            if (binding == null)
                return $"Workspace chưa có tool \"{profile.Kind}\". Thêm tool trước bằng add_tool.";
            var action = ToolCatalog.FindAction(ws, binding, profile.ActionId);
            if (action == null)
                return $"Không có lệnh \"{profile.ActionId}\" của {profile.Kind}. Dùng list_setup để xem action.";
            var variant = IsBlank(profile.VariantId)
                ? action.Default
                : action.Variants.FirstOrDefault(v => Same(v.Id, profile.VariantId));
            if (variant == null)
                return $"Không có biến thể \"{profile.VariantId}\" của lệnh \"{profile.ActionId}\".";
            profile.Kind = binding.Kind;
            profile.VariantId = variant.Id;
            var errors = CommandBuilder.Validate(variant, profile.Vals);
            return errors.Count == 0 ? null : string.Join(" ", errors.Values);
        }

        // ----- Lệnh riêng -----

        public static string CreateCustom(Workspace ws, CustomCommand command)
        {
            command.Id = IsBlank(command.Id) ? WorkspaceStore.NewId() : command.Id.Trim();
            if (ws.Customs.Any(c => Same(c.Id, command.Id)))
                return $"Đã có lệnh riêng với id \"{command.Id}\".";
            var problem = CheckCustom(ws, command);
            if (problem != null)
                return problem;
            ws.Customs.Add(command);
            return null;
        }

        public static string UpdateCustom(Workspace ws, string id, string label, string commandLine, string cwd, bool? window)
        {
            var current = ws.Customs.Find(c => Same(c.Id, id));
            if (current == null)
                return NotFound("lệnh riêng", id);
            var copy = new CustomCommand
            {
                Id = current.Id,
                Label = IsBlank(label) ? current.Label : label,
                CommandLine = IsBlank(commandLine) ? current.CommandLine : commandLine,
                Cwd = cwd ?? current.Cwd,
                Window = window ?? current.Window,
            };
            var problem = CheckCustom(ws, copy);
            if (problem != null)
                return problem;
            current.Label = copy.Label;
            current.CommandLine = copy.CommandLine;
            current.Cwd = copy.Cwd;
            current.Window = copy.Window;
            return null;
        }

        public static string DeleteCustom(Workspace ws, string id)
        {
            var command = ws.Customs.Find(c => Same(c.Id, id));
            if (command == null)
                return NotFound("lệnh riêng", id);
            var stacks = ws.Stacks.Where(s => s.Steps.Any(st => Same(st.Kind, TaskKey.Custom) && Same(st.ActionId, id)))
                .Select(s => "bộ chạy \"" + s.Label + "\"").ToList();
            if (stacks.Count > 0)
                return "Lệnh riêng đang được dùng bởi " + string.Join(", ", stacks) + ". Sửa các bộ chạy đó trước.";
            ws.Customs.Remove(command);
            return null;
        }

        private static string CheckCustom(Workspace ws, CustomCommand command)
        {
            if (IsBlank(command.Label))
                return "Lệnh riêng cần có tên (label).";
            if (IsBlank(command.CommandLine))
                return "Lệnh riêng cần có dòng lệnh (commandLine).";
            command.Cwd = IsBlank(command.Cwd) ? "." : command.Cwd.Trim();
            var full = ws.ResolveCwd(command.Cwd);
            return Directory.Exists(full) ? null : "Thư mục chạy không tồn tại: " + full;
        }

        // ----- Bộ chạy -----

        public static string CreateStack(Workspace ws, StackDef stack)
        {
            if (IsBlank(stack.Label))
                return "Bộ chạy cần có tên (label).";
            stack.Id = IsBlank(stack.Id) ? WorkspaceStore.NewId() : stack.Id.Trim();
            if (ws.Stacks.Any(s => Same(s.Id, stack.Id)))
                return $"Đã có bộ chạy với id \"{stack.Id}\".";
            var problem = CheckStack(ws, stack);
            if (problem != null)
                return problem;
            ws.Stacks.Add(stack);
            return null;
        }

        public static string UpdateStack(Workspace ws, string id, string label, List<StackStep> steps)
        {
            var current = ws.Stacks.Find(s => Same(s.Id, id));
            if (current == null)
                return NotFound("bộ chạy", id);
            var copy = new StackDef
            {
                Id = current.Id,
                Label = IsBlank(label) ? current.Label : label,
                Steps = steps ?? new List<StackStep>(current.Steps),
            };
            var problem = CheckStack(ws, copy);
            if (problem != null)
                return problem;
            current.Label = copy.Label;
            current.Steps = copy.Steps;
            return null;
        }

        public static string DeleteStack(Workspace ws, string id)
        {
            var stack = ws.Stacks.Find(s => Same(s.Id, id));
            if (stack == null)
                return NotFound("bộ chạy", id);
            ws.Stacks.Remove(stack);
            return null;
        }

        /// <summary>Mỗi bước phải chạy được theo workspace hiện tại (tool, lệnh, profile, cổng, kiểu chờ). Bước hỏng thì từ chối cả bộ chạy.</summary>
        private static string CheckStack(Workspace ws, StackDef stack)
        {
            if (stack.Steps == null || stack.Steps.Count == 0)
                return "Bộ chạy cần ít nhất một bước.";
            foreach (var resolved in StackResolver.ResolveAll(ws, stack))
            {
                if (resolved.IsBroken)
                    return $"Bước {resolved.Index + 1}: {resolved.Problem}";
            }
            return null;
        }

        // ----- Worktree -----

        public static void SetWorktree(Workspace ws, IList<string> files, string command)
        {
            if (ws.Worktree == null)
                ws.Worktree = new WorktreeSetup();
            if (files != null)
                ws.Worktree.Files = files.Select(f => (f ?? "").Trim()).Where(f => f.Length > 0).ToList();
            if (command != null)
                ws.Worktree.Command = IsBlank(command) ? null : command.Trim();
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool IsBlank(string value) => string.IsNullOrWhiteSpace(value);

        private static string NotFound(string what, string id) => $"Không có {what} \"{id}\". Dùng list_setup để xem id.";
    }
}
