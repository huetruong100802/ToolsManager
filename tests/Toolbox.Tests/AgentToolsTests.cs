using System;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class AgentToolsTests
    {
        private static ToolStore Store(TempDir dir) => ToolStore.Load(dir.Combine("tools.json"));

        [Fact]
        public void FileNho_ChepCaThuMucVaoThuVien()
        {
            using (var dir = new TempDir())
            {
                var source = dir.WriteFile(@"src\tool.exe", "exe");
                dir.WriteFile(@"src\tool.dll", "dll");
                var store = Store(dir);

                var outcome = AgentTools.Register(store, new AgentTools.Request { Name = "Tool Demo", Path = source, Tags = new[] { "demo" }.ToList() });

                var tool = outcome.Tool;
                Assert.True(outcome.Created);
                Assert.Equal("tool-demo", tool.Id);
                Assert.Equal(StoreMode.Copy, tool.Store);
                Assert.Equal(PackMode.Folder, tool.Pack);
                Assert.Equal(dir.Combine("library", "tool-demo", "tool.exe"), tool.Path);
                Assert.True(File.Exists(dir.Combine("library", "tool-demo", "tool.dll")));
                Assert.StartsWith("tool.exe|3|", tool.Fingerprint);
                Assert.Empty(outcome.Warnings);
            }
        }

        [Fact]
        public void FileNhoTrongThuMucLon_KhongChepCaThuMuc()
        {
            using (var dir = new TempDir())
            {
                // Chỉ file exe nhỏ, nhưng thư mục chứa nó vượt ngưỡng: phải giữ đường dẫn gốc, không chép.
                var source = dir.WriteFile(@"src\tool.exe", "exe");
                using (var big = File.Create(dir.Combine("src", "du-lieu.bin")))
                    big.SetLength(Library.CopyLimitBytes + 1);
                var store = Store(dir);

                var tool = AgentTools.Register(store, new AgentTools.Request { Name = "Tool", Path = source }).Tool;

                Assert.Equal(StoreMode.Link, tool.Store);
                Assert.Equal(source, tool.Path);
                Assert.False(Directory.Exists(dir.Combine("library")));
            }
        }

        [Fact]
        public void TrungPathHoacTrungNoiDung_CapNhatKhongTaoMoi()
        {
            using (var dir = new TempDir())
            {
                var source = dir.WriteFile(@"src\a.bat", "@echo off");
                var store = Store(dir);
                var first = AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = source });

                // Trùng nội dung nhưng đường dẫn là bản chép của lần trước: vẫn là cùng một tool.
                var again = AgentTools.Register(store, new AgentTools.Request { Name = "A moi", Path = source, Notes = "ghi chu" });
                Assert.False(again.Created);
                Assert.Equal(first.Tool.Id, again.Tool.Id);
                Assert.Single(store.Tools);
                Assert.Equal("A moi", store.Tools.Single().Name);
                Assert.Equal("ghi chu", store.Tools.Single().Notes);

                // Agent truyền luôn đường dẫn trong thư viện (lấy từ list_tools): không chép đè chính nó.
                AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = first.Tool.Path });
                Assert.Single(store.Tools);
                Assert.True(File.Exists(first.Tool.Path));
            }
        }

        [Fact]
        public void DauVaoSai_NemLoiVaDanhSachKhongDoi()
        {
            using (var dir = new TempDir())
            {
                var store = Store(dir);

                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = " ", Path = dir.Combine("a.exe") }));
                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = @"tuong-doi\a.exe" }));
                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = dir.Combine("khong-co.exe") }));
                Assert.Empty(store.Tools);
                Assert.False(Directory.Exists(dir.Combine("library")));
            }
        }

        [Fact]
        public void AppTrongProgramFiles_KhongChep_PackNone()
        {
            using (var dir = new TempDir())
            {
                var store = Store(dir);
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                var outcome = AgentTools.Register(store, new AgentTools.Request { Name = "Cai san", Path = programFiles });

                Assert.Equal(PackMode.None, outcome.Tool.Pack);
                Assert.Equal(StoreMode.Link, outcome.Tool.Store);
                Assert.Contains(outcome.Warnings, w => w.Contains("Program Files"));
                Assert.False(Directory.Exists(dir.Combine("library")));
            }
        }

        [Fact]
        public void CoSetupPath_ChepBoCai_PackSetup()
        {
            using (var dir = new TempDir())
            {
                var installer = dir.WriteFile(@"tai\SetupVSPE.msi", "msi");
                var installed = dir.Combine("Chua-cai", "vspe.exe");
                var store = Store(dir);

                var tool = AgentTools.Register(store, new AgentTools.Request { Name = "VSPE", Path = installed, SetupPath = installer }).Tool;

                Assert.Equal(PackMode.Setup, tool.Pack);
                Assert.Equal(StoreMode.Link, tool.Store);
                Assert.Equal(installed, tool.Path);
                Assert.Equal(dir.Combine("library", "vspe", "setup", "SetupVSPE.msi"), tool.Setup);
                Assert.True(File.Exists(tool.Setup));
                Assert.True(tool.CanInstall);
            }
        }

        [Fact]
        public void SetupPathSaiDuoiHoacKhongCo_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var zip = dir.WriteFile(@"tai\setup.zip", "zip");
                var store = Store(dir);

                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = dir.Combine("a.exe"), SetupPath = zip }));
                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = dir.Combine("a.exe"), SetupPath = dir.Combine("khong-co.msi") }));
                Assert.Empty(store.Tools);
            }
        }

        [Fact]
        public void IdTrung_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var first = dir.WriteFile(@"src\one.bat", "1");
                var second = dir.WriteFile(@"src2\two.bat", "2");
                var store = Store(dir);
                AgentTools.Register(store, new AgentTools.Request { Name = "Sim", Path = first, Id = "sim" });

                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request { Name = "Sim", Path = second, Id = "sim" }));
                Assert.Single(store.Tools);
            }
        }

        [Fact]
        public void Update_DoiTenVaPath_ThanhLink()
        {
            using (var dir = new TempDir())
            {
                var source = dir.WriteFile(@"src\a.exe", "a");
                var moved = dir.WriteFile(@"moi\a2.exe", "a2");
                var store = Store(dir);
                var created = AgentTools.Register(store, new AgentTools.Request { Name = "A", Path = source }).Tool;

                var tool = AgentTools.Update(store, created.Id, new AgentTools.Request { Name = "A2", Path = moved }).Tool;

                Assert.Equal(created.Id, tool.Id);
                Assert.Equal("A2", tool.Name);
                Assert.Equal(moved, tool.Path);
                Assert.Equal(StoreMode.Link, tool.Store);
            }
        }

        [Fact]
        public void Update_IdKhongCo_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<ArgumentException>(() => AgentTools.Update(Store(dir), "khong-co", new AgentTools.Request { Name = "x" }));
        }
    }
}
