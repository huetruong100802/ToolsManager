using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    /// <summary>Kết quả nhập gói vào workspace. Bên gọi tự Save ToolStore và WorkspaceStore.</summary>
    public class WorkspaceImportResult
    {
        /// <summary>Tool mới thêm/thay vào thư viện.</summary>
        public List<ToolEntry> Tools { get; } = new List<ToolEntry>();
        public int BindingsAdded { get; set; }
        public int CustomsAdded { get; set; }
        public int ProfilesAdded { get; set; }
        public int StacksAdded { get; set; }
        public int VariableGroupsAdded { get; set; }
        /// <summary>Id tool trong gói → id thật trong thư viện sau khi nhập.</summary>
        public Dictionary<string, string> ToolMap { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Nhập gói: giải nén các tool được chọn rồi thêm vào danh sách.</summary>
    public static class PackageImporter
    {
        /// <param name="choices">Id tool trong gói → cách xử lý nếu trùng id. Tool không có trong choices hoặc Skip thì không nhập.</param>
        public static IList<ToolEntry> Import(ToolStore store, string zipPath, string destRoot, IDictionary<string, ConflictResolution> choices) =>
            ImportTools(store, zipPath, destRoot, choices, null);

        private static IList<ToolEntry> ImportTools(ToolStore store, string zipPath, string destRoot,
            IDictionary<string, ConflictResolution> choices, IDictionary<string, string> map)
        {
            // Bỏ qua tool Skip đang trùng id ngay từ đầu để không giải nén thừa.
            var wanted = choices
                .Where(c => c.Value != ConflictResolution.Skip || store.Find(c.Key) == null)
                .Select(c => c.Key)
                .ToList();

            var imported = new List<ToolEntry>();
            foreach (var entry in Packer.Extract(zipPath, destRoot, wanted))
            {
                var packId = entry.Id;
                var added = store.Import(entry, choices[packId]);
                if (added != null)
                {
                    imported.Add(added);
                    if (map != null) map[packId] = added.Id;
                }
            }
            if (map != null)
                foreach (var c in choices.Where(c => c.Value == ConflictResolution.Skip && !map.ContainsKey(c.Key)))
                    if (store.Find(c.Key) != null)
                        map[c.Key] = store.Find(c.Key).Id;
            return imported;
        }

        /// <summary>
        /// Nhập gói vào một workspace có sẵn: tool vào <paramref name="store"/> (theo <paramref name="toolChoices"/>), gắn tool vào
        /// workspace, rồi thêm lệnh riêng, profile, bộ chạy với id MỚI nhưng tham chiếu đã map lại. Trùng tên (nhãn) trong workspace
        /// xử lý theo <paramref name="itemConflict"/>: KeepBoth thêm hậu tố " (2)", Skip dùng mục sẵn có, Replace thay nội dung
        /// mục sẵn có (giữ id) - không bao giờ ghi đè thầm. Gói cũ không có phần workspace thì chỉ nhập tool và gắn vào workspace.
        /// Không tự Save.
        /// </summary>
        public static WorkspaceImportResult ImportIntoWorkspace(ToolStore store, Workspace ws, string zipPath, string destRoot,
            IDictionary<string, ConflictResolution> toolChoices, ConflictResolution itemConflict = ConflictResolution.KeepBoth)
        {
            var manifest = Packer.ReadManifest(zipPath);
            var data = manifest.Workspace ?? new WorkspacePackData();
            var result = new WorkspaceImportResult();

            result.Tools.AddRange(ImportTools(store, zipPath, destRoot, toolChoices ?? new Dictionary<string, ConflictResolution>(), result.ToolMap));

            // Id tool trong gói → id trong thư viện đích (đổi khi KeepBoth); không có trong map thì giữ nguyên.
            string MapTool(string id) => id != null && result.ToolMap.TryGetValue(id, out var mappedId) ? mappedId : id;

            // Gắn tool vào workspace (tool vừa nhập, tool trong bindings/requiredKinds đã có sẵn ở thư viện đích);
            // Args của gói chỉ áp khi workspace chưa gắn tool đó.
            var wanted = result.ToolMap.Keys
                .Concat(data.Bindings.Select(b => b?.ToolId))
                .Concat(data.RequiredKinds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var packedId in wanted)
            {
                var tool = store.Find(MapTool(packedId));
                if (tool == null)
                {
                    result.Warnings.Add("Tool \"" + packedId + "\" chưa có trong thư viện (chưa nhập), chưa gắn được vào workspace.");
                    continue;
                }
                if (ws.Binding(tool.Id) != null) continue;
                var packed = data.Bindings.Find(b => string.Equals(b?.ToolId, packedId, StringComparison.OrdinalIgnoreCase));
                ws.Bindings.Add(new ToolBinding { ToolId = tool.Id, Args = packed?.Args });
                result.BindingsAdded++;
            }

            var customMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in data.Customs)
            {
                var existing = ws.Customs.Find(c => SameLabel(c.Label, src.Label));
                if (existing != null && itemConflict != ConflictResolution.KeepBoth)
                {
                    customMap[src.Id] = existing.Id;
                    if (itemConflict == ConflictResolution.Replace)
                    {
                        existing.CommandLine = src.CommandLine;
                        existing.Cwd = src.Cwd;
                        existing.Window = src.Window;
                    }
                    continue;
                }
                var copy = new CustomCommand
                {
                    Id = NewId(ws.Customs.Select(c => c.Id)),
                    Label = UniqueLabel(src.Label, ws.Customs.Select(c => c.Label)),
                    CommandLine = src.CommandLine, Cwd = src.Cwd, Window = src.Window,
                };
                ws.Customs.Add(copy);
                customMap[src.Id] = copy.Id;
                result.CustomsAdded++;
            }

            // Nhóm biến (D8): gói chỉ có tên biến + giá trị thường; secret nhập về trạng thái "Cần nhập". Gói cũ không có phần này.
            var groupMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in data.VariableGroups)
            {
                if (src == null || string.IsNullOrEmpty(src.Id)) continue;
                var incoming = ImportedVariables(src);
                var existing = ws.VariableGroups.Find(g => SameLabel(g.Label, src.Label));
                if (existing != null && itemConflict != ConflictResolution.KeepBoth)
                {
                    groupMap[src.Id] = existing.Id;
                    if (itemConflict == ConflictResolution.Replace)
                    {
                        // Giữ secret đã nhập ở máy này nếu tên trùng; gói không bao giờ mang giá trị secret.
                        foreach (var v in incoming.Where(v => v.Secret))
                        {
                            var mine = existing.Variables.Find(x => x != null && x.Secret && string.Equals(x.Name, v.Name, StringComparison.OrdinalIgnoreCase));
                            if (mine != null) v.Enc = mine.Enc;
                        }
                        existing.Description = src.Description;
                        existing.Variables = incoming;
                        existing.ModifiedAt = DateTime.UtcNow.ToString("o");
                        WarnNeedInput(result, existing);
                    }
                    continue;
                }
                var group = new VariableGroup
                {
                    Id = NewId(ws.VariableGroups.Select(g => g.Id)),
                    Label = UniqueLabel(src.Label, ws.VariableGroups.Select(g => g.Label)),
                    Description = src.Description,
                    AllowAgent = src.AllowAgent,
                    ModifiedAt = DateTime.UtcNow.ToString("o"),
                    Variables = incoming,
                };
                ws.VariableGroups.Add(group);
                groupMap[src.Id] = group.Id;
                result.VariableGroupsAdded++;
                WarnNeedInput(result, group);
            }

            var profileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var profileKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in data.Profiles)
            {
                var mapped = WorkspacePack.CloneProfile(src);
                // Gói cũ: kind "library" + refId = toolId → kind = toolId, chạy lệnh gốc.
                if (ProfileKind.IsLibrary(mapped.Kind) && !string.IsNullOrEmpty(mapped.RefId))
                {
                    mapped.Kind = mapped.RefId;
                    mapped.ActionId = "";
                    mapped.VariantId = null;
                    mapped.RefId = null;
                }
                if (!ProfileKind.IsCustom(mapped.Kind) && !string.IsNullOrEmpty(mapped.Kind))
                {
                    var toolKind = mapped.Kind;
                    mapped.Kind = MapTool(toolKind);
                    if (store.Find(mapped.Kind) == null)
                        result.Warnings.Add("Profile \"" + src.Label + "\" trỏ tool \"" + toolKind + "\" chưa có trong thư viện (chưa nhập).");
                }
                else if (ProfileKind.IsCustom(mapped.Kind) && !string.IsNullOrEmpty(mapped.RefId))
                {
                    if (customMap.TryGetValue(mapped.RefId, out var customId))
                        mapped.RefId = customId;
                    else
                        result.Warnings.Add("Profile \"" + src.Label + "\" trỏ lệnh riêng \"" + src.RefId + "\" không có trong gói.");
                }

                if (!string.IsNullOrEmpty(mapped.VariableGroupId))
                {
                    if (groupMap.TryGetValue(mapped.VariableGroupId, out var groupId))
                        mapped.VariableGroupId = groupId;
                    else
                    {
                        result.Warnings.Add("Profile \"" + src.Label + "\" gắn nhóm biến không có trong gói; profile này chưa gắn nhóm nào.");
                        mapped.VariableGroupId = null;
                    }
                }

                var existing = ws.Profiles.Find(p => SameLabel(p.Label, src.Label));
                if (existing != null && itemConflict != ConflictResolution.KeepBoth)
                {
                    profileMap[src.Id] = existing.Id;
                    if (itemConflict == ConflictResolution.Replace)
                    {
                        existing.Kind = mapped.Kind; existing.ActionId = mapped.ActionId; existing.VariantId = mapped.VariantId;
                        existing.RefId = mapped.RefId; existing.Vals = mapped.Vals; existing.Pairs = mapped.Pairs;
                        existing.VariableGroupId = mapped.VariableGroupId;
                    }
                    continue;
                }
                mapped.Id = NewId(ws.Profiles.Select(p => p.Id));
                mapped.Label = UniqueLabel(src.Label, ws.Profiles.Select(p => p.Label));
                ws.Profiles.Add(mapped);
                profileMap[src.Id] = mapped.Id;
                profileKind[src.Id] = mapped.Kind;
                result.ProfilesAdded++;
            }

            foreach (var src in data.Stacks)
            {
                var steps = new List<StackStep>();
                foreach (var step in src.Steps)
                {
                    var copy = new StackStep
                    {
                        Kind = step.Kind, ActionId = step.ActionId, VariantId = step.VariantId, ProfileId = step.ProfileId,
                        Wait = step.Wait, Port = step.Port, Group = step.Group,
                    };
                    var legacyTool = ProfileKind.IsLibrary(copy.Kind);
                    if (!string.IsNullOrEmpty(copy.ProfileId))
                    {
                        var srcProfileId = copy.ProfileId;
                        if (profileMap.TryGetValue(srcProfileId, out var pid))
                        {
                            copy.ProfileId = pid;
                            // Bước cũ loại "library" có profile: tool lấy theo profile (đã map về toolId).
                            if (legacyTool && profileKind.TryGetValue(srcProfileId, out var pkind))
                            {
                                copy.Kind = pkind;
                                copy.ActionId = "";
                                copy.VariantId = null;
                            }
                        }
                        else
                        {
                            result.Warnings.Add("Bộ chạy \"" + src.Label + "\" có bước trỏ profile không có trong gói; bước này mất profile.");
                            copy.ProfileId = null;
                        }
                    }
                    else if (ProfileKind.IsCustom(copy.Kind) && customMap.TryGetValue(copy.ActionId ?? "", out var cid))
                        copy.ActionId = cid;
                    else if (legacyTool && !string.IsNullOrEmpty(copy.ActionId))
                    {
                        // Bước cũ loại "library" không profile: ActionId là toolId.
                        copy.Kind = MapTool(copy.ActionId);
                        copy.ActionId = "";
                        copy.VariantId = null;
                    }
                    if (!ProfileKind.IsCustom(copy.Kind) && !ProfileKind.IsLibrary(copy.Kind))
                        copy.Kind = MapTool(copy.Kind);
                    steps.Add(copy);
                }

                var existing = ws.Stacks.Find(s => SameLabel(s.Label, src.Label));
                if (existing != null && itemConflict != ConflictResolution.KeepBoth)
                {
                    if (itemConflict == ConflictResolution.Replace)
                        existing.Steps = steps;
                    continue;
                }
                ws.Stacks.Add(new StackDef
                {
                    Id = NewId(ws.Stacks.Select(s => s.Id)),
                    Label = UniqueLabel(src.Label, ws.Stacks.Select(s => s.Label)),
                    Steps = steps,
                });
                result.StacksAdded++;
            }

            var missing = data.RequiredKinds.Where(k => ws.Binding(k) == null).ToList();
            if (missing.Count > 0)
                result.Warnings.Add("Workspace này chưa gắn tool dựng sẵn mà gói cần: " + string.Join(", ", missing) + ".");
            return result;
        }

        /// <summary>Biến của nhóm trong gói: tên + giá trị thường; secret bị ép về "Cần nhập" (bỏ Value/Enc kể cả khi gói viết tay có), biến tên hỏng bị bỏ.</summary>
        private static List<GroupVariable> ImportedVariables(VariableGroup src)
        {
            var list = new List<GroupVariable>();
            foreach (var v in src.Variables ?? new List<GroupVariable>())
            {
                if (v == null || VariableGroups.ValidateName(v.Name) != null) continue;
                if (list.Any(x => string.Equals(x.Name, v.Name, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(v.Secret
                    ? new GroupVariable { Name = v.Name, Secret = true }
                    : new GroupVariable { Name = v.Name, Value = v.Value });
            }
            return list;
        }

        private static void WarnNeedInput(WorkspaceImportResult result, VariableGroup group)
        {
            var need = group.Variables.Where(v => v.NeedsInput).Select(v => v.Name).ToList();
            if (need.Count > 0)
                result.Warnings.Add("Nhóm biến \"" + group.Label + "\" cần nhập giá trị secret ở tab Biến: " + string.Join(", ", need) + ".");
        }

        private static bool SameLabel(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static string UniqueLabel(string label, IEnumerable<string> existing)
        {
            var taken = new HashSet<string>(existing.Select(l => (l ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
            var baseLabel = (label ?? "").Trim();
            if (!taken.Contains(baseLabel))
                return label;
            for (var i = 2; ; i++)
            {
                var candidate = baseLabel + " (" + i + ")";
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private static string NewId(IEnumerable<string> existing)
        {
            var taken = new HashSet<string>(existing.Where(i => i != null), StringComparer.OrdinalIgnoreCase);
            string id;
            do { id = WorkspaceStore.NewId(); } while (taken.Contains(id));
            return id;
        }
    }
}
