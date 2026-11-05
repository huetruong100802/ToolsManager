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

            // Gắn tool vào workspace; Args của gói chỉ áp khi workspace chưa gắn tool đó.
            foreach (var pair in result.ToolMap)
            {
                var tool = store.Find(pair.Value);
                if (tool == null || ws.FindLibrary(tool.Id) != null) continue;
                var packed = data.Bindings.Find(b => string.Equals(b.ToolId, pair.Key, StringComparison.OrdinalIgnoreCase));
                ws.LibraryBindings.Add(new LibraryBinding { ToolId = tool.Id, Name = tool.Name, Args = packed?.Args });
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

            var profileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in data.Profiles)
            {
                var mapped = WorkspacePack.CloneProfile(src);
                if (ProfileKind.IsLibrary(mapped.Kind) && !string.IsNullOrEmpty(mapped.RefId))
                {
                    if (result.ToolMap.TryGetValue(mapped.RefId, out var toolId))
                        mapped.RefId = toolId;
                    else if (store.Find(mapped.RefId) == null)
                        result.Warnings.Add("Profile \"" + src.Label + "\" trỏ tool \"" + src.RefId + "\" chưa có trong thư viện (chưa nhập).");
                }
                else if (ProfileKind.IsCustom(mapped.Kind) && !string.IsNullOrEmpty(mapped.RefId))
                {
                    if (customMap.TryGetValue(mapped.RefId, out var customId))
                        mapped.RefId = customId;
                    else
                        result.Warnings.Add("Profile \"" + src.Label + "\" trỏ lệnh riêng \"" + src.RefId + "\" không có trong gói.");
                }

                var existing = ws.Profiles.Find(p => SameLabel(p.Label, src.Label));
                if (existing != null && itemConflict != ConflictResolution.KeepBoth)
                {
                    profileMap[src.Id] = existing.Id;
                    if (itemConflict == ConflictResolution.Replace)
                    {
                        existing.Kind = mapped.Kind; existing.ActionId = mapped.ActionId; existing.VariantId = mapped.VariantId;
                        existing.RefId = mapped.RefId; existing.Vals = mapped.Vals;
                    }
                    continue;
                }
                mapped.Id = NewId(ws.Profiles.Select(p => p.Id));
                mapped.Label = UniqueLabel(src.Label, ws.Profiles.Select(p => p.Label));
                ws.Profiles.Add(mapped);
                profileMap[src.Id] = mapped.Id;
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
                    if (!string.IsNullOrEmpty(copy.ProfileId))
                    {
                        if (profileMap.TryGetValue(copy.ProfileId, out var pid))
                            copy.ProfileId = pid;
                        else
                        {
                            result.Warnings.Add("Bộ chạy \"" + src.Label + "\" có bước trỏ profile không có trong gói; bước này mất profile.");
                            copy.ProfileId = null;
                        }
                    }
                    else if (ProfileKind.IsCustom(copy.Kind) && customMap.TryGetValue(copy.ActionId ?? "", out var cid))
                        copy.ActionId = cid;
                    else if (ProfileKind.IsLibrary(copy.Kind) && result.ToolMap.TryGetValue(copy.ActionId ?? "", out var tid))
                        copy.ActionId = tid;
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
