using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class SearchIndexTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        private static (SearchIndex index, UsageStore usage, TempDir dir) Make()
        {
            var dir = new TempDir();
            var usage = UsageStore.Load(dir.Combine("usage.json"));
            var workspace = new Workspace
            {
                Id = "w1", Name = "DxFactory", Path = @"C:\repo\dx",
                Profiles = new List<Profile> { new Profile { Id = "p1", Label = "Dev API cổng 4200" }, new Profile { Id = "p2", Label = "Build production" } },
                Stacks = new List<StackDef> { new StackDef { Id = "s1", Label = "Dev toàn bộ" } },
                Customs = new List<CustomCommand> { new CustomCommand { Id = "c1", Label = "Dọn thư mục", CommandLine = "git clean -fdx" } },
            };
            var archived = new Workspace { Id = "w2", Name = "Cũ", Path = @"C:\old", Archived = true, Profiles = new List<Profile> { new Profile { Id = "p9", Label = "Dev cũ" } } };
            var tools = new[]
            {
                new ToolEntry { Id = "git", Name = "Git", Command = "git", Tags = new List<string> { "vcs" } },
                new ToolEntry { Id = "dev-server", Name = "Máy chủ dev", Command = "dotnet" },
            };
            var actions = new[] { new SearchEntry { Kind = SearchKind.Action, Title = "Thêm workspace", TargetId = "add-workspace" } };
            return (SearchIndex.Build(tools, new[] { workspace, archived }, actions), usage, dir);
        }

        [Fact]
        public void Query_KhongDau_KhopTenCoDau()
        {
            var (index, usage, dir) = Make();
            using (dir)
            {
                Assert.Contains(index.Query("may chu", usage), e => e.TargetId == "dev-server");
                Assert.Contains(index.Query("don thu muc", usage), e => e.TargetId == "c1");
            }
        }

        [Fact]
        public void Query_MoiTuPhaiKhop_VaBoQuaWorkspaceLuuTru()
        {
            var (index, usage, dir) = Make();
            using (dir)
            {
                var hits = index.Query("dev", usage, 20, Now);
                Assert.DoesNotContain(hits, e => e.TargetId == "p9");
                Assert.Empty(index.Query("dev khongco", usage));
                Assert.Equal("p1", index.Query("dev 4200", usage).Single().TargetId);
            }
        }

        [Fact]
        public void Query_DauTenXepTruocChuaTrongTen()
        {
            var (index, usage, dir) = Make();
            using (dir)
            {
                var hits = index.Query("git", usage, 20, Now);
                Assert.Equal("git", hits[0].TargetId); // tên là "Git"
                Assert.Contains(hits, e => e.TargetId == "c1"); // chỉ khớp ở lệnh "git clean"
                Assert.True(hits.ToList().FindIndex(e => e.TargetId == "c1") > 0);
            }
        }

        [Fact]
        public void Query_MucDungNhieuVaGanDayXepLen()
        {
            var (index, usage, dir) = Make();
            using (dir)
            {
                var before = index.Query("dev", usage, 20, Now).Select(e => e.TargetId).ToList();
                var workflowKey = UsageStore.Key("workflow", "s1", "w1");
                for (var i = 0; i < 5; i++)
                    usage.Record(workflowKey, Now);
                var after = index.Query("dev", usage, 20, Now).Select(e => e.TargetId).ToList();
                Assert.Equal("s1", after[0]);
                Assert.NotEqual("s1", before[0]);
            }
        }

        [Fact]
        public void Query_OTrong_LaMucChayDuocDungGanDayRoiHanhDong()
        {
            var (index, usage, dir) = Make();
            using (dir)
            {
                usage.Record(UsageStore.Key("profile", "p2", "w1"), Now.AddHours(-2));
                usage.Record(UsageStore.Key("profile", "p1", "w1"), Now.AddHours(-1));
                var hits = index.Query("", usage, 5, Now);
                Assert.Equal(new[] { "p1", "p2" }, hits.Take(2).Select(e => e.TargetId));
                Assert.Equal("add-workspace", hits[2].TargetId);
            }
        }

        [Fact]
        public void UsageStore_GhiBen_GiuToiDaVaGoMucKhongConTonTai()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("usage.json");
                var usage = UsageStore.Load(path);
                usage.Record("a", Now);
                usage.Record("a", Now.AddMinutes(1));
                var again = UsageStore.Load(path);
                Assert.Equal(2, again.Get("a").Count);

                for (var i = 0; i < UsageStore.MaxEntries + 20; i++)
                    usage.Record("k" + i, Now.AddMinutes(i));
                Assert.Null(UsageStore.Load(path).Get("a")); // mục cũ nhất bị bỏ
                Assert.NotNull(UsageStore.Load(path).Get("k" + (UsageStore.MaxEntries + 19)));

                usage.Forget(k => k.StartsWith("k1"));
                Assert.Null(UsageStore.Load(path).Get("k10"));
            }
        }

        [Fact]
        public void UsageStore_FileHong_BatDauLai()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("usage.json", "{ hỏng");
                var usage = UsageStore.Load(path);
                Assert.Null(usage.Get("x"));
                usage.Record("x", Now);
                Assert.Equal(1, UsageStore.Load(path).Get("x").Count);
            }
        }

        [Fact]
        public void Score_GiamDanTheoNgay()
        {
            using (var dir = new TempDir())
            {
                var usage = UsageStore.Load(dir.Combine("usage.json"));
                usage.Record("a", Now);
                Assert.True(usage.Score("a", Now) > usage.Score("a", Now.AddDays(10)));
                Assert.Equal(0, usage.Score("khong-co", Now));
            }
        }
    }
}
