using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class HomeFeedTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        private static TaskInfo Task(string id, string key, TaskState state, DateTime endedOrStarted, int? port = null)
        {
            var task = new TaskInfo(id, new TaskSpec { WorkspaceId = "w1", Label = "Tác vụ " + id, CommandLine = "x", Port = port, Key = key }, null);
            if (state != TaskState.Running)
                task.Finish(state, state == TaskState.Failed ? 1 : 0);
            return task;
        }

        private static Workspace Ws(TempDir dir, string id = "w1", bool pinned = false)
        {
            var path = dir.Combine(id);
            Directory.CreateDirectory(path);
            return new Workspace { Id = id, Name = "Repo " + id, Path = path, Pinned = pinned };
        }

        private static HomeFeed Build(IReadOnlyList<Workspace> ws, IReadOnlyList<ToolEntry> tools = null, IReadOnlyList<TaskInfo> tasks = null,
            int pending = 0, bool agent = false, IEnumerable<SearchEntry> entries = null, UsageStore usage = null, Func<string, bool> exists = null) =>
            HomeFeed.Build(ws, tools ?? new List<ToolEntry>(), tasks ?? new List<TaskInfo>(), pending, agent, entries, usage, Now, exists ?? (p => true));

        [Fact]
        public void DeXuatChoDuyet_ThanhViecCanXuLyTroToiTrangAgent()
        {
            using (var dir = new TempDir())
            {
                var feed = Build(new[] { Ws(dir) }, pending: 3);
                var item = feed.Attention.Single();
                Assert.Equal(HomeTarget.Agents, item.Target);
                Assert.Contains("3 đề xuất", item.Title);
            }
        }

        [Fact]
        public void TacVuLoi_LayLanMoiNhatMoiKhoa_VaBoNeuDaChayLaiThanhCong()
        {
            using (var dir = new TempDir())
            {
                var failedAgain = Task("a1", "k1", TaskState.Failed, Now);
                var doneLater = Task("b2", "k2", TaskState.Done, Now);
                var failedThenDone1 = Task("c1", "k3", TaskState.Failed, Now);
                System.Threading.Thread.Sleep(15); // StartedAt của lần sau phải muộn hơn
                var failedThenDone2 = Task("c2", "k3", TaskState.Done, Now);
                var feed = Build(new[] { Ws(dir) }, tasks: new[] { failedAgain, doneLater, failedThenDone1, failedThenDone2 });
                var failed = feed.Attention.Where(i => i.Target == HomeTarget.Task).ToList();
                Assert.Single(failed);
                Assert.Same(failedAgain, failed[0].Task);
            }
        }

        [Fact]
        public void WorkspaceMatThuMuc_VaToolMatDuongDan_DuocNeu()
        {
            using (var dir = new TempDir())
            {
                var gone = new Workspace { Id = "w9", Name = "Mất", Path = dir.Combine("khong-co") };
                var tools = new List<ToolEntry>
                {
                    new ToolEntry { Id = "ok", Name = "Ổn", Path = @"C:\ok.exe" },
                    new ToolEntry { Id = "lost", Name = "Mất", Path = @"C:\lost.exe" },
                    new ToolEntry { Id = "cmd", Name = "Lệnh", Command = "git" },
                };
                var feed = Build(new[] { Ws(dir), gone }, tools, exists: p => p == @"C:\ok.exe");
                Assert.Contains(feed.Attention, i => i.Target == HomeTarget.Workspace && i.WorkspaceId == "w9");
                Assert.Contains(feed.Attention, i => i.Target == HomeTarget.Library && i.ToolId == "lost");
                Assert.DoesNotContain(feed.Attention, i => i.ToolId == "ok" || i.ToolId == "cmd");
            }
        }

        [Fact]
        public void DangChay_CoCong_ThiNutMoCong()
        {
            using (var dir = new TempDir())
            {
                var withPort = Task("p", "k1", TaskState.Running, Now, port: 4200);
                var plain = Task("q", "k2", TaskState.Running, Now);
                var feed = Build(new[] { Ws(dir) }, tasks: new[] { withPort, plain, Task("d", "k3", TaskState.Done, Now) });
                Assert.Equal(2, feed.Running.Count);
                Assert.Equal(HomeTarget.OpenPort, feed.Running.Single(r => r.Port == 4200).Target);
                Assert.Equal(HomeTarget.Task, feed.Running.Single(r => r.Port == null).Target);
            }
        }

        [Fact]
        public void TiepTuc_GomWorkspaceGhimVaMucChayDuocDungGanDay()
        {
            using (var dir = new TempDir())
            {
                var ws = Ws(dir, pinned: true);
                var entries = new[]
                {
                    new SearchEntry { Kind = SearchKind.Profile, Key = "profile:w1:p1", Title = "Dev API", Subtitle = "Profile · Repo w1", WorkspaceId = "w1", TargetId = "p1", Runnable = true },
                    new SearchEntry { Kind = SearchKind.Tool, Key = "tool:git", Title = "Git", TargetId = "git" },
                };
                var usage = UsageStore.Load(dir.Combine("usage.json"));
                usage.Record("profile:w1:p1", Now);
                usage.Record("tool:git", Now);
                var feed = Build(new[] { ws }, entries: entries, usage: usage);
                Assert.Equal(new[] { HomeTarget.Workspace, HomeTarget.Run }, feed.Continue.Select(c => c.Target));
                Assert.Equal("p1", feed.Continue[1].Entry.TargetId);
            }
        }

        [Fact]
        public void LamQuen_BaBuoc_TuDanhDauXong()
        {
            using (var dir = new TempDir())
            {
                var empty = Build(new List<Workspace>());
                Assert.Equal(3, empty.Onboarding.Count);
                Assert.All(empty.Onboarding, s => Assert.False(s.Done));
                Assert.False(empty.OnboardingDone);

                var partial = Build(new[] { Ws(dir) }, new List<ToolEntry> { new ToolEntry { Id = "a", Name = "A", Command = "a" } });
                Assert.Equal(new[] { true, true, false }, partial.Onboarding.Select(s => s.Done));

                var full = Build(new[] { Ws(dir) }, new List<ToolEntry> { new ToolEntry { Id = "a", Name = "A", Command = "a" } }, agent: true);
                Assert.True(full.OnboardingDone);
            }
        }
    }
}
