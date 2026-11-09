using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core.Agents
{
    /// <summary>Ảnh chụp các đối tượng hoàn tác được (JSON chuẩn hoá theo từng đối tượng) của một workspace và kho tool.</summary>
    public sealed class UndoSnapshot
    {
        internal sealed class Item
        {
            public string Type;
            public string Id;
            public string Json;
        }

        internal readonly Dictionary<string, Item> Items = new Dictionary<string, Item>(StringComparer.Ordinal);

        internal void Add(string type, string id, string json) =>
            Items[ActivityUndo.Key(type, id)] = new Item { Type = type, Id = id, Json = json };
    }

    /// <summary>
    /// Hoàn tác từng thay đổi lẻ ở Nhật ký. Chỉ khôi phục đúng đối tượng bị đổi (không khôi phục cả file), và chỉ khi đối tượng đó
    /// chưa bị sửa sau thao tác: băm hiện tại phải còn khớp băm bản sau đã ghi.
    /// </summary>
    public static class ActivityUndo
    {
        public const string TypeProfile = "profile";
        public const string TypeStack = "stack";
        public const string TypeCustom = "custom";
        public const string TypeBinding = "binding";
        public const string TypeBuiltin = "builtin";
        public const string TypeTool = "tool";

        public const string UndoAction = "undo";
        public const string NoData = "Dòng này không có dữ liệu hoàn tác.";
        public const string Modified = "Đối tượng đã bị sửa sau đó nên không hoàn tác được.";
        public const string AlreadyUndone = "Đã hoàn tác rồi.";

        private const int MaxChanges = 8;

        /// <summary>Các công cụ MCP có thể sinh dữ liệu hoàn tác (thay đổi profile, bộ chạy, lệnh riêng, tool).</summary>
        public static readonly HashSet<string> UndoableActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "register_tool", "update_tool", "unregister_tool", "add_tool", "remove_tool", "bind_library_tool",
            "create_profile", "update_profile", "delete_profile", "create_custom", "update_custom", "delete_custom",
            "create_stack", "update_stack", "delete_stack", "save", "delete", "attach",
        };

        internal static string Key(string type, string id) => type + "|" + (id ?? "").ToLowerInvariant();

        // ---------- Chụp và so sánh ----------

        /// <summary>Chụp trạng thái hiện tại. <paramref name="workspace"/> hoặc <paramref name="tools"/> có thể null (phiên toàn máy không có workspace).</summary>
        public static UndoSnapshot Capture(Workspace workspace, ToolStore tools)
        {
            var snap = new UndoSnapshot();
            if (workspace != null)
            {
                foreach (var p in workspace.Profiles) snap.Add(TypeProfile, p.Id, Normalize(p));
                foreach (var s in workspace.Stacks) snap.Add(TypeStack, s.Id, Normalize(s));
                foreach (var c in workspace.Customs) snap.Add(TypeCustom, c.Id, Normalize(c));
                foreach (var b in workspace.Bindings) snap.Add(TypeBuiltin, b.ToolId, Normalize(b));
            }
            if (tools != null)
                foreach (var t in tools.Tools) snap.Add(TypeTool, t.Id, Normalize(t));
            return snap;
        }

        /// <summary>
        /// Dữ liệu hoàn tác cho những gì khác nhau giữa hai ảnh chụp; null nếu không có gì đổi (hoặc đổi quá nhiều để hoàn tác an toàn).
        /// Đối tượng đầu là chính, các đối tượng còn lại nằm ở Also.
        /// </summary>
        public static ActivityUndoData Build(UndoSnapshot before, UndoSnapshot after)
        {
            if (before == null || after == null)
                return null;

            var changes = new List<ActivityUndoData>();
            foreach (var key in before.Items.Keys.Concat(after.Items.Keys.Where(k => !before.Items.ContainsKey(k))))
            {
                before.Items.TryGetValue(key, out var b);
                after.Items.TryGetValue(key, out var a);
                if (b != null && a != null && b.Json == a.Json)
                    continue;
                var any = b ?? a;
                changes.Add(new ActivityUndoData
                {
                    Type = any.Type,
                    Id = any.Id,
                    Before = b?.Json,
                    AfterHash = a == null ? "" : Hash(a.Json),
                });
            }

            if (changes.Count == 0 || changes.Count > MaxChanges)
                return null;
            var primary = changes[0];
            primary.Token = Guid.NewGuid().ToString("N");
            if (changes.Count > 1)
                primary.Also = changes.Skip(1).ToList();
            return primary;
        }

        // ---------- Kiểm ----------

        /// <summary>
        /// Có hoàn tác được không. <paramref name="workspace"/> là workspace của dòng nhật ký (null nếu đã bị gỡ hoặc phiên toàn máy),
        /// <paramref name="tools"/> là kho tool hiện tại.
        /// </summary>
        public static bool CanUndo(ActivityEntry entry, Workspace workspace, ToolStore tools, out string reason)
        {
            reason = null;
            if (entry?.Undo == null)
            {
                reason = NoData;
                return false;
            }

            foreach (var item in Items(entry.Undo))
            {
                if (item.Type == TypeTool ? tools == null : workspace == null)
                {
                    reason = item.Type == TypeTool ? "Không đọc được kho tool." : "Workspace của việc này đã bị gỡ khỏi Toolbox.";
                    return false;
                }
                if (!string.Equals(Hash(CurrentJson(item.Type, item.Id, workspace, tools)), item.AfterHash ?? "", StringComparison.Ordinal))
                {
                    reason = Modified;
                    return false;
                }
            }
            return true;
        }

        /// <summary>Dòng này đã có dòng "undo" trỏ về chưa.</summary>
        public static bool IsUndone(ActivityEntry entry, IEnumerable<ActivityEntry> log) =>
            !string.IsNullOrEmpty(entry?.Undo?.Token)
            && log != null
            && log.Any(e => e.Action == UndoAction && string.Equals(e.UndoOf, entry.Undo.Token, StringComparison.Ordinal));

        // ---------- Áp dụng ----------

        /// <summary>
        /// Hoàn tác một dòng nhật ký: kiểm lại dưới khoá, khôi phục đúng các đối tượng đã đổi, rồi ghi một dòng "undo" vào nhật ký.
        /// Ném <see cref="InvalidOperationException"/> (kèm lý do) nếu không hoàn tác được; khi đó không đổi gì.
        /// </summary>
        public static string Undo(ActivityEntry entry, string workspacesPath, string toolsPath, string activityPath)
        {
            if (entry?.Undo == null)
                throw new InvalidOperationException(NoData);
            if (IsUndone(entry, ActivityLog.Read(activityPath, int.MaxValue)))
                throw new InvalidOperationException(AlreadyUndone);

            string message = null;
            var toolsChanged = false;
            using (ToolStore.Lock())
            {
                var tools = ToolStore.Load(toolsPath);
                tools.Actor = "gui";
                WorkspaceStore.Update(workspacesPath, store =>
                {
                    var workspace = string.IsNullOrEmpty(entry.WorkspaceId) ? null : store.Find(entry.WorkspaceId);
                    if (!CanUndo(entry, workspace, tools, out var reason))
                        throw new InvalidOperationException(reason);
                    message = Restore(entry.Undo, workspace, tools, out toolsChanged, out var workspaceChanged);
                    return workspaceChanged;
                });
                if (toolsChanged)
                    tools.Save();
            }

            ActivityLog.Append(activityPath, new ActivityEntry
            {
                AgentName = "Bạn",
                WorkspaceId = entry.WorkspaceId,
                Action = UndoAction,
                Target = ($"{entry.Action} {entry.Target}").Trim(),
                Result = "applied",
                UndoOf = entry.Undo.Token,
            });
            return message;
        }

        /// <summary>Sửa trong bộ nhớ (không ghi file): đã kiểm hết trước khi đổi gì.</summary>
        private static string Restore(ActivityUndoData data, Workspace workspace, ToolStore tools, out bool toolsChanged, out bool workspaceChanged)
        {
            var parsed = Items(data).Select(i => new { Item = i, Value = i.Before == null ? null : FromJson(i.Type, i.Before) }).ToList();
            toolsChanged = false;
            workspaceChanged = false;
            var notes = new List<string>();

            foreach (var p in parsed)
            {
                var item = p.Item;
                var name = TypeName(item.Type);
                var exists = CurrentJson(item.Type, item.Id, workspace, tools) != null;
                notes.Add(item.Before == null ? $"đã xoá {name} \"{item.Id}\" (agent tạo)"
                    : exists ? $"đã khôi phục bản trước của {name} \"{item.Id}\""
                    : $"đã tạo lại {name} \"{item.Id}\"");

                if (item.Type == TypeTool)
                {
                    toolsChanged = true;
                    if (p.Value == null)
                        tools.Remove(item.Id);
                    else
                    {
                        var restored = (ToolEntry)p.Value;
                        var current = tools.Find(item.Id);
                        if (current == null)
                            tools.Add(restored);
                        else
                        {
                            restored.RunCount = current.RunCount;
                            restored.LastUsedAt = current.LastUsedAt;
                            tools.Update(restored);
                        }
                    }
                    continue;
                }

                workspaceChanged = true;
                switch (item.Type)
                {
                    case TypeProfile: Put(workspace.Profiles, x => x.Id, item.Id, (Profile)p.Value); break;
                    case TypeStack: Put(workspace.Stacks, x => x.Id, item.Id, (StackDef)p.Value); break;
                    case TypeCustom: Put(workspace.Customs, x => x.Id, item.Id, (CustomCommand)p.Value); break;
                    // Dữ liệu hoàn tác cũ (tool thư viện gắn riêng): nay là binding thường trong Bindings.
                    case TypeBinding: Put(workspace.Bindings, x => x.ToolId, item.Id, ToBinding((LibraryBinding)p.Value)); break;
                    case TypeBuiltin: Put(workspace.Bindings, x => x.ToolId, item.Id, (ToolBinding)p.Value); break;
                    default: throw new InvalidOperationException("Không biết loại đối tượng \"" + item.Type + "\".");
                }
            }

            var text = string.Join("; ", notes);
            return "Đã hoàn tác: " + text + ".";
        }

        private static ToolBinding ToBinding(LibraryBinding legacy) =>
            legacy == null ? null : new ToolBinding { ToolId = legacy.ToolId, Args = legacy.Args };

        private static LibraryBinding ToLegacy(ToolBinding binding) =>
            binding == null ? null : new LibraryBinding { ToolId = binding.ToolId, Args = binding.Args };

        private static void Put<T>(List<T> list, Func<T, string> key, string id, T value) where T : class
        {
            var index = list.FindIndex(x => string.Equals(key(x), id, StringComparison.OrdinalIgnoreCase));
            if (value == null)
            {
                if (index >= 0)
                    list.RemoveAt(index);
            }
            else if (index >= 0)
                list[index] = value;
            else
                list.Add(value);
        }

        // ---------- Đối tượng và JSON ----------

        private static IEnumerable<ActivityUndoData> Items(ActivityUndoData data)
        {
            yield return data;
            if (data.Also != null)
                foreach (var more in data.Also)
                    yield return more;
        }

        private static string TypeName(string type)
        {
            switch (type)
            {
                case TypeProfile: return "profile";
                case TypeStack: return "bộ chạy";
                case TypeCustom: return "lệnh riêng";
                case TypeBinding: return "tool gắn vào workspace";
                case TypeBuiltin: return "tool dự án";
                default: return "tool";
            }
        }

        /// <summary>JSON chuẩn hoá của đối tượng hiện tại; null nếu không có.</summary>
        private static string CurrentJson(string type, string id, Workspace workspace, ToolStore tools)
        {
            bool Same(string a) => string.Equals(a, id, StringComparison.OrdinalIgnoreCase);
            switch (type)
            {
                case TypeProfile: return NormalizeOrNull(workspace?.Profiles.Find(x => Same(x.Id)));
                case TypeStack: return NormalizeOrNull(workspace?.Stacks.Find(x => Same(x.Id)));
                case TypeCustom: return NormalizeOrNull(workspace?.Customs.Find(x => Same(x.Id)));
                case TypeBinding: return NormalizeOrNull(ToLegacy(workspace?.Bindings.Find(x => Same(x.ToolId))));
                case TypeBuiltin: return NormalizeOrNull(workspace?.Bindings.Find(x => Same(x.ToolId)));
                case TypeTool: return NormalizeOrNull(tools?.Find(id));
                default: return null;
            }
        }

        private static string NormalizeOrNull(object value) => value == null ? null : Normalize(value);

        /// <summary>
        /// JSON ổn định của đối tượng: thứ tự trường theo Order, khoá Vals sắp xếp, và với tool thì bỏ số liệu dùng (lần chạy cuối, số lần chạy)
        /// để việc chạy tool không làm lệch băm.
        /// </summary>
        private static string Normalize(object value)
        {
            if (value is Profile p)
                value = new Profile
                {
                    Id = p.Id, Label = p.Label, Kind = p.Kind, ActionId = p.ActionId, VariantId = p.VariantId, RefId = p.RefId,
                    Vals = new SortedDictionary<string, string>(p.Vals ?? new Dictionary<string, string>(), StringComparer.Ordinal)
                        .ToDictionary(kv => kv.Key, kv => kv.Value),
                    Pairs = p.HasPairs ? p.Pairs.Select(x => x.Clone()).ToList() : null,
                };
            else if (value is ToolEntry t)
            {
                var copy = t.Clone();
                copy.LastUsedAt = null;
                copy.RunCount = 0;
                value = copy;
            }
            return ToJson(value);
        }

        private static string ToJson(object value)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(value.GetType()).WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static object FromJson(string type, string json)
        {
            Type clr;
            switch (type)
            {
                case TypeProfile: clr = typeof(Profile); break;
                case TypeStack: clr = typeof(StackDef); break;
                case TypeCustom: clr = typeof(CustomCommand); break;
                case TypeBinding: clr = typeof(LibraryBinding); break;
                case TypeBuiltin: clr = typeof(ToolBinding); break;
                case TypeTool: clr = typeof(ToolEntry); break;
                default: throw new InvalidOperationException("Không biết loại đối tượng \"" + type + "\".");
            }
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return new DataContractJsonSerializer(clr).ReadObject(ms);
            }
            catch (Exception ex) when (ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException)
            {
                throw new InvalidDataException("Bản trước trong nhật ký bị hỏng: " + ex.Message, ex);
            }
        }

        /// <summary>SHA-256 (hex thường) của JSON chuẩn hoá; chuỗi rỗng nếu đối tượng không tồn tại.</summary>
        private static string Hash(string json)
        {
            if (json == null)
                return "";
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(b => b.ToString("x2")));
        }
    }
}
