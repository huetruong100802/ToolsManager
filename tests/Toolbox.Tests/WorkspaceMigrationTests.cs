using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class WorkspaceMigrationTests
    {
        /// <summary>
        /// workspaces.json định dạng cũ (không có schemaVersion): binding preset angular/npm + một kind lạ, tool thư viện
        /// gắn qua libraryBindings (có args), profile library/custom/preset, bộ chạy có bước library (trực tiếp và qua profile).
        /// </summary>
        private const string OldJson = @"{""workspaces"":[{""id"":""w1"",""name"":""dxwms"",""path"":""C:\\x"",""color"":0,
 ""bindings"":[{""kind"":""angular"",""cwd"":""frontend""},{""kind"":""npm""},{""kind"":""ghost-tool""}],
 ""customs"":[{""id"":""c1"",""label"":""Seed"",""commandLine"":""pwsh seed.ps1""}],
 ""profiles"":[
  {""id"":""p1"",""label"":""Lib nhanh"",""kind"":""library"",""action"":null,""variant"":null,""refId"":""mytool"",""vals"":[{""Key"":""args"",""Value"":""--fast""}]},
  {""id"":""p2"",""label"":""Seed v"",""kind"":""custom"",""action"":null,""variant"":null,""refId"":""c1"",""vals"":[{""Key"":""args"",""Value"":""-v""}]},
  {""id"":""p3"",""label"":""FE"",""kind"":""angular"",""action"":""serve"",""variant"":""dev"",""vals"":[{""Key"":""port"",""Value"":""4201""}]}],
 ""stacks"":[{""id"":""s1"",""label"":""All"",""steps"":[
  {""kind"":""library"",""action"":""mytool""},
  {""kind"":""library"",""action"":"""",""profile"":""p1""},
  {""kind"":""custom"",""action"":""c1""},
  {""kind"":""angular"",""action"":""serve""}]}],
 ""libraryBindings"":[{""toolId"":""mytool"",""name"":""My tool"",""args"":""--ws""},{""toolId"":""gone"",""name"":""Đã xoá""}]}]}";

        private static void WriteLibrary(string toolsPath)
        {
            var library = ToolStore.Load(toolsPath);
            library.Add(new ToolEntry { Id = "mytool", Name = "My tool", Path = @"C:\t\my.exe" });
            library.Save();
        }

        [Fact]
        public void Load_DinhDangCu_NangDuLieuSaoLuuVaSeedThuVien()
        {
            using (var dir = new TempDir())
            {
                var toolsPath = dir.Combine("tools.json");
                WriteLibrary(toolsPath);
                var file = dir.WriteFile("workspaces.json", OldJson);

                var ws = WorkspaceStore.Load(file).Workspaces.Single();

                // Thư viện: preset đang bind được seed với Id = kind cũ; kind lạ không sinh gì.
                var library = ToolStore.Load(toolsPath);
                Assert.Equal("angular", library.Find("angular").Id);
                Assert.Equal("npm", library.Find("npm").Id);
                Assert.NotNull(library.Find("mytool"));
                Assert.Null(library.Find("ghost-tool"));
                Assert.Equal(3, library.Tools.Count);

                // Bindings gộp tool thư viện (giữ args), không xoá binding trỏ tool không tồn tại.
                Assert.Empty(ws.LibraryBindings);
                Assert.Equal("frontend", ws.Binding("angular").Cwd);
                Assert.NotNull(ws.Binding("npm"));
                Assert.NotNull(ws.Binding("ghost-tool"));
                Assert.Equal("--ws", ws.Binding("mytool").Args);
                Assert.NotNull(ws.Binding("gone"));
                Assert.Equal(5, ws.Bindings.Count);

                // Profile library → Kind = toolId; custom và preset giữ nguyên.
                var p1 = ws.Profiles.Single(p => p.Id == "p1");
                Assert.Equal("mytool", p1.Kind);
                Assert.Equal("", p1.ActionId);
                Assert.Null(p1.VariantId);
                Assert.Null(p1.RefId);
                Assert.Equal("--fast", p1.ExtraArgs);
                var p2 = ws.Profiles.Single(p => p.Id == "p2");
                Assert.Equal(ProfileKind.Custom, p2.Kind);
                Assert.Equal("c1", p2.RefId);
                var p3 = ws.Profiles.Single(p => p.Id == "p3");
                Assert.Equal("angular", p3.Kind);
                Assert.Equal("serve", p3.ActionId);
                Assert.Equal("dev", p3.VariantId);

                // Bước library → Kind = toolId, ActionId rỗng; bước qua profile theo Kind mới của profile.
                var steps = ws.Stacks.Single().Steps;
                Assert.Equal("mytool", steps[0].Kind);
                Assert.Equal("", steps[0].ActionId);
                Assert.Equal("mytool", steps[1].Kind);
                Assert.Equal("p1", steps[1].ProfileId);
                Assert.Equal(ProfileKind.Custom, steps[2].Kind);
                Assert.Equal("c1", steps[2].ActionId);
                Assert.Equal("angular", steps[3].Kind);
                Assert.Equal("serve", steps[3].ActionId);

                // Sao lưu đúng bản gốc, file mới có schema.
                Assert.Equal(OldJson, File.ReadAllText(dir.Combine("workspaces.json.v1.bak")));
                Assert.Matches("\"schemaVersion\"\\s*:\\s*" + WorkspaceMigration.CurrentSchema, File.ReadAllText(file));
            }
        }

        [Fact]
        public void Load_LanHai_KhongGhiLaiVaKhongSaoLuuThem()
        {
            using (var dir = new TempDir())
            {
                WriteLibrary(dir.Combine("tools.json"));
                var file = dir.WriteFile("workspaces.json", OldJson);
                WorkspaceStore.Load(file);
                var migrated = File.ReadAllText(file);
                var tools = File.ReadAllText(dir.Combine("tools.json"));
                var stamp = File.GetLastWriteTimeUtc(file);

                var again = WorkspaceStore.Load(file).Workspaces.Single();

                Assert.Equal(migrated, File.ReadAllText(file));
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
                Assert.Equal(tools, File.ReadAllText(dir.Combine("tools.json")));
                Assert.Single(Directory.GetFiles(dir.Path, "workspaces.json.v*.bak"));
                Assert.Equal("mytool", again.Profiles.Single(p => p.Id == "p1").Kind);
            }
        }

        [Fact]
        public void Upgrade_ChayLaiKhongDoiGi()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("workspaces.json", OldJson);
                WriteLibrary(dir.Combine("tools.json"));
                var workspaces = WorkspaceStore.Load(file).Workspaces.ToList();
                var library = ToolStore.Load(dir.Combine("tools.json"));

                Assert.False(WorkspaceMigration.Upgrade(workspaces, library));
                Assert.Equal(3, library.Tools.Count);
            }
        }

        [Fact]
        public void Upgrade_KhongCoGiCu_TraFalse()
        {
            using (var dir = new TempDir())
            {
                var library = ToolStore.Load(dir.Combine("tools.json"));
                var ws = new Workspace { Id = "w", Name = "w", Path = dir.Path };
                ws.Bindings.Add(new ToolBinding { ToolId = "ghost-tool" });
                ws.Profiles.Add(new Profile { Id = "p", Kind = ProfileKind.Custom, RefId = "c1" });

                Assert.False(WorkspaceMigration.Upgrade(new List<Workspace> { ws }, library));
                Assert.Empty(library.Tools);
                Assert.Equal("ghost-tool", ws.Bindings.Single().ToolId);
            }
        }

        [Fact]
        public void Upgrade_ToolIdTrungTenLoaiDanhRieng_GiuNguyen()
        {
            using (var dir = new TempDir())
            {
                var library = ToolStore.Load(dir.Combine("tools.json"));
                var ws = new Workspace { Id = "w", Name = "w", Path = dir.Path };
                ws.Profiles.Add(new Profile { Id = "p", Kind = ProfileKind.Library, RefId = "custom" });

                Assert.False(WorkspaceMigration.Upgrade(new List<Workspace> { ws }, library));
                Assert.Equal(ProfileKind.Library, ws.Profiles.Single().Kind);
                Assert.Equal("custom", ws.Profiles.Single().RefId);
            }
        }

        [Fact]
        public void Load_ThuVienHong_NemLoiVaGiuFileGoc()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("tools.json", "{ hỏng");
                var file = dir.WriteFile("workspaces.json", OldJson);

                Assert.Throws<InvalidDataException>(() => WorkspaceStore.Load(file));
                Assert.Equal(OldJson, File.ReadAllText(file));
                Assert.Empty(Directory.GetFiles(dir.Path, "workspaces.json.v*.bak"));
                Assert.Equal("{ hỏng", File.ReadAllText(dir.Combine("tools.json")));
            }
        }

        [Fact]
        public void Load_ThuVienTiemDuongDan_DungDuongDanDo()
        {
            using (var dir = new TempDir())
            {
                var toolsPath = dir.Combine("lib", "tools.json");
                var file = dir.WriteFile("workspaces.json", OldJson);

                WorkspaceStore.Load(file, toolsPath);

                Assert.NotNull(ToolStore.Load(toolsPath).Find("angular"));
                Assert.False(File.Exists(dir.Combine("tools.json")));
            }
        }
    }
}
