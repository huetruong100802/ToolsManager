using System;
using System.Collections.Generic;
using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>Nguồn của một lệnh chạy nhanh.</summary>
    public enum QuickSource
    {
        /// <summary>Script đọc từ file của tool (vd. script trong package.json: <c>npm run build</c>).</summary>
        Script,
        /// <summary>Lệnh gốc của tool hoặc lệnh con trong bảng khả năng (vd. <c>dotnet build</c>).</summary>
        Action,
        /// <summary>Lệnh riêng của workspace.</summary>
        Custom,
        /// <summary>Profile đã lưu.</summary>
        Profile,
    }

    /// <summary>
    /// Một lệnh chạy được của workspace, đã dựng xong chuỗi lệnh người dùng sẽ thấy. Chỉ để liệt kê/tìm/chạy lại:
    /// không lưu xuống đĩa, nên <see cref="Id"/> phải suy ra được từ dữ liệu workspace (không Guid, không thứ tự).
    /// </summary>
    public sealed class QuickCommand
    {
        /// <summary>
        /// Id ổn định: <c>workspaceId:nguồn:...</c>. Script/Action: <c>toolId:actionId:variantId</c>;
        /// Custom/Profile: id của lệnh riêng/profile. Cũng là phần id của khoá mức dùng (<see cref="UsageStore.QuickKey"/>).
        /// </summary>
        public string Id { get; set; }

        /// <summary>Tên hiển thị ngắn, vd. "build", "Dev API".</summary>
        public string Title { get; set; }

        /// <summary>Nhóm hiển thị: tên tool; "Lệnh riêng" cho lệnh riêng; "Profile" cho profile.</summary>
        public string Group { get; set; }

        /// <summary>Chuỗi lệnh cuối cùng, vd. <c>npm run build</c>. Có <see cref="Problem"/> thì chỉ là chuỗi gần đúng nhất.</summary>
        public string CommandLine { get; set; }

        public QuickSource Source { get; set; }

        public string WorkspaceId { get; set; }

        /// <summary>Id tool trong thư viện (kind); rỗng với lệnh riêng.</summary>
        public string ToolId { get; set; }

        /// <summary>Binding của tool trong workspace (null với lệnh riêng và profile không có binding).</summary>
        public ToolBinding Binding { get; set; }

        /// <summary>Id action của catalog (rỗng với lệnh gốc của tool); null với lệnh riêng và profile.</summary>
        public string ActionId { get; set; }

        /// <summary>Id biến thể của action; null với lệnh riêng và profile.</summary>
        public string VariantId { get; set; }

        /// <summary>Id của <see cref="CustomCommand"/> hoặc <see cref="Profile"/> với nguồn tương ứng; null với script/action.</summary>
        public string RefId { get; set; }

        /// <summary>Thư mục chạy thật.</summary>
        public string Cwd { get; set; }

        /// <summary>Lý do không chạy được ngay (profile hỏng, tool thiếu file, cần nhập giá trị…); null nếu chạy được.</summary>
        public string Problem { get; set; }

        public bool IsRunnable => Problem == null;
    }

    /// <summary>Liệt kê mọi lệnh chạy được của workspace từ tool đã gắn, lệnh riêng và profile.</summary>
    public static class QuickCommands
    {
        public const string CustomGroup = "Lệnh riêng";
        public const string ProfileGroup = "Profile";

        /// <summary>
        /// Lệnh của workspace theo thứ tự: từng tool đã gắn (lệnh gốc, rồi action/script), lệnh riêng, profile.
        /// Workspace null hoặc chưa gắn gì thì trả danh sách rỗng; không ném lỗi vì một mục hỏng.
        /// </summary>
        public static IReadOnlyList<QuickCommand> For(Workspace workspace)
        {
            var result = new List<QuickCommand>();
            if (workspace == null)
                return result;

            foreach (var binding in workspace.Bindings ?? new List<ToolBinding>())
                if (binding != null && !string.IsNullOrWhiteSpace(binding.ToolId))
                    AddTool(result, workspace, binding);

            foreach (var custom in workspace.Customs ?? new List<CustomCommand>())
                if (custom != null && !string.IsNullOrEmpty(custom.Id))
                    result.Add(new QuickCommand
                    {
                        Id = MakeId(workspace, QuickSource.Custom, custom.Id),
                        Title = string.IsNullOrWhiteSpace(custom.Label) ? custom.CommandLine : custom.Label,
                        Group = CustomGroup,
                        CommandLine = (custom.CommandLine ?? "").Trim(),
                        Source = QuickSource.Custom,
                        WorkspaceId = workspace.Id,
                        ToolId = "",
                        RefId = custom.Id,
                        Cwd = workspace.ResolveCwd(custom.Cwd),
                        Problem = string.IsNullOrWhiteSpace(custom.CommandLine) ? "Lệnh riêng chưa có dòng lệnh." : null,
                    });

            foreach (var profile in workspace.Profiles ?? new List<Profile>())
                if (profile != null && !string.IsNullOrEmpty(profile.Id))
                    result.Add(FromProfile(workspace, profile));
            return result;
        }

        private static void AddTool(List<QuickCommand> result, Workspace workspace, ToolBinding binding)
        {
            var entry = ToolCatalog.FindEntry(binding.ToolId);
            if (entry == null)
                return;
            var tool = ToolDef.From(entry);
            var cwd = CommandBuilder.ToolCwd(workspace, binding, entry);

            foreach (var action in ToolCatalog.ActionsFor(workspace, binding))
            {
                var isBase = string.IsNullOrEmpty(action.Id);
                var source = !isBase && tool.ActionsFrom != null ? QuickSource.Script : QuickSource.Action;
                foreach (var variant in action.Variants)
                {
                    var quick = new QuickCommand
                    {
                        Id = MakeId(workspace, source, entry.Id, action.Id, variant.Id),
                        Title = action.Variants.Count > 1 ? action.Label + " · " + variant.Label : action.Label,
                        Group = tool.Name,
                        Source = source,
                        WorkspaceId = workspace.Id,
                        ToolId = entry.Id,
                        Binding = binding,
                        ActionId = action.Id ?? "",
                        VariantId = variant.Id ?? "",
                        Cwd = cwd,
                    };
                    try
                    {
                        if (isBase)
                        {
                            quick.Problem = CommandBuilder.CheckTool(entry);
                            quick.CommandLine = quick.Problem == null ? CommandBuilder.ToolCommandLine(entry, binding, null) : tool.Command;
                        }
                        else
                        {
                            quick.CommandLine = CommandBuilder.Build(variant, null);
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                    {
                        quick.Problem = ex.Message;
                        quick.CommandLine = variant.Template;
                    }
                    result.Add(quick);
                }
            }
        }

        private static QuickCommand FromProfile(Workspace workspace, Profile profile)
        {
            var quick = new QuickCommand
            {
                Id = MakeId(workspace, QuickSource.Profile, profile.Id),
                Title = string.IsNullOrWhiteSpace(profile.Label) ? profile.Id : profile.Label,
                Group = ProfileGroup,
                Source = QuickSource.Profile,
                WorkspaceId = workspace.Id,
                ToolId = ProfileKind.IsCustom(profile.Kind) ? "" : profile.Kind ?? "",
                Binding = ProfileKind.IsCustom(profile.Kind) ? null : workspace.Binding(profile.Kind),
                RefId = profile.Id,
            };
            var resolved = StackResolver.Resolve(workspace, new StackStep { Kind = profile.Kind, ProfileId = profile.Id }, 0);
            quick.Problem = resolved.Problem;
            quick.CommandLine = resolved.Spec?.CommandLine ?? "";
            quick.Cwd = resolved.Spec?.Cwd ?? workspace.ResolveCwd(null);
            return quick;
        }

        /// <summary>Id ổn định từ workspace + nguồn + các id thành phần (không phân biệt hoa thường để đổi kiểu chữ trong file không làm mất mức dùng).</summary>
        private static string MakeId(Workspace workspace, QuickSource source, params string[] parts) =>
            (workspace.Id + ":" + source.ToString().ToLowerInvariant() + ":" + string.Join(":", parts.Select(p => p ?? ""))).ToLowerInvariant();
    }
}
