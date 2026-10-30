using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class ToolStoreTests
    {
        [Theory]
        [InlineData("EasyModbus Server Simulator", "easymodbus-server-simulator")]
        [InlineData("Công cụ Đo  lường!", "cong-cu-do-luong")]
        [InlineData("***", "tool")]
        public void ToId_SinhKebabCaseKhongDau(string name, string expected) =>
            Assert.Equal(expected, Text.ToId(name));

        [Fact]
        public void Load_FileChuaCo_TraVeDanhSachRong()
        {
            using (var dir = new TempDir())
                Assert.Empty(ToolStore.Load(dir.Combine("tools.json")).Tools);
        }

        [Fact]
        public void SaveRoiLoad_GiuNguyenDuLieu_KeCaTiengViet()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("sub", "tools.json");
                var store = ToolStore.Load(path);
                store.Add(new ToolEntry
                {
                    Name = "Bộ giả lập Modbus",
                    Path = @"C:\tools\sim.exe",
                    Args = "--port 502",
                    Description = "Giả lập PLC",
                    Tags = new List<string> { "modbus", "plc" },
                    Pack = PackMode.File,
                });
                store.Save();
                store.Save(); // lần 2 đi nhánh File.Replace

                var loaded = ToolStore.Load(path).Tools.Single();
                Assert.Equal("bo-gia-lap-modbus", loaded.Id);
                Assert.Equal("Bộ giả lập Modbus", loaded.Name);
                Assert.Equal("--port 502", loaded.Args);
                Assert.Equal(new[] { "modbus", "plc" }, loaded.Tags);
                Assert.Equal(PackMode.File, loaded.Pack);
                Assert.False(File.Exists(path + ".tmp"));
            }
        }

        [Fact]
        public void Load_ThieuTagsVaPack_DungMacDinh()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"C:\\\\a.exe\"}]}");
                var tool = ToolStore.Load(path).Tools.Single();
                Assert.Empty(tool.Tags);
                Assert.Equal(PackMode.Folder, tool.Pack);
            }
        }

        [Fact]
        public void Load_FileHong_NemLoiVaKhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{ hỏng");
                Assert.Throws<InvalidDataException>(() => ToolStore.Load(path));
                Assert.Equal("{ hỏng", File.ReadAllText(path));
            }
        }

        [Fact]
        public void Load_PackSai_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"x\",\"pack\":\"nonee\"}]}");
                Assert.Throws<InvalidDataException>(() => ToolStore.Load(path));
            }
        }

        [Fact]
        public void Add_TrungTen_ThemHauTo()
        {
            var store = ToolStore.Load(Path.Combine(Path.GetTempPath(), "khong-ton-tai.json"));
            store.Add(new ToolEntry { Name = "Sim", Path = "a" });
            Assert.Equal("sim-2", store.Add(new ToolEntry { Name = "Sim", Path = "b" }).Id);
        }

        [Fact]
        public void Search_KhongPhanBietDau_VaLocTag()
        {
            var store = ToolStore.Load(Path.Combine(Path.GetTempPath(), "khong-ton-tai.json"));
            store.Add(new ToolEntry { Name = "Công cụ đo", Path = "a", Tags = new List<string> { "PLC" } });
            store.Add(new ToolEntry { Name = "VSPE", Path = "b", Description = "cổng COM ảo", Tags = new List<string> { "serial" } });

            Assert.Equal("VSPE", store.Search("cong com").Single().Name);
            Assert.Equal("Công cụ đo", store.Search("", "plc").Single().Name);
            Assert.Empty(store.Search("vspe", "plc"));
            Assert.Equal(new[] { "PLC", "serial" }, store.AllTags());
        }

        [Theory]
        [InlineData(ConflictResolution.Replace, 1, "sim")]
        [InlineData(ConflictResolution.KeepBoth, 2, "sim-2")]
        [InlineData(ConflictResolution.Skip, 1, null)]
        public void Import_TrungId_XuLyTheoLuaChon(ConflictResolution resolution, int expectedCount, string expectedId)
        {
            var store = ToolStore.Load(Path.Combine(Path.GetTempPath(), "khong-ton-tai.json"));
            store.Add(new ToolEntry { Name = "Sim", Path = "old" });

            var added = store.Import(new ToolEntry { Id = "sim", Name = "Sim", Path = "new" }, resolution);

            Assert.Equal(expectedCount, store.Tools.Count);
            Assert.Equal(expectedId, added?.Id);
            Assert.Equal(resolution == ConflictResolution.Replace ? "new" : "old", store.Find("sim").Path);
        }

        [Fact]
        public void SaveCoThayDoi_SaoLuBanCuVaGhiNhatKy()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{\"tools\":[]}");
                var store = ToolStore.Load(path);
                store.Actor = "mcp";
                store.Add(new ToolEntry { Name = "Sim", Path = "a" });
                store.Save();

                var backup = Directory.GetFiles(dir.Combine("backup"), "tools-*.json").Single();
                Assert.Equal("{\"tools\":[]}", File.ReadAllText(backup));

                var line = Assert.Single(File.ReadAllLines(dir.Combine("changes.log")));
                var fields = line.Split('\t');
                Assert.Equal("mcp", fields[1]);
                Assert.Equal("add", fields[2]);
                Assert.Equal("sim", fields[3]);
                Assert.Equal("Sim", fields[4]);
            }
        }

        [Fact]
        public void MarkUsed_KhongSaoLuVaKhongGhiNhatKy()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"C:\\\\a.exe\"}]}");
                var store = ToolStore.Load(path);
                store.MarkUsed("a");
                store.MarkUsed("a");
                store.MarkUsed("khong-co");
                store.Save();

                var tool = ToolStore.Load(path).Find("a");
                Assert.Equal(2, tool.RunCount);
                Assert.NotNull(tool.LastUsedAt);
                Assert.False(Directory.Exists(dir.Combine("backup")));
                Assert.False(File.Exists(dir.Combine("changes.log")));
            }
        }

        [Fact]
        public void SaveRoiLoad_GiuCacTruongMoi()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var lastUsed = new DateTime(2026, 10, 28, 2, 30, 0, DateTimeKind.Utc);
                var store = ToolStore.Load(path);
                store.Add(new ToolEntry
                {
                    Name = "Sim",
                    Path = "a",
                    Store = StoreMode.Copy,
                    Fingerprint = "sim.exe|10|ab",
                    Notes = "Dùng cho test",
                    RunCount = 3,
                    LastUsedAt = lastUsed,
                });
                store.Save();

                var loaded = ToolStore.Load(path).Tools.Single();
                Assert.Equal(StoreMode.Copy, loaded.Store);
                Assert.Equal("sim.exe|10|ab", loaded.Fingerprint);
                Assert.Equal("Dùng cho test", loaded.Notes);
                Assert.Equal(3, loaded.RunCount);
                Assert.Equal(lastUsed, loaded.LastUsedAt);
            }
        }

        [Fact]
        public void Load_ThieuStore_LaLink_StoreSai_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("tools.json", "{\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"x\"}]}");
                Assert.Equal(StoreMode.Link, ToolStore.Load(path).Tools.Single().Store);

                var bad = dir.WriteFile("bad.json", "{\"tools\":[{\"id\":\"a\",\"name\":\"A\",\"path\":\"x\",\"store\":\"mirror\"}]}");
                Assert.Throws<InvalidDataException>(() => ToolStore.Load(bad));
            }
        }
    }
}
