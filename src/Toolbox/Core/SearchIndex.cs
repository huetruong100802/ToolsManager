using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;

namespace Toolbox.Core
{
    public enum SearchKind { Action, Workspace, Tool, Profile, Workflow, Custom, Quick }

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

        /// <summary>Lệnh đã ghim hoặc tool đã ghim: hiện đầu ô trống và nhỉnh hơn một chút khi xếp hạng.</summary>
        public bool Pinned { get; set; }

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

        /// <summary>
        /// Dựng từ thư viện, workspace và danh sách hành động của app. <paramref name="includeQuickCommands"/>: thêm lệnh chạy nhanh
        /// của từng workspace (<see cref="QuickCommands.For"/>, script và action của tool; lệnh riêng và profile đã có mục riêng nên không lặp)
        /// kind <see cref="SearchKind.Quick"/>, TargetId = <see cref="QuickCommand.Id"/>, Key = <see cref="UsageStore.QuickKey"/>.
        /// </summary>
        public static SearchIndex Build(IEnumerable<ToolEntry> tools, IEnumerable<Workspace> workspaces, IEnumerable<SearchEntry> actions, bool includeQuickCommands = false)
        {
            var entries = new List<SearchEntry>();
            foreach (var tool in tools ?? Enumerable.Empty<ToolEntry>())
                entries.Add(new SearchEntry
                {
                    Kind = SearchKind.Tool, Key = UsageStore.Key("tool", tool.Id), Title = tool.Name, Subtitle = "Tool trong Thư viện",
                    TargetId = tool.Id, Runnable = false, Pinned = tool.Pinned, Keywords = tool.Id + " " + string.Join(" ", tool.Tags ?? new List<string>()) + " " + tool.Description,
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
                        Kind = SearchKind.Profile, Key = UsageStore.Key("profile", profile.Id, workspace.Id), Title = profile.Label, Subtitle = "Lệnh đã ghim · " + workspace.Name,
                        WorkspaceId = workspace.Id, TargetId = profile.Id, Runnable = true, Pinned = true, Keywords = workspace.Name,
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
                if (includeQuickCommands)
                    foreach (var quick in QuickCommands.For(workspace).Where(q => q.Source == QuickSource.Script || q.Source == QuickSource.Action))
                        entries.Add(new SearchEntry
                        {
                            Kind = SearchKind.Quick, Key = UsageStore.QuickKey(quick.Id), Title = quick.Title,
                            Subtitle = "Lệnh · " + quick.Group + " · " + workspace.Name + " · " + quick.CommandLine,
                            WorkspaceId = workspace.Id, TargetId = quick.Id, Runnable = quick.IsRunnable,
                            Keywords = workspace.Name + " " + quick.CommandLine,
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
                return EmptyQuery(usage, limit, now);

            var scored = _entries.Select(e => new { Entry = e, Score = Score(e, terms, usage, now) }).Where(x => x.Score > 0).ToList();
            // Gõ tên workspace thì lệnh chạy được của workspace đó đứng trước chính workspace (Enter phải chạy được việc).
            var withCommands = new HashSet<string>(
                scored.Where(x => x.Entry.Kind != SearchKind.Workspace && x.Entry.Runnable && x.Entry.WorkspaceId != null).Select(x => x.Entry.WorkspaceId),
                StringComparer.OrdinalIgnoreCase);
            return scored
                .Select(x => new { x.Entry, Score = x.Entry.Kind == SearchKind.Workspace && withCommands.Contains(x.Entry.WorkspaceId) ? Math.Max(0.1, x.Score - 8) : x.Score })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        /// <summary>Ô trống: lệnh đã ghim, mục chạy được dùng gần đây, tool đã ghim; hành động điều hướng đến sau cùng. Mỗi mục một dòng.</summary>
        private IReadOnlyList<SearchEntry> EmptyQuery(UsageStore usage, int limit, DateTime? now)
        {
            var half = Math.Max(1, limit / 2);
            var actions = _entries.Where(e => e.Kind == SearchKind.Action).ToList();
            var result = new List<SearchEntry>();

            void AddRange(IEnumerable<SearchEntry> items, int cap, int room)
            {
                foreach (var item in items.Where(i => !result.Contains(i)).Take(cap))
                    if (result.Count < room)
                        result.Add(item);
            }

            var room = limit - Math.Min(2, actions.Count); // chừa chỗ cho vài hành động
            AddRange(_entries.Where(e => e.Pinned && e.Kind != SearchKind.Tool).OrderByDescending(e => usage?.Score(e.Key, now) ?? 0), half, room);
            var recentKeys = usage?.Recent(limit * 3) ?? (IReadOnlyList<string>)new string[0];
            AddRange(recentKeys.Select(k => _entries.FirstOrDefault(e => string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase)))
                               .Where(e => e != null && e.Kind != SearchKind.Workspace), half, room);
            AddRange(_entries.Where(e => e.Pinned && e.Kind == SearchKind.Tool).OrderByDescending(e => usage?.Score(e.Key, now) ?? 0), Math.Max(1, limit / 4), room);
            AddRange(actions, limit, limit);
            return result;
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
            return match + (entry.Runnable ? 0.5 : 0) + (entry.Pinned ? 0.3 : 0) + (usage?.Score(entry.Key, now) ?? 0);
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
