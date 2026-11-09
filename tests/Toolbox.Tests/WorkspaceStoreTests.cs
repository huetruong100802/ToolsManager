using System.IO;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class WorkspaceStoreTests
    {
        [Fact]
        public void Load_FileChuaCo_TraDanhSachRong()
        {
            using (var dir = new TempDir())
                Assert.Empty(WorkspaceStore.Load(dir.Combine("workspaces.json")).Workspaces);
        }

        [Fact]
        public void SaveRoiLoad_GiuDuToanBoDuLieu()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("workspaces.json");
                var store = WorkspaceStore.Load(file);
                var ws = store.Add(new Workspace { Name = "dxwms", Path = dir.Path });
                ws.Bindings.Add(new ToolBinding { ToolId = "angular", Cwd = "frontend" });
                ws.Customs.Add(new CustomCommand { Id = "c1", Label = "Nạp dữ liệu", CommandLine = "pwsh seed.ps1", Window = true });
                ws.Profiles.Add(new Profile { Id = "p1", Label = "FE Đối tác", Kind = "angular", ActionId = "serve", VariantId = "dev", Vals = { ["port"] = "4201" } });
                ws.Stacks.Add(new StackDef
                {
                    Id = "s1", Label = "Cả hệ thống",
                    Steps = { new StackStep { Kind = "dotnet", ActionId = "run", Wait = WaitKind.Port, Port = 5000 } },
                });
                store.Save();

                var loaded = WorkspaceStore.Load(file).Workspaces.Single();
                Assert.Equal("dxwms", loaded.Name);
                Assert.Equal("frontend", loaded.Binding("ANGULAR").Cwd);
                Assert.True(loaded.Customs.Single().Window);
                Assert.Equal("4201", loaded.Profiles.Single().Vals["port"]);
                Assert.Equal(5000, loaded.Stacks.Single().Steps.Single().Port);
                Assert.Equal(WaitKind.Port, loaded.Stacks.Single().Steps.Single().Wait);
            }
        }

        [Fact]
        public void Load_TruongThieu_ThanhDanhSachRongKhongNull()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("workspaces.json", "{\"workspaces\":[{\"id\":\"a\",\"name\":\"x\",\"path\":\"C:\\\\x\",\"color\":0}]}");
                var ws = WorkspaceStore.Load(file).Workspaces.Single();

                Assert.Empty(ws.Bindings);
                Assert.Empty(ws.Customs);
                Assert.Empty(ws.Profiles);
                Assert.Empty(ws.Stacks);
            }
        }

        [Fact]
        public void Load_FileHong_NemLoiVaKhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("workspaces.json", "{ không phải json");

                Assert.Throws<InvalidDataException>(() => WorkspaceStore.Load(file));
                Assert.Equal("{ không phải json", File.ReadAllText(file));
            }
        }

        [Fact]
        public void Add_CapIdMoiVaChiaDeuMau()
        {
            var store = WorkspaceStore.Load(Path.Combine(Path.GetTempPath(), "toolbox-tests", "khong-ton-tai", "w.json"));
            var made = Enumerable.Range(0, Workspace.ColorCount)
                .Select(i => store.Add(new Workspace { Name = "w" + i, Path = "C:\\w" + i })).ToList();

            Assert.Equal(Workspace.ColorCount, made.Select(w => w.Id).Distinct().Count());
            Assert.Equal(Workspace.ColorCount, made.Select(w => w.ColorIndex).Distinct().Count());
        }

        [Theory]
        [InlineData("", "Workspace cần có tên.")]
        [InlineData("  ", "Workspace cần có tên.")]
        public void Add_ThieuTen_NemLoi(string name, string message)
        {
            var store = WorkspaceStore.Load(Path.Combine(Path.GetTempPath(), "toolbox-tests", "khong-ton-tai", "w.json"));
            var ex = Assert.Throws<System.ArgumentException>(() => store.Add(new Workspace { Name = name, Path = "C:\\x" }));
            Assert.Equal(message, ex.Message);
        }

        [Fact]
        public void ResolveCwd_TuongDoiVaTuyetDoi()
        {
            using (var dir = new TempDir())
            {
                var ws = new Workspace { Name = "w", Path = dir.Path };

                Assert.Equal(dir.Path, ws.ResolveCwd(null));
                Assert.Equal(dir.Path, ws.ResolveCwd("."));
                Assert.Equal(dir.Combine("frontend"), ws.ResolveCwd("frontend"));
                Assert.Equal(@"C:\other", ws.ResolveCwd(@"C:\other"));
            }
        }

        [Fact]
        public void Remove_XoaDungWorkspace()
        {
            var store = WorkspaceStore.Load(Path.Combine(Path.GetTempPath(), "toolbox-tests", "khong-ton-tai", "w.json"));
            var a = store.Add(new Workspace { Name = "a", Path = "C:\\a" });
            store.Add(new Workspace { Name = "b", Path = "C:\\b" });

            Assert.True(store.Remove(a.Id));
            Assert.False(store.Remove(a.Id));
            Assert.Equal("b", store.Workspaces.Single().Name);
        }
    }
}
