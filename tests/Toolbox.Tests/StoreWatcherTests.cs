using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Đồng bộ trực tiếp: theo dõi file, gộp thay đổi của agent vào bộ nhớ, và không ghi đè khi GUI lưu.</summary>
    public class StoreWatcherTests
    {
        private static bool WaitFor(Func<bool> condition, int milliseconds = 5000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                if (condition())
                    return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        [Fact]
        public void Watcher_GomNhieuLanGhiThanhMotLanBao()
        {
            using (var dir = new TempDir())
            using (var watcher = new StoreWatcher(dir.Path, TimeSpan.FromMilliseconds(150)))
            {
                var batches = new List<string[]>();
                watcher.Changed += files => { lock (batches) batches.Add(files.ToArray()); };

                for (var i = 0; i < 5; i++)
                {
                    File.WriteAllText(dir.Combine("tools.json"), "{\"tools\":[]}" + new string(' ', i));
                    File.WriteAllText(dir.Combine("workspaces.json"), "{}");
                }

                Assert.True(WaitFor(() => { lock (batches) return batches.Count > 0; }));
                Thread.Sleep(400);
                lock (batches)
                {
                    Assert.Single(batches);
                    Assert.Equal(new[] { "tools.json", "workspaces.json" }, batches[0]);
                }
            }
        }

        [Fact]
        public void Watcher_BoQuaFileKhongTheoDoi()
        {
            using (var dir = new TempDir())
            using (var watcher = new StoreWatcher(dir.Path, TimeSpan.FromMilliseconds(100)))
            {
                var count = 0;
                watcher.Changed += files => Interlocked.Increment(ref count);
                File.WriteAllText(dir.Combine("changes.log"), "x");
                File.WriteAllText(dir.Combine("tools.json.tmp"), "x");
                Thread.Sleep(500);
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public void Watcher_BaoKhiFileDuocThayBangFileTam()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var store = ToolStore.Load(path);
                store.Add(new ToolEntry { Name = "A", Path = @"C:\a.exe" });
                store.Save();

                using (var watcher = new StoreWatcher(dir.Path, TimeSpan.FromMilliseconds(100)))
                {
                    var seen = 0;
                    watcher.Changed += files => Interlocked.Increment(ref seen);
                    store.Add(new ToolEntry { Name = "B", Path = @"C:\b.exe" });
                    store.Save(); // File.Replace
                    Assert.True(WaitFor(() => Volatile.Read(ref seen) > 0));
                }
            }
        }

        [Fact]
        public void ToolStore_Sync_LayToolMoiCuaAgentVaGiuTickDoCuaGui()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var gui = ToolStore.Load(path);
                gui.Add(new ToolEntry { Name = "A", Path = @"C:\a.exe" });
                gui.Save();

                var agent = ToolStore.Load(path);
                agent.Add(new ToolEntry { Name = "B", Path = @"C:\b.exe" });
                agent.Save();

                Assert.True(gui.Sync());
                Assert.Equal(new[] { "a", "b" }, gui.Tools.Select(t => t.Id).OrderBy(x => x));
                Assert.False(gui.Sync()); // file không đổi nữa
            }
        }

        [Fact]
        public void ToolStore_Save_KhongDeThemDoiCuaAgent()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var gui = ToolStore.Load(path);
                gui.Add(new ToolEntry { Name = "A", Path = @"C:\a.exe" });
                gui.Save();

                // Agent thêm B và sửa A trong khi GUI chưa kịp nạp lại.
                var agent = ToolStore.Load(path);
                agent.Add(new ToolEntry { Name = "B", Path = @"C:\b.exe" });
                var a = agent.Find("a").Clone();
                a.Description = "agent sửa";
                agent.Update(a);
                agent.Save();

                // GUI thêm C rồi lưu: B và sửa đổi của agent phải còn.
                gui.Add(new ToolEntry { Name = "C", Path = @"C:\c.exe" });
                gui.Save();

                var final = ToolStore.Load(path);
                Assert.Equal(new[] { "a", "b", "c" }, final.Tools.Select(t => t.Id).OrderBy(x => x));
                Assert.Equal("agent sửa", final.Find("a").Description);
            }
        }

        [Fact]
        public void ToolStore_Save_ToolGuiXoaKhongBiThemLai()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var gui = ToolStore.Load(path);
                gui.Add(new ToolEntry { Name = "A", Path = @"C:\a.exe" });
                gui.Add(new ToolEntry { Name = "B", Path = @"C:\b.exe" });
                gui.Save();

                var agent = ToolStore.Load(path);
                agent.Add(new ToolEntry { Name = "N", Path = @"C:\n.exe" });
                agent.Save();

                gui.Remove("a");
                gui.Save();

                Assert.Equal(new[] { "b", "n" }, ToolStore.Load(path).Tools.Select(t => t.Id).OrderBy(x => x));
            }
        }

        [Fact]
        public void ToolStore_Sync_GuiDangSuaDoThiGiuBanTrongBoNho()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var gui = ToolStore.Load(path);
                gui.Add(new ToolEntry { Name = "A", Path = @"C:\a.exe" });
                gui.Save();

                var agent = ToolStore.Load(path);
                var fromAgent = agent.Find("a").Clone();
                fromAgent.Description = "agent";
                agent.Update(fromAgent);
                agent.Save();

                gui.Find("a").Description = "gui";
                gui.Sync();
                Assert.Equal("gui", gui.Find("a").Description);
            }
        }

        private static WorkspaceStore NewWorkspaceStore(TempDir dir, out string path, out string id)
        {
            var repo = dir.Combine("repo");
            Directory.CreateDirectory(repo);
            path = dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(path);
            id = store.Add(new Workspace { Name = "Repo A", Path = repo }).Id;
            store.Save();
            return store;
        }

        [Fact]
        public void WorkspaceStore_Sync_ThemWorkspaceVaProfileCuaAgent_GiuDoiTuongKhongDoi()
        {
            using (var dir = new TempDir())
            {
                var gui = NewWorkspaceStore(dir, out var path, out var id);
                var keptCustom = new CustomCommand { Id = "c1", Label = "Giữ", CommandLine = "echo 1" };
                gui.Find(id).Customs.Add(keptCustom);
                gui.Save();

                WorkspaceStore.Update(path, s =>
                {
                    var w = s.Find(id);
                    w.Customs.Add(new CustomCommand { Id = "c2", Label = "Agent", CommandLine = "echo 2" });
                    s.Add(new Workspace { Name = "Repo B", Path = dir.Path });
                    return true;
                });

                var changed = gui.Sync();
                Assert.Equal(2, changed.Count);
                Assert.Equal(2, gui.Workspaces.Count);
                var customs = gui.Find(id).Customs;
                Assert.Equal(new[] { "c1", "c2" }, customs.Select(c => c.Id));
                Assert.Same(keptCustom, customs[0]); // form đang mở vẫn trỏ đúng đối tượng cũ
                Assert.Empty(gui.Sync());
            }
        }

        [Fact]
        public void WorkspaceStore_Save_KhongDeThayDoiCuaAgent()
        {
            using (var dir = new TempDir())
            {
                var gui = NewWorkspaceStore(dir, out var path, out var id);

                WorkspaceStore.Update(path, s =>
                {
                    s.Find(id).Profiles.Add(new Profile { Id = "p1", Label = "Của agent", Kind = "x", ActionId = "a" });
                    return true;
                });

                // GUI đổi tên (chưa kịp nạp thay đổi của agent) rồi lưu.
                gui.Find(id).Name = "Repo A đổi tên";
                gui.Save();

                var final = WorkspaceStore.Load(path).Find(id);
                Assert.Equal("Repo A đổi tên", final.Name);
                Assert.Single(final.Profiles);
                Assert.Equal("p1", final.Profiles[0].Id);
            }
        }

        [Fact]
        public void WorkspaceStore_Save_WorkspaceGuiGoKhongBiThemLai()
        {
            using (var dir = new TempDir())
            {
                var gui = NewWorkspaceStore(dir, out var path, out var id);
                gui.Remove(id);
                gui.Save();
                Assert.Empty(WorkspaceStore.Load(path).Workspaces);
            }
        }
    }
}
