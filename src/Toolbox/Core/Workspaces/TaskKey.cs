using System.Linq;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Khoá của <see cref="TaskSpec.Key"/>: <c>workspace|tool|action|variant|profile</c>. Giao diện dựa vào đó để biết
    /// dòng nào đang chạy. Lệnh riêng dùng tool = "custom" và action = id lệnh.
    /// </summary>
    public static class TaskKey
    {
        public const string Custom = "custom";

        public static string ForAction(string workspaceId, string kind, string actionId, string variantId) =>
            string.Join("|", workspaceId, kind, actionId, variantId, "");

        public static string ForProfile(string workspaceId, Profile profile) =>
            string.Join("|", workspaceId, profile.Kind, profile.ActionId, profile.VariantId, profile.Id);

        public static string ForCustom(string workspaceId, string commandId) =>
            string.Join("|", workspaceId, Custom, commandId, "", "");

        /// <summary>5 phần của khoá; thiếu thì điền rỗng để so sánh không văng.</summary>
        public static string[] Split(string key)
        {
            var parts = (key ?? "").Split('|');
            return parts.Length >= 5 ? parts : parts.Concat(Enumerable.Repeat("", 5 - parts.Length)).ToArray();
        }
    }
}
