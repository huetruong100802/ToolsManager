using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Nâng dữ liệu workspace định dạng cũ (schema 1) lên mô hình "một loại tool": mọi tool gắn vào workspace là một
    /// <see cref="ToolEntry"/> trong thư viện, trỏ bằng <see cref="ToolBinding.ToolId"/>.
    /// Idempotent: chạy lại trên dữ liệu đã nâng không đổi gì. Không xoá dữ liệu nào của người dùng.
    /// </summary>
    public static class WorkspaceMigration
    {
        /// <summary>Phiên bản schema của workspaces.json hiện tại. File cũ không có trường schema được coi là 1.</summary>
        public const int CurrentSchema = 2;

        /// <summary>
        /// Nâng <paramref name="workspaces"/> tại chỗ và seed tool mẫu vào <paramref name="library"/> (không ghi file):
        /// (a) binding có ToolId là preset mà thư viện chưa có → thêm <see cref="ToolPresets.ToEntry"/> (Id = kind cũ);
        /// (b) <see cref="Workspace.LibraryBindings"/> → <see cref="Workspace.Bindings"/> (giữ Args) rồi làm rỗng;
        /// (c) profile và bước bộ chạy loại "library" → Kind = toolId, ActionId = "".
        /// Trả về true nếu có thay đổi (workspace hoặc thư viện).
        /// </summary>
        public static bool Upgrade(IList<Workspace> workspaces, ToolStore library)
        {
            if (workspaces == null)
                throw new ArgumentNullException(nameof(workspaces));
            if (library == null)
                throw new ArgumentNullException(nameof(library));

            var changed = false;
            foreach (var ws in workspaces)
            {
                if (ws == null)
                    continue;
                changed |= MoveLibraryBindings(ws);
                changed |= RemapSteps(ws);
                changed |= RemapProfiles(ws);
                changed |= SeedPresets(ws, library);
            }
            return changed;
        }

        private static bool MoveLibraryBindings(Workspace ws)
        {
            if (ws.LibraryBindings == null || ws.LibraryBindings.Count == 0)
                return false;

            foreach (var old in ws.LibraryBindings)
            {
                if (string.IsNullOrWhiteSpace(old?.ToolId) || ws.Binding(old.ToolId) != null)
                    continue;
                ws.Bindings.Add(new ToolBinding { ToolId = old.ToolId, Cwd = null, Args = old.Args });
            }
            ws.LibraryBindings.Clear();
            return true;
        }

        /// <summary>
        /// Bước cũ loại "library": không profile thì ActionId là toolId; có profile thì toolId là RefId của profile
        /// (StackRunner đòi profile.Kind == step.Kind, nên bước phải theo đúng Kind mới của profile).
        /// Chạy trước <see cref="RemapProfiles"/> để còn đọc được RefId.
        /// </summary>
        private static bool RemapSteps(Workspace ws)
        {
            var changed = false;
            foreach (var step in ws.Stacks.Where(s => s?.Steps != null).SelectMany(s => s.Steps))
            {
                if (step == null || !ProfileKind.IsLibrary(step.Kind))
                    continue;

                string toolId;
                if (!string.IsNullOrEmpty(step.ProfileId))
                {
                    var profile = ws.Profiles.Find(p => string.Equals(p?.Id, step.ProfileId, StringComparison.OrdinalIgnoreCase));
                    if (profile == null)
                        continue; // profile đã xoá: giữ nguyên để runner báo lỗi như trước
                    toolId = ProfileKind.IsLibrary(profile.Kind) ? profile.RefId : null;
                }
                else
                    toolId = step.ActionId;

                if (!IsToolId(toolId))
                    continue;
                step.Kind = toolId.Trim();
                step.ActionId = "";
                step.VariantId = null;
                changed = true;
            }
            return changed;
        }

        private static bool RemapProfiles(Workspace ws)
        {
            var changed = false;
            foreach (var profile in ws.Profiles)
            {
                if (profile == null || !ProfileKind.IsLibrary(profile.Kind) || !IsToolId(profile.RefId))
                    continue;
                profile.Kind = profile.RefId.Trim();
                profile.ActionId = "";
                profile.VariantId = null;
                profile.RefId = null;
                changed = true;
            }
            return changed;
        }

        /// <summary>Seed tool mẫu cho binding có ToolId là preset mà thư viện chưa có. ToolId lạ: giữ binding, bỏ qua.</summary>
        private static bool SeedPresets(Workspace ws, ToolStore library)
        {
            var changed = false;
            foreach (var binding in ws.Bindings)
            {
                var toolId = binding?.ToolId;
                if (string.IsNullOrWhiteSpace(toolId) || library.Find(toolId) != null)
                    continue;
                var preset = ToolPresets.Find(toolId);
                if (preset == null)
                    continue;
                if (library.Import(ToolPresets.ToEntry(preset), ConflictResolution.Skip) != null)
                    changed = true;
            }
            return changed;
        }

        /// <summary>toolId dùng làm Kind được: không rỗng và không trùng tên loại dành riêng ("library", "custom").</summary>
        private static bool IsToolId(string toolId) =>
            !string.IsNullOrWhiteSpace(toolId) && !ProfileKind.IsRef(toolId.Trim());
    }
}
