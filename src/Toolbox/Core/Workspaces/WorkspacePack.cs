using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Thứ người dùng tick khi đóng gói theo workspace. Id theo workspace nguồn (tool theo id thư viện).</summary>
    public class WorkspacePackSelection
    {
        public List<string> ToolIds { get; set; } = new List<string>();
        public List<string> ProfileIds { get; set; } = new List<string>();
        public List<string> StackIds { get; set; } = new List<string>();
        public List<string> CustomIds { get; set; } = new List<string>();

        /// <summary>
        /// Chỉ có sau <see cref="WorkspacePack.Resolve"/>: vì sao mục được kéo theo (mục tick tay không có). Khoá dạng
        /// "tool:id", "profile:id", "custom:id"; xem <see cref="WhyPulled"/>.
        /// </summary>
        public Dictionary<string, string> Reasons { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public const string ToolKey = "tool";
        public const string ProfileKey = "profile";
        public const string CustomKey = "custom";

        /// <summary>Lý do được kéo theo của mục; null nếu do tick tay hoặc không có.</summary>
        public string WhyPulled(string kind, string id) =>
            id != null && Reasons != null && Reasons.TryGetValue(kind + ":" + id, out var why) ? why : null;

        public bool IsEmpty => ToolIds.Count == 0 && ProfileIds.Count == 0 && StackIds.Count == 0 && CustomIds.Count == 0;
    }

    /// <summary>Thống kê và cảnh báo cho khung "Gói sẽ có".</summary>
    public class WorkspacePackPreview
    {
        /// <summary>Selection đã mở rộng (gồm Reasons).</summary>
        public WorkspacePackSelection Selection { get; set; } = new WorkspacePackSelection();
        /// <summary>Số tool thư viện thật sự đóng gói được (có trong thư viện).</summary>
        public int ToolCount { get; set; }
        public int ProfileCount { get; set; }
        public int StackCount { get; set; }
        public int CustomCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Phần workspace trong manifest. Không chứa đường dẫn máy, id workspace, tác vụ, log, worktree.</summary>
    [DataContract]
    public class WorkspacePackData
    {
        /// <summary>Tool thư viện cần có trong workspace nhận về (ToolId + Args).</summary>
        [DataMember(Name = "bindings", Order = 0)]
        public List<LibraryBinding> Bindings { get; set; } = new List<LibraryBinding>();

        [DataMember(Name = "customs", Order = 1)]
        public List<CustomCommand> Customs { get; set; } = new List<CustomCommand>();

        [DataMember(Name = "profiles", Order = 2)]
        public List<Profile> Profiles { get; set; } = new List<Profile>();

        [DataMember(Name = "stacks", Order = 3)]
        public List<StackDef> Stacks { get; set; } = new List<StackDef>();

        /// <summary>Kind catalog dựng sẵn (dotnet, git…) mà profile/bộ chạy cần; workspace nhận phải tự gắn.</summary>
        [DataMember(Name = "requiredKinds", Order = 4, EmitDefaultValue = false)]
        public List<string> RequiredKinds { get; set; } = new List<string>();

        public bool IsEmpty => Bindings.Count == 0 && Customs.Count == 0 && Profiles.Count == 0 && Stacks.Count == 0;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Bindings == null) Bindings = new List<LibraryBinding>();
            if (Customs == null) Customs = new List<CustomCommand>();
            if (Profiles == null) Profiles = new List<Profile>();
            if (Stacks == null) Stacks = new List<StackDef>();
            if (RequiredKinds == null) RequiredKinds = new List<string>();
        }

        /// <summary>Kiểm gói khi đọc: id rỗng/trùng là gói hỏng.</summary>
        internal void Validate()
        {
            Check(Customs.Select(c => c?.Id), "lệnh riêng");
            Check(Profiles.Select(p => p?.Id), "profile");
            Check(Stacks.Select(s => s?.Id), "bộ chạy");
        }

        private static void Check(IEnumerable<string> ids, string what)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Gói có " + what + " thiếu id.");
                if (!seen.Add(id))
                    throw new InvalidDataException("Gói có " + what + " trùng id: \"" + id + "\".");
            }
        }
    }

    /// <summary>Lõi đóng gói theo workspace: mở rộng lựa chọn, xem trước, dựng phần workspace của manifest. Không UI, không ghi file.</summary>
    public static class WorkspacePack
    {
        /// <summary>
        /// Mở rộng lựa chọn: bộ chạy kéo theo profile (và tool/lệnh của bước không có profile); profile library/custom kéo theo
        /// tool thư viện / lệnh riêng. Id không còn trong workspace bị bỏ. Không đổi <paramref name="selection"/>.
        /// </summary>
        public static WorkspacePackSelection Resolve(Workspace ws, WorkspacePackSelection selection)
        {
            selection = selection ?? new WorkspacePackSelection();
            var r = new WorkspacePackSelection();

            foreach (var id in selection.ToolIds ?? new List<string>())
                Add(r.ToolIds, r, WorkspacePackSelection.ToolKey, id, null);
            foreach (var id in selection.CustomIds ?? new List<string>())
                if (ws.Customs.Any(c => Same(c.Id, id)))
                    Add(r.CustomIds, r, WorkspacePackSelection.CustomKey, id, null);
            foreach (var id in selection.ProfileIds ?? new List<string>())
                if (ws.Profiles.Any(p => Same(p.Id, id)))
                    Add(r.ProfileIds, r, WorkspacePackSelection.ProfileKey, id, null);

            foreach (var id in selection.StackIds ?? new List<string>())
            {
                var stack = ws.Stacks.Find(s => Same(s.Id, id));
                if (stack == null) continue;
                if (!r.StackIds.Contains(stack.Id, StringComparer.OrdinalIgnoreCase))
                    r.StackIds.Add(stack.Id);
                var why = "Bộ chạy \"" + stack.Label + "\" dùng";
                foreach (var step in stack.Steps)
                {
                    if (!string.IsNullOrEmpty(step.ProfileId))
                    {
                        var profile = ws.Profiles.Find(p => Same(p.Id, step.ProfileId));
                        if (profile != null)
                            Add(r.ProfileIds, r, WorkspacePackSelection.ProfileKey, profile.Id, why);
                    }
                    else if (ProfileKind.IsLibrary(step.Kind) && !string.IsNullOrEmpty(step.ActionId))
                        Add(r.ToolIds, r, WorkspacePackSelection.ToolKey, step.ActionId, why);
                    else if (ProfileKind.IsCustom(step.Kind) && ws.Customs.Any(c => Same(c.Id, step.ActionId)))
                        Add(r.CustomIds, r, WorkspacePackSelection.CustomKey, ws.Customs.Find(c => Same(c.Id, step.ActionId)).Id, why);
                }
            }

            // Profile (tick tay hoặc vừa kéo theo) → tool / lệnh riêng nó trỏ tới. Duyệt theo chỉ số vì danh sách không đổi trong vòng này.
            foreach (var profileId in r.ProfileIds.ToList())
            {
                var profile = ws.Profiles.Find(p => Same(p.Id, profileId));
                if (profile == null || string.IsNullOrEmpty(profile.RefId)) continue;
                var why = "Profile \"" + profile.Label + "\" dùng";
                if (ProfileKind.IsLibrary(profile.Kind))
                    Add(r.ToolIds, r, WorkspacePackSelection.ToolKey, profile.RefId, why);
                else if (ProfileKind.IsCustom(profile.Kind))
                {
                    var custom = ws.Customs.Find(c => Same(c.Id, profile.RefId));
                    if (custom != null)
                        Add(r.CustomIds, r, WorkspacePackSelection.CustomKey, custom.Id, why);
                }
            }
            return r;
        }

        /// <summary>Thống kê và cảnh báo của gói sẽ tạo từ <paramref name="selection"/> (tự mở rộng bằng <see cref="Resolve"/>).</summary>
        public static WorkspacePackPreview Preview(Workspace ws, ToolStore library, WorkspacePackSelection selection)
        {
            var resolved = Resolve(ws, selection);
            var preview = new WorkspacePackPreview { Selection = resolved };
            BuildData(ws, library, resolved, preview.Warnings);
            preview.ToolCount = resolved.ToolIds.Count(id => library != null && library.Find(id) != null);
            preview.ProfileCount = resolved.ProfileIds.Count;
            preview.StackCount = resolved.StackIds.Count;
            preview.CustomCount = resolved.CustomIds.Count;
            return preview;
        }

        /// <summary>Tool thư viện sẽ được kèm (theo selection đã Resolve); id không còn trong thư viện bị bỏ.</summary>
        public static List<ToolEntry> ToolsOf(ToolStore library, WorkspacePackSelection resolved) =>
            resolved.ToolIds.Select(id => library?.Find(id)).Where(t => t != null).ToList();

        /// <summary>
        /// Dựng phần workspace của manifest từ selection đã Resolve. Bản sao độc lập với workspace nguồn; Cwd tuyệt đối của lệnh riêng
        /// đổi thành tương đối nếu nằm trong gốc workspace, ngược lại để trống (kèm cảnh báo).
        /// </summary>
        public static WorkspacePackData BuildData(Workspace ws, ToolStore library, WorkspacePackSelection resolved, IList<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            var data = new WorkspacePackData();

            foreach (var id in resolved.ToolIds)
            {
                var tool = library?.Find(id);
                if (tool == null)
                {
                    warnings.Add("Tool thư viện \"" + id + "\" không còn trong thư viện, sẽ không có trong gói.");
                    continue;
                }
                var bound = ws.FindLibrary(id);
                data.Bindings.Add(new LibraryBinding { ToolId = tool.Id, Name = tool.Name, Args = bound?.Args });
            }

            foreach (var id in resolved.CustomIds)
            {
                var src = ws.Customs.Find(c => Same(c.Id, id));
                if (src == null) continue;
                var copy = new CustomCommand { Id = src.Id, Label = src.Label, CommandLine = src.CommandLine, Window = src.Window };
                copy.Cwd = PortableCwd(ws, src.Cwd, out var dropped);
                if (dropped)
                    warnings.Add("Lệnh riêng \"" + src.Label + "\" chạy ở thư mục ngoài workspace (" + "đường dẫn máy bị bỏ" + "); máy nhận sẽ chạy ở gốc workspace.");
                data.Customs.Add(copy);
            }

            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in resolved.ProfileIds)
            {
                var src = ws.Profiles.Find(p => Same(p.Id, id));
                if (src == null) continue;
                data.Profiles.Add(CloneProfile(src));
                if (ProfileKind.IsLibrary(src.Kind))
                {
                    if (!string.IsNullOrEmpty(src.RefId) && !resolved.ToolIds.Contains(src.RefId, StringComparer.OrdinalIgnoreCase))
                        warnings.Add("Profile \"" + src.Label + "\" trỏ tool \"" + src.RefId + "\" nhưng tool này không có trong gói.");
                }
                else if (ProfileKind.IsCustom(src.Kind))
                {
                    if (!resolved.CustomIds.Contains(src.RefId ?? "", StringComparer.OrdinalIgnoreCase))
                        warnings.Add("Profile \"" + src.Label + "\" trỏ lệnh riêng \"" + src.RefId + "\" không có trong workspace/gói.");
                }
                else if (!string.IsNullOrEmpty(src.Kind))
                    kinds.Add(src.Kind);
            }

            foreach (var id in resolved.StackIds)
            {
                var src = ws.Stacks.Find(s => Same(s.Id, id));
                if (src == null) continue;
                var copy = new StackDef { Id = src.Id, Label = src.Label };
                foreach (var step in src.Steps)
                {
                    copy.Steps.Add(new StackStep
                    {
                        Kind = step.Kind, ActionId = step.ActionId, VariantId = step.VariantId, ProfileId = step.ProfileId,
                        Wait = step.Wait, Port = step.Port, Group = step.Group,
                    });
                    if (string.IsNullOrEmpty(step.ProfileId) && !ProfileKind.IsRef(step.Kind) && !string.IsNullOrEmpty(step.Kind))
                        kinds.Add(step.Kind);
                    if (!string.IsNullOrEmpty(step.ProfileId) && !resolved.ProfileIds.Contains(step.ProfileId, StringComparer.OrdinalIgnoreCase))
                        warnings.Add("Bộ chạy \"" + src.Label + "\" có bước trỏ profile không còn trong workspace; bước này sẽ mất profile.");
                }
                data.Stacks.Add(copy);
            }

            data.RequiredKinds.AddRange(kinds.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            if (data.RequiredKinds.Count > 0)
                warnings.Add("Workspace nhận cần gắn sẵn tool dựng sẵn: " + string.Join(", ", data.RequiredKinds) + ".");
            return data;
        }

        /// <summary>Cwd để đưa vào gói: rỗng/tương đối giữ nguyên; tuyệt đối trong gốc workspace → tương đối; ngoài → rỗng (dropped).</summary>
        internal static string PortableCwd(Workspace ws, string cwd, out bool dropped)
        {
            dropped = false;
            if (string.IsNullOrWhiteSpace(cwd))
                return cwd;
            var expanded = Environment.ExpandEnvironmentVariables(cwd.Trim());
            if (!Path.IsPathRooted(expanded))
                return cwd;

            var root = ws.ResolvedPath;
            if (!string.IsNullOrWhiteSpace(root))
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
                var full = Path.GetFullPath(expanded).TrimEnd('\\', '/');
                if (string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase))
                    return ".";
                if (full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(fullRoot.Length + 1).Replace('\\', '/');
            }
            dropped = true;
            return null;
        }

        internal static Profile CloneProfile(Profile src) => new Profile
        {
            Id = src.Id, Label = src.Label, Kind = src.Kind, ActionId = src.ActionId, VariantId = src.VariantId, RefId = src.RefId,
            Vals = src.Vals == null ? new Dictionary<string, string>() : new Dictionary<string, string>(src.Vals),
        };

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static void Add(List<string> list, WorkspacePackSelection r, string kind, string id, string reason)
        {
            if (string.IsNullOrWhiteSpace(id) || list.Contains(id, StringComparer.OrdinalIgnoreCase))
                return;
            list.Add(id);
            if (reason != null)
                r.Reasons[kind + ":" + id] = reason;
        }
    }
}
