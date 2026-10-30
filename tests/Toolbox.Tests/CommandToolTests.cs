using System;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class CommandToolTests
    {
        private static ToolStore Store(TempDir dir) => ToolStore.Load(dir.Combine("tools.json"));

        private static string WorkDir(TempDir dir)
        {
            var work = dir.Combine("ai-autopilot-dxfac");
            Directory.CreateDirectory(work);
            File.WriteAllText(System.IO.Path.Combine(work, ".env"), "SECRET=1");
            return work;
        }

        [Fact]
        public void DangKyLenh_GiuThuMucLamViec_KhongChepVaKhongDongGoi()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);

                var tool = AgentTools.Register(store, new AgentTools.Request
                {
                    Name = "AI Autopilot",
                    Path = work,
                    Command = "python -m ai_autopilot",
                }).Tool;

                Assert.True(tool.IsCommand);
                Assert.Equal(work, tool.Path);
                Assert.Equal("python -m ai_autopilot", tool.Command);
                Assert.Equal(StoreMode.Link, tool.Store);
                Assert.Equal(PackMode.None, tool.Pack);
                Assert.Null(tool.Fingerprint);
                // Thư mục làm việc có .env và DB: không được chép vào thư viện.
                Assert.False(Directory.Exists(dir.Combine("library")));
            }
        }

        [Fact]
        public void DangKyLenhVoiFile_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var file = dir.WriteFile(@"src\tool.exe", "exe");
                var store = Store(dir);

                Assert.Throws<ArgumentException>(() => AgentTools.Register(store, new AgentTools.Request
                {
                    Name = "Tool",
                    Path = file,
                    Command = "python -m tool",
                }));
            }
        }

        [Fact]
        public void DangKyLaiCungLenhCungThuMuc_CapNhatKhongTaoMoi()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);
                var first = AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" });
                store.Save();

                var second = AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" });

                Assert.False(second.Created);
                Assert.Equal(first.Tool.Id, second.Tool.Id);
                Assert.Single(store.Tools);
            }
        }

        [Fact]
        public void ThuMucThuongKhongKhopLenh()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);
                AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" });

                var plain = AgentTools.Register(store, new AgentTools.Request { Name = "Thư mục", Path = work });

                Assert.True(plain.Created);
                Assert.False(plain.Tool.IsCommand);
                Assert.Equal(2, store.Tools.Count());
            }
        }

        [Fact]
        public void SuaLenh_DoiLenhGiuThuMuc()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);
                var id = AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" }).Tool.Id;

                var updated = AgentTools.Update(store, id, new AgentTools.Request { Command = "python -m ai_autopilot doctor" }).Tool;

                Assert.Equal("python -m ai_autopilot doctor", updated.Command);
                Assert.Equal(work, updated.Path);
                Assert.Equal(PackMode.None, updated.Pack);
            }
        }

        [Fact]
        public void SuaLenhRong_BaoLoi()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);
                var id = AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" }).Tool.Id;

                Assert.Throws<ArgumentException>(() => AgentTools.Update(store, id, new AgentTools.Request { Command = "  " }));
            }
        }

        [Fact]
        public void LuuVaDocLai_GiuLenh()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var store = Store(dir);
                var id = AgentTools.Register(store, new AgentTools.Request { Name = "AI", Path = work, Command = "python -m ai_autopilot" }).Tool.Id;
                store.Save();

                var loaded = Store(dir).Find(id);

                Assert.Equal("python -m ai_autopilot", loaded.Command);
                Assert.True(loaded.IsCommand);
            }
        }

        [Fact]
        public void Runner_LenhChayTaiThuMucDaDangKy()
        {
            using (var dir = new TempDir())
            {
                var work = WorkDir(dir);
                var tool = new ToolEntry { Name = "AI", Path = work, Command = "python -m ai_autopilot", Pack = PackMode.None };

                var open = Runner.CreateStartInfo(tool);
                Assert.Equal("cmd.exe", open.FileName);
                Assert.Equal("/k \"python -m ai_autopilot\"", open.Arguments);
                Assert.Equal(System.IO.Path.GetFullPath(work), open.WorkingDirectory);
                Assert.True(open.UseShellExecute);

                var capture = Runner.CreateCaptureStartInfo(tool, "doctor");
                Assert.Equal("cmd.exe", capture.FileName);
                Assert.Equal("/c \"python -m ai_autopilot doctor\"", capture.Arguments);
                Assert.Equal(System.IO.Path.GetFullPath(work), capture.WorkingDirectory);
                Assert.True(capture.RedirectStandardOutput);
                Assert.False(capture.UseShellExecute);
            }
        }
    }
}
