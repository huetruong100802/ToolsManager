using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    public enum SearchKind { Action, Workspace, Tool, Profile, Workflow, Custom }

    /// <summary>Một kết quả tìm được. <see cref="Runnable"/>: Enter chạy luôn; không thì Enter chỉ mở nơi chứa nó.</summary>
    public sealed class SearchEntry
    {
        public SearchKind Kind { get; set; }

        /// <summary>Khoá duy nhất, cũng là khoá mức dùng trong <see cref="UsageStore"/> (rỗng với hành động không tính mức dùng).</summary>
        public string Key { get; set; }

        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string WorkspaceId { get; set; }

        /// <summary>Id của tool, profile, workflow hoặc lệnh riêng; với hành động là mã hành động.</summary>
        public string TargetId { get; set; }

        public bool Runnable { get; set; }

        /// <summary>Từ khoá phụ (tag, id, tên workspace) để tìm, không hiện ra.</summary>
        public string Keywords { get; set; }

        internal string FoldedTitle { get; set; }
        internal string FoldedAll { get; set; }
    }

    /// <summary>
    /// Chỉ mục tìm toàn app cho bảng lệnh Ctrl+K: tool, profile, workflow, lệnh riêng, workspace và hành động.
    /// Khớp không dấu theo từng từ; thứ hạng = mức khớp (đầu tên &gt; đầu từ &gt; chứa) cộng mức dùng gần đây và tần suất.
    /// </summary>
    public sealed class SearchIndex
    {
        private readonly List<SearchEntry> _entries;

        public SearchIndex(IEnumerable<SearchEntry> entries)
        {
            _entries = entries.ToList();
            foreach (var entry in _entries)
            {
                entry.FoldedTitle = Text.Fold(entry.Title);
                entry.FoldedAll = Text.Fold(string.Join(" ", new[] { entry.Title, entry.Subtitle, entry.Keywords }.Where(x => !string.IsNullOrEmpty(x))));
            }
        }

        public IReadOnlyList<SearchEntry> Entries => _entries;

        /// <summary>Dựng từ thư viện, workspace và danh sách hành động của app.</summary>
        public static SearchIndex Build(IEnumerable<ToolEntry> tools, IEnumerable<Workspace> workspaces, IEnumerable<SearchEntry> actions)
        {
            var entries = new List<SearchEntry>();
            foreach (var tool in tools ?? Enumerable.Empty<ToolEntry>())
                entries.Add(new SearchEntry
                {
                    Kind = SearchKind.Tool, Key = UsageStore.Key("tool", tool.Id), Title = tool.Name, Subtitle = "Tool trong Thư viện",
                    TargetId = tool.Id, Runnable = false, Keywords = tool.Id + " " + string.Join(" ", tool.Tags ?? new List<string>()) + " " + tool.Description,
                });

            foreach (var workspace in workspaces ?? Enumerable.Empty<Workspace>())
            {
                if (workspace.Archived)
                    continue;
                entries.Add(new SearchEntry
                {
                    Kind = SearchKind.Workspace, Key = UsageStore.Key("workspace", workspace.Id), Title = workspace.Name, Subtitle = "Workspace · " + workspace.Path,
                    WorkspaceId = workspace.Id, TargetId = workspace.Id,
                });
                foreach (var profile in workspace.Profiles)
                    entries.Add(new SearchEntry
                    {
                        Kind = SearchKind.Profile, Key = UsageStore.Key("profile", profile.Id, workspace.Id), Title = profile.Label, Subtitle = "Profile · " + workspace.Name,
                        WorkspaceId = workspace.Id, TargetId = profile.Id, Runnable = true, Keywords = workspace.Name,
                    });
                foreach (var stack in workspace.Stacks)
                    entries.Add(new SearchEntry
                    {
                        Kind = SearchKind.Workflow, Key = UsageStore.Key("workflow", stack.Id, workspace.Id), Title = stack.Label, Subtitle = "Workflow · " + workspace.Name,
                        WorkspaceId = workspace.Id, TargetId = stack.Id, Runnable = true, Keywords = workspace.Name,
                    });
                foreach (var custom in workspace.Customs)
                    entries.Add(new SearchEntry
                    {
                        Kind = SearchKind.Custom, Key = UsageStore.Key("custom", custom.Id, workspace.Id), Title = custom.Label, Subtitle = "Lệnh riêng · " + workspace.Name,
                        WorkspaceId = workspace.Id, TargetId = custom.Id, Runnable = true, Keywords = workspace.Name + " " + custom.CommandLine,
                    });
            }

            entries.AddRange(actions ?? Enumerable.Empty<SearchEntry>());
            return new SearchIndex(entries);
        }

        /// <summary>
        /// Kết quả cho <paramref name="query"/>. Ô trống: các mục chạy được dùng gần đây nhất, rồi các hành động.
        /// Mọi từ trong truy vấn phải khớp (không dấu, không phân biệt hoa thường).
        /// </summary>
        public IReadOnlyList<SearchEntry> Query(string query, UsageStore usage, int limit = 12, DateTime? now = null)
        {
            var terms = Text.Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
            {
                var recentKeys = usage?.Recent(limit * 3) ?? (IReadOnlyList<string>)new string[0];
                var recent = recentKeys.Select(k => _entries.FirstOrDefault(e => string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase)))
                                       .Where(e => e != null && e.Kind != SearchKind.Workspace)
                                       .Take(limit)
                                       .ToList();
                if (recent.Count < limit)
                    recent.AddRange(_entries.Where(e => e.Kind == SearchKind.Action && !recent.Contains(e)).Take(limit - recent.Count));
                return recent;
            }

            return _entries
                .Select(e => new { Entry = e, Score = Score(e, terms, usage, now) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        private static double Score(SearchEntry entry, string[] terms, UsageStore usage, DateTime? now)
        {
            double match = 0;
            foreach (var term in terms)
            {
                double best;
                if (entry.FoldedTitle.StartsWith(term, StringComparison.Ordinal))
                    best = 10;
                else if (HasWordPrefix(entry.FoldedTitle, term))
                    best = 7;
                else if (entry.FoldedTitle.Contains(term))
                    best = 5;
                else if (HasWordPrefix(entry.FoldedAll, term))
                    best = 3;
                else if (entry.FoldedAll.Contains(term))
                    best = 1.5;
                else
                    return 0;
                match += best;
            }
            // Chạy được thì nhỉnh hơn một chút: gõ tên một profile thì ưu tiên profile hơn workspace cùng tên.
            return match + (entry.Runnable ? 0.5 : 0) + (usage?.Score(entry.Key, now) ?? 0);
        }

        private static bool HasWordPrefix(string text, string term)
        {
            var index = 0;
            while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
                    return true;
                index++;
            }
            return false;
        }
    }
}
