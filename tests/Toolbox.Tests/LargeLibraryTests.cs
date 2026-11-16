using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Toolbox.Core;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>
    /// Thư viện lớn và độ bền của app: dòng tool không đọc đĩa trên luồng giao diện, lọc và sắp xếp trong bộ nhớ, Trang chủ gom tool lỗi,
    /// list_tools phân trang, tools.json có BOM vẫn đọc được, một cửa sổ cho mỗi thư mục dữ liệu, cài và gỡ sạch.
    /// </summary>
    public class LargeLibraryTests
    {
        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // ---- Dòng tool ----

        [Fact]
        public void ToolItem_ChuaKiemFile_CoiNhuConVaKhongCanhBao()
        {
            var item = new ToolItem(new ToolEntry { Id = "a", Name = "A", Path = @"Z:\khong\co\a.exe" });

            Assert.False(item.IsProbed);
            Assert.True(item.Exists);
            Assert.Equal(ToolStatus.Ready, item.Status);
            Assert.Null(item.Warning);
        }

        [Fact]
        public void ToolItem_NhanKetQuaKiem_DoiTrangThaiVaBaoThayDoiMotLan()
        {
            var item = new ToolItem(new ToolEntry { Id = "a", Name = "A", Path = @"Z:\khong\co\a.exe" });

            Assert.True(item.ApplyProbe(exists: false, canInstall: false));
            Assert.Equal(ToolStatus.Missing, item.Status);
            Assert.True(item.HasProblem);
            Assert.Contains(@"Z:\khong\co\a.exe", item.Warning);
            // Kiểm lại ra cùng kết quả thì không có gì để vẽ lại.
            Assert.False(item.ApplyProbe(exists: false, canInstall: false));

            Assert.True(item.ApplyProbe(exists: false, canInstall: true));
            Assert.Equal(ToolStatus.NotInstalled, item.Status);
            Assert.True(item.ApplyProbe(exists: true, canInstall: false));
            Assert.Equal(ToolStatus.Ready, item.Status);
        }

        [Fact]
        public void Probe_ToolLenhKhongCoThuMuc_KhongBiCoiLaMatFile()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile("a.exe", "x");
                var setup = dir.WriteFile("setup.msi", "x");

                Assert.Equal((true, false), ToolItem.Probe(new ToolEntry { Name = "git", Command = "git" }));
                Assert.Equal((true, false), ToolItem.Probe(new ToolEntry { Name = "A", Path = file }));
                Assert.Equal((false, false), ToolItem.Probe(new ToolEntry { Name = "A", Path = dir.Combine("mat.exe") }));
                Assert.Equal((false, true), ToolItem.Probe(new ToolEntry { Name = "A", Path = dir.Combine("mat.exe"), Pack = PackMode.Setup, Setup = setup }));
                Assert.Equal((false, false), ToolItem.Probe(new ToolEntry { Name = "py", Command = "python -m x", Path = dir.Combine("mat") }));
            }
        }

        [Fact]
        public void ToolItem_Matches_KhongDauVaLocTag()
        {
            var item = new ToolItem(new ToolEntry { Id = "modbus-sim", Name = "Giả lập Modbus", Description = "Server thử PLC", Tags = new List<string> { "Thiết bị", "plc" } });
            var none = new HashSet<string>();

            Assert.True(item.Matches(new[] { "gia", "lap" }, none));
            Assert.True(item.Matches(new[] { "plc", "server" }, none));
            Assert.False(item.Matches(new[] { "gia", "khongco" }, none));
            Assert.True(item.Matches(new string[0], new HashSet<string> { Text.Fold("Thiết bị") }));
            Assert.False(item.Matches(new string[0], new HashSet<string> { "khac" }));
        }

        [Fact]
        public void ToolItem_Rebind_GiuTickVaKetQuaKiem_CapNhatNoiDungTim()
        {
            var item = new ToolItem(new ToolEntry { Id = "a", Name = "Cũ", Path = @"C:\x\a.exe" }) { IsChecked = true };
            item.ApplyProbe(false, false);
            var names = new List<string>();
            item.PropertyChanged += (s, e) => names.Add(e.PropertyName);

            item.Rebind(new ToolEntry { Id = "a", Name = "Tên mới", Path = @"C:\x\a.exe", Pinned = true });

            Assert.True(item.IsChecked);
            Assert.False(item.Exists);
            Assert.Equal("Tên mới", item.Tool.Name);
            Assert.True(item.Matches(new[] { "ten", "moi" }, new HashSet<string>()));
            Assert.Contains(null, names); // báo đổi mọi thuộc tính để dòng vẽ lại
            // Cùng một bản ghi thì không báo gì: ghim hay sửa một tool không làm các dòng khác vẽ lại.
            names.Clear();
            item.Rebind(item.Tool);
            Assert.Empty(names);
        }

        [Fact]
        public void BulkCollection_ThayNoiDungBangMotLanReset_GiongHetThiImLang()
        {
            var list = new BulkCollection<string>();
            var events = new List<NotifyCollectionChangedAction>();
            list.CollectionChanged += (s, e) => events.Add(e.Action);

            list.ResetTo(Enumerable.Range(0, 1000).Select(i => "t" + i).ToList());
            Assert.Equal(1000, list.Count);
            Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);

            list.ResetTo(Enumerable.Range(0, 1000).Select(i => "t" + i).ToList());
            Assert.Single(events); // không đổi: giữ vị trí cuộn và dòng đang chọn

            list.ResetTo(new List<string> { "a" });
            Assert.Equal(2, events.Count);
            Assert.Equal("a", list.Single());
        }

        [Fact]
        public void SortOption_GiaTriLaVeTheoTen()
        {
            Assert.Equal(LibrarySort.Name, SortOption.Parse(null));
            Assert.Equal(LibrarySort.Name, SortOption.Parse("linh-tinh"));
            Assert.Equal(LibrarySort.Recent, SortOption.Parse("recent"));
            Assert.Equal(LibrarySort.Pinned, SortOption.Parse(SortOption.Name(LibrarySort.Pinned)));
            Assert.Null(SortOption.Name(LibrarySort.Name));
        }

        [Fact]
        public void IconCache_NapONen_MoiDuongDanChiNapMotLan()
        {
            var loads = 0;
            var old = IconCache.Loader;
            IconCache.Loader = p => { Interlocked.Increment(ref loads); return null; };
            try
            {
                var path = @"C:\icon-test\" + Guid.NewGuid().ToString("N") + ".exe";
                Assert.False(IconCache.TryGet(path, out _));
                using (var done = new CountdownEvent(3))
                {
                    for (var i = 0; i < 3; i++)
                        IconCache.Request(path, icon => done.Signal());
                    Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
                }
                Assert.Equal(1, loads);
                Assert.True(IconCache.TryGet(path, out var cached));
                Assert.Null(cached);

                IconCache.Invalidate(path);
                Assert.False(IconCache.TryGet(path, out _));
            }
            finally
            {
                IconCache.Loader = old;
            }
        }

        // ---- Trang chủ ----

        private static HomeFeed Feed(IReadOnlyList<ToolEntry> tools, Func<string, bool> exists) =>
            HomeFeed.Build(new List<Core.Workspaces.Workspace>(), tools, new List<Core.Workspaces.TaskInfo>(), 0, false, null, null,
                new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), exists);

        [Fact]
        public void TrangChu_NhieuToolMatDuongDan_GomThanhMotDong()
        {
            var tools = Enumerable.Range(0, 40).Select(i => new ToolEntry { Id = "t" + i, Name = "Tool " + i, Path = @"C:\mat\t" + i + ".exe" }).ToList();

            var item = Feed(tools, p => false).Attention.Single();

            Assert.Equal("40 tool mất đường dẫn", item.Title);
            Assert.Contains("Tool 0", item.Detail);
            Assert.Contains("37 tool khác", item.Detail);
            Assert.Equal(HomeTarget.Library, item.Target);
            Assert.Null(item.ToolId); // null = mở Thư viện đã lọc sẵn các tool lỗi
        }

        [Fact]
        public void TrangChu_ItToolMatDuongDan_VanLaTungDong()
        {
            var tools = Enumerable.Range(0, HomeFeed.MaxMissingShown).Select(i => new ToolEntry { Id = "t" + i, Name = "Tool " + i, Path = @"C:\mat\t" + i + ".exe" }).ToList();

            var attention = Feed(tools, p => false).Attention;

            Assert.Equal(HomeFeed.MaxMissingShown, attention.Count);
            Assert.Equal(new[] { "t0", "t1", "t2" }, attention.Select(a => a.ToolId));
        }

        // ---- Kho ----

        [Fact]
        public void ToolsJsonCoBom_VanDocDuoc()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var json = "{\"tools\":[{\"id\":\"a\",\"name\":\"Công cụ A\",\"path\":\"C:\\\\x\\\\a.exe\",\"tags\":[]}]}";
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray());

                var store = ToolStore.Load(path);

                Assert.Equal("Công cụ A", store.Tools.Single().Name);
                // Lần lưu sau ghi lại không BOM và vẫn đọc được.
                store.Add(new ToolEntry { Name = "B", Path = @"C:\x\b.exe" });
                store.Save();
                Assert.Equal(2, ToolStore.Load(path).Tools.Count);
            }
        }

        [Fact]
        public void Sync_FileKhongDoiThiKhongCoGiDeNap_FileDoiThiNap()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("tools.json");
                var gui = ToolStore.Load(path);
                gui.Add(new ToolEntry { Name = "A", Path = @"C:\x\a.exe" });
                gui.Save();

                Assert.False(gui.Sync());

                var agent = ToolStore.Load(path);
                agent.Add(new ToolEntry { Name = "B", Path = @"C:\x\b.exe" });
                agent.Save();

                Assert.True(gui.Sync());
                Assert.Equal(2, gui.Tools.Count);
                Assert.False(gui.Sync());
            }
        }

        // ---- MCP ----

        private static McpServer ServerWith(TempDir dir, int count)
        {
            var path = dir.Combine("tools.json");
            var store = ToolStore.Load(path);
            for (var i = 0; i < count; i++)
                store.Add(new ToolEntry { Name = "Tool " + i.ToString("000"), Path = @"C:\x\t" + i + ".exe", Tags = new List<string> { i % 2 == 0 ? "chan" : "le" } });
            store.Save();
            return new McpServer(() => ToolStore.Load(path));
        }

        private static string ListTools(McpServer server, string arguments)
        {
            var request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tools\",\"arguments\":" + arguments + "}}";
            var response = Js.Deserialize<Dictionary<string, object>>(server.Handle(request));
            var result = (Dictionary<string, object>)response["result"];
            return (string)((Dictionary<string, object>)((ArrayList)result["content"])[0])["text"];
        }

        [Fact]
        public void ListTools_ThuVienNho_VanTraMangNhuTruoc()
        {
            using (var dir = new TempDir())
            {
                var tools = Js.Deserialize<List<Dictionary<string, object>>>(ListTools(ServerWith(dir, 5), "{}"));
                Assert.Equal(5, tools.Count);
            }
        }

        [Fact]
        public void ListTools_ThuVienLon_PhanTrangKemTongVaChoDocTiep()
        {
            using (var dir = new TempDir())
            {
                var server = ServerWith(dir, McpServer.DefaultListLimit + 30);

                var first = Js.Deserialize<Dictionary<string, object>>(ListTools(server, "{}"));
                Assert.Equal(McpServer.DefaultListLimit + 30, first["total"]);
                Assert.Equal(McpServer.DefaultListLimit, ((ArrayList)first["tools"]).Count);
                Assert.Equal(McpServer.DefaultListLimit, first["nextOffset"]);
                Assert.True(first.ContainsKey("hint"));

                var rest = Js.Deserialize<Dictionary<string, object>>(ListTools(server, "{\"offset\":" + McpServer.DefaultListLimit + "}"));
                Assert.Equal(30, ((ArrayList)rest["tools"]).Count);
                Assert.False(rest.ContainsKey("nextOffset"));

                var small = Js.Deserialize<Dictionary<string, object>>(ListTools(server, "{\"limit\":7,\"tag\":\"le\"}"));
                Assert.Equal(7, ((ArrayList)small["tools"]).Count);
                Assert.Equal((McpServer.DefaultListLimit + 30) / 2, small["total"]);

                // Thu hẹp bằng từ khoá đến khi vừa một trang thì lại là mảng.
                var one = Js.Deserialize<List<Dictionary<string, object>>>(ListTools(server, "{\"query\":\"tool 007\"}"));
                Assert.Equal("tool-007", one.Single()["id"]);
            }
        }

        // ---- Một cửa sổ, nhật ký lỗi, cài và gỡ ----

        [Fact]
        public void SingleInstance_BanThuHaiKhongLayDuocCho_VaGuiDuocFileChoBanDau()
        {
            var key = "Toolbox.Test." + Guid.NewGuid().ToString("N");
            using (var first = SingleInstance.TryAcquire(key))
            {
                Assert.NotNull(first);
                string received = null;
                using (var got = new ManualResetEventSlim())
                {
                    first.Listen(message => { received = message; got.Set(); });

                    // Mutex là đệ quy trong cùng luồng, nên "bản thứ hai" phải ở luồng khác như một tiến trình khác.
                    SingleInstance second = first;
                    var thread = new Thread(() => second = SingleInstance.TryAcquire(key));
                    thread.Start();
                    thread.Join();
                    Assert.Null(second);

                    Assert.True(SingleInstance.Send(key, @"C:\gói\bộ đồ nghề.toolbox", 5000));
                    Assert.True(got.Wait(TimeSpan.FromSeconds(10)));
                    Assert.Equal(@"C:\gói\bộ đồ nghề.toolbox", received);
                }
            }
        }

        [Fact]
        public void SingleInstance_KhoaTheoThuMucDuLieu()
        {
            Assert.Equal(SingleInstance.KeyFor(@"C:\Data\Toolbox"), SingleInstance.KeyFor(@"c:\data\toolbox\"));
            Assert.NotEqual(SingleInstance.KeyFor(@"C:\Data\Toolbox"), SingleInstance.KeyFor(@"C:\Data\Toolbox2"));
            Assert.False(SingleInstance.Send("Toolbox.Test.KhongAi." + Guid.NewGuid().ToString("N"), "x", 200));
        }

        [Fact]
        public void CrashLog_GhiLoiKemThoiGianVaKhongBaoGioNem()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("sub", "crash.log");
                CrashLog.Write(new InvalidOperationException("hỏng rồi"), "giao diện", path);
                CrashLog.Write(null, null, path);

                var text = File.ReadAllText(path);
                Assert.Contains("hỏng rồi", text);
                Assert.Contains("[giao diện]", text);
                Assert.Contains("InvalidOperationException", text);

                CrashLog.Write(new Exception("x"), null, "?:\\khong hop le\\crash.log"); // không ném
            }
        }

        [Fact]
        public void Installer_GhiMucGoCaiDat_VaGoSach()
        {
            using (var dir = new TempDir())
            {
                var testKey = @"Software\ToolboxTests\" + Guid.NewGuid().ToString("N");
                var source = dir.WriteFile(@"download\Toolbox.exe", "exe");
                var installer = new Installer(dir.Combine("install"), dir.Combine("startmenu"), testKey + @"\Classes");
                try
                {
                    var installed = installer.Install(source);

                    Assert.StartsWith(testKey, installer.UninstallKey); // test không bao giờ ghi vào mục Uninstall thật
                    using (var key = Registry.CurrentUser.OpenSubKey(installer.UninstallKey))
                    {
                        Assert.Equal("Toolbox", key.GetValue("DisplayName"));
                        Assert.Equal($"\"{installed}\" --uninstall", key.GetValue("UninstallString"));
                        Assert.Equal(Installer.Version, key.GetValue("DisplayVersion"));
                    }

                    installer.Uninstall();

                    Assert.False(File.Exists(installer.ShortcutPath));
                    Assert.Null(Registry.CurrentUser.OpenSubKey(installer.UninstallKey));
                    Assert.Null(Registry.CurrentUser.OpenSubKey(testKey + @"\Classes\.toolbox"));
                    Assert.Null(Registry.CurrentUser.OpenSubKey(testKey + @"\Classes\" + Installer.ProgId));
                    Assert.True(File.Exists(installed)); // exe do bước xoá sau khi thoát lo, Uninstall không đụng
                    installer.Uninstall(); // gỡ lần hai không lỗi
                }
                finally
                {
                    Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false);
                    using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\ToolboxTests"))
                        if (parent != null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
                            Registry.CurrentUser.DeleteSubKey(@"Software\ToolboxTests", throwOnMissingSubKey: false);
                }
            }
        }

        [Fact]
        public void Installer_KhongGoLienKetDuoiFileDaThuocAppKhac()
        {
            using (var dir = new TempDir())
            {
                var testKey = @"Software\ToolboxTests\" + Guid.NewGuid().ToString("N");
                var installer = new Installer(dir.Combine("install"), dir.Combine("startmenu"), testKey + @"\Classes");
                try
                {
                    installer.Install(dir.WriteFile(@"download\Toolbox.exe", "exe"));
                    using (var ext = Registry.CurrentUser.CreateSubKey(testKey + @"\Classes\.toolbox"))
                        ext.SetValue("", "AppKhac.Goi");

                    installer.Uninstall();

                    using (var ext = Registry.CurrentUser.OpenSubKey(testKey + @"\Classes\.toolbox"))
                        Assert.Equal("AppKhac.Goi", ext.GetValue(""));
                }
                finally
                {
                    Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false);
                    using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\ToolboxTests"))
                        if (parent != null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
                            Registry.CurrentUser.DeleteSubKey(@"Software\ToolboxTests", throwOnMissingSubKey: false);
                }
            }
        }
    }
}
