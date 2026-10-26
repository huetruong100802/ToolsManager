using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class PackerTests
    {
        private static ToolEntry Tool(string id, string path, PackMode pack = PackMode.Folder) =>
            new ToolEntry { Id = id, Name = id, Path = path, Pack = pack };

        [Fact]
        public void Pack_Folder_LayCaThuMuc_PathTuongDoi()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"src\sim\sim.exe", "exe");
                dir.WriteFile(@"src\sim\lib\a.dll", "dll");
                var zipPath = dir.Combine("out" + Packer.FileExtension);

                Packer.Pack(new[] { Tool("sim", exe) }, zipPath);

                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
                    Assert.Equal(new[] { "sim/lib/a.dll", "sim/sim.exe", Packer.ManifestName }, names);
                }
                Assert.Equal("sim/sim.exe", Packer.ReadManifest(zipPath).Tools.Single().Path);
            }
        }

        [Fact]
        public void Pack_FileVaNone()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"src\one\tool.exe", "exe");
                dir.WriteFile(@"src\one\other.txt", "khác");
                var zipPath = dir.Combine("out.zip");

                Packer.Pack(new[] { Tool("one", exe, PackMode.File), Tool("vspe", @"%ProgramFiles%\VSPE\vspe.exe", PackMode.None) }, zipPath);

                using (var zip = ZipFile.OpenRead(zipPath))
                    Assert.Equal(new[] { "one/tool.exe", Packer.ManifestName }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
                var tools = Packer.ReadManifest(zipPath).Tools;
                Assert.Equal("one/tool.exe", tools[0].Path);
                Assert.Equal(@"%ProgramFiles%\VSPE\vspe.exe", tools[1].Path); // giữ nguyên path gốc
            }
        }

        [Fact]
        public void Pack_PathKhongTonTai_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<FileNotFoundException>(() => Packer.Pack(new[] { Tool("x", dir.Combine("mat.exe")) }, dir.Combine("o.zip")));
        }

        [Fact]
        public void Preview_DemFileVaDungLuong()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"sim\sim.exe", "12345");
                dir.WriteFile(@"sim\b.txt", "123");

                var preview = Packer.Preview(Tool("sim", exe));

                Assert.Equal(2, preview.FileCount);
                Assert.Equal(8, preview.TotalBytes);
                Assert.False(preview.IsBroadFolder);
            }
        }

        [Fact]
        public void PackRoiExtract_KhoiPhucDungFile_PathMoiTonTai()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"src\sim\sim.exe", "exe");
                dir.WriteFile(@"src\sim\lib\a.dll", "dll");
                var folderTool = dir.Combine(@"src\scripts");
                dir.WriteFile(@"src\scripts\run.ps1", "ps");
                var zipPath = dir.Combine("out.zip");
                Packer.Pack(new[] { Tool("sim", exe), Tool("scripts", folderTool) }, zipPath);

                var dest = dir.Combine("imported");
                Directory.CreateDirectory(Path.Combine(dest, "sim")); // thư mục đã tồn tại → phải sang sim-2
                var tools = Packer.Extract(zipPath, dest, new[] { "sim", "scripts" });

                Assert.Equal(Path.Combine(dest, @"sim-2\sim.exe"), tools.Single(t => t.Id == "sim").Path);
                Assert.Equal("dll", File.ReadAllText(Path.Combine(dest, @"sim-2\lib\a.dll")));
                Assert.Equal(Path.Combine(dest, "scripts"), tools.Single(t => t.Id == "scripts").Path);
                Assert.True(File.Exists(Path.Combine(dest, @"scripts\run.ps1")));
            }
        }

        [Fact]
        public void Extract_ChiLayToolDuocChon()
        {
            using (var dir = new TempDir())
            {
                var a = dir.WriteFile(@"a\a.exe", "a");
                var b = dir.WriteFile(@"b\b.exe", "b");
                var zipPath = dir.Combine("out.zip");
                Packer.Pack(new[] { Tool("a", a), Tool("b", b) }, zipPath);

                var dest = dir.Combine("imported");
                var tools = Packer.Extract(zipPath, dest, new[] { "b" });

                Assert.Equal("b", tools.Single().Id);
                Assert.False(Directory.Exists(Path.Combine(dest, "a")));
            }
        }

        [Fact]
        public void Extract_ZipSlip_NemLoiVaKhongGiaiNenGiCa()
        {
            using (var dir = new TempDir())
            {
                var zipPath = dir.Combine("evil.zip");
                WriteZip(zipPath,
                    "{\"formatVersion\":1,\"tools\":[{\"id\":\"evil\",\"name\":\"E\",\"path\":\"evil/ok.txt\",\"pack\":\"folder\"}]}",
                    "evil/ok.txt", "evil/../../outside.txt");

                var dest = dir.Combine("imported");
                Assert.Throws<InvalidDataException>(() => Packer.Extract(zipPath, dest, new[] { "evil" }));

                Assert.False(File.Exists(dir.Combine("outside.txt")));
                Assert.False(File.Exists(Path.Combine(dest, @"evil\ok.txt")));
            }
        }

        [Fact]
        public void PackSetup_ChiGuiBoCai_ExtractGiuPathVaTraBoCaiTuyetDoi()
        {
            using (var dir = new TempDir())
            {
                var setup = dir.WriteFile(@"downloads\SetupVSPE.msi", "msi");
                dir.WriteFile(@"downloads\khac.txt", "không gửi");
                var tool = Tool("vspe", @"%ProgramFiles%\VSPE\vspe.exe", PackMode.Setup);
                tool.Setup = setup;
                var zipPath = dir.Combine("out.zip");

                Assert.Equal(3, Packer.Preview(tool).TotalBytes);
                Packer.Pack(new[] { tool }, zipPath);

                using (var zip = ZipFile.OpenRead(zipPath))
                    Assert.Equal(new[] { Packer.ManifestName, "vspe/SetupVSPE.msi" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
                var packed = Packer.ReadManifest(zipPath).Tools.Single();
                Assert.Equal("vspe/SetupVSPE.msi", packed.Setup); // không lộ đường dẫn máy gửi
                Assert.Equal(@"%ProgramFiles%\VSPE\vspe.exe", packed.Path);

                var extracted = Packer.Extract(zipPath, dir.Combine("imported"), new[] { "vspe" }).Single();
                Assert.Equal(@"%ProgramFiles%\VSPE\vspe.exe", extracted.Path);
                Assert.Equal(dir.Combine(@"imported\vspe\SetupVSPE.msi"), extracted.Setup);
                Assert.Equal("msi", File.ReadAllText(extracted.Setup));
            }
        }

        [Fact]
        public void PackSetup_ThieuBoCai_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var noSetup = Tool("a", @"C:\a.exe", PackMode.Setup);
                var missing = Tool("b", @"C:\b.exe", PackMode.Setup);
                missing.Setup = dir.Combine("mat.msi");
                Assert.Throws<FileNotFoundException>(() => Packer.Pack(new[] { noSetup }, dir.Combine("o.zip")));
                Assert.Throws<FileNotFoundException>(() => Packer.Preview(missing));
            }
        }

        [Fact]
        public void Extract_SetupCuaToolKhongPhaiPackSetup_BiBo()
        {
            using (var dir = new TempDir())
            {
                var zipPath = dir.Combine("odd.zip");
                WriteZip(zipPath,
                    "{\"formatVersion\":1,\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"C:\\\\a.exe\",\"pack\":\"none\",\"setup\":\"C:\\\\evil.exe\"}]}");

                Assert.Null(Packer.Extract(zipPath, dir.Combine("imported"), new[] { "a" }).Single().Setup);
            }
        }

        [Theory]
        [InlineData("{\"formatVersion\":1,\"tools\":[{\"id\":\"..\\\\x\",\"name\":\"E\",\"path\":\"x\"}]}")]
        [InlineData("{\"formatVersion\":1,\"tools\":[{\"id\":\"a\",\"name\":\"E\",\"path\":\"b/x.exe\"}]}")]
        [InlineData("{\"formatVersion\":1,\"tools\":[{\"id\":\"a\",\"name\":\"E\",\"path\":\"C:\\\\a.exe\",\"pack\":\"setup\"}]}")]
        [InlineData("{\"formatVersion\":1,\"tools\":[{\"id\":\"a\",\"name\":\"E\",\"path\":\"C:\\\\a.exe\",\"pack\":\"setup\",\"setup\":\"b/setup.msi\"}]}")]
        [InlineData("{\"formatVersion\":1,\"tools\":[{\"id\":\"a\",\"name\":\"E\",\"path\":\"C:\\\\a.exe\",\"pack\":\"setup\",\"setup\":\"a/\"}]}")]
        [InlineData("{\"formatVersion\":99,\"tools\":[]}")]
        [InlineData("không phải json")]
        public void ReadManifest_KhongHopLe_NemLoi(string manifest)
        {
            using (var dir = new TempDir())
            {
                var zipPath = dir.Combine("bad.zip");
                WriteZip(zipPath, manifest);
                Assert.Throws<InvalidDataException>(() => Packer.ReadManifest(zipPath));
            }
        }

        [Fact]
        public void ReadManifest_ZipThuong_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var zipPath = dir.Combine("plain.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                    zip.CreateEntry("readme.txt");
                Assert.Throws<InvalidDataException>(() => Packer.ReadManifest(zipPath));
            }
        }

        [Fact]
        public void Import_ThemVaoDanhSach_BoQuaToolSkip()
        {
            using (var dir = new TempDir())
            {
                var a = dir.WriteFile(@"a\a.exe", "a");
                var b = dir.WriteFile(@"b\b.exe", "b");
                var zipPath = dir.Combine("out.zip");
                Packer.Pack(new[] { Tool("a", a), Tool("b", b) }, zipPath);

                var store = ToolStore.Load(dir.Combine("tools.json"));
                store.Add(new ToolEntry { Id = "a", Name = "A cũ", Path = "old" });
                var dest = dir.Combine("imported");

                var imported = PackageImporter.Import(store, zipPath, dest, new Dictionary<string, ConflictResolution>
                {
                    ["a"] = ConflictResolution.Skip,
                    ["b"] = ConflictResolution.KeepBoth,
                });

                Assert.Equal("b", imported.Single().Id);
                Assert.Equal("old", store.Find("a").Path);
                Assert.False(Directory.Exists(Path.Combine(dest, "a"))); // tool Skip không bị giải nén
                Assert.True(File.Exists(store.Find("b").Path));
            }
        }

        private static void WriteZip(string zipPath, string manifest, params string[] extraEntries)
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry(Packer.ManifestName).Open(), new UTF8Encoding(false)))
                    w.Write(manifest);
                foreach (var name in extraEntries)
                    using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                        w.Write("x");
            }
        }
    }
}
