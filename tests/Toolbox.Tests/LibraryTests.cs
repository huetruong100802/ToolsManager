using System.IO;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class LibraryTests
    {
        [Fact]
        public void DeleteCopy_XoaBanChepVaGiuFileGoc()
        {
            using (var dir = new TempDir())
            {
                var source = dir.WriteFile(@"src\tool.exe", "exe");
                var store = ToolStore.Load(dir.Combine("tools.json"));
                var tool = AgentTools.Register(store, new AgentTools.Request { Name = "Tool Demo", Path = source }).Tool;
                Assert.True(Library.HasCopy(store, tool.Id));

                store.Remove(tool.Id);
                Library.DeleteCopy(store, tool.Id);

                Assert.False(Library.HasCopy(store, tool.Id));
                Assert.False(Directory.Exists(dir.Combine("library", "tool-demo")));
                Assert.True(File.Exists(source));
            }
        }

        [Fact]
        public void DeleteCopy_ToolKhongCoBanChep_KhongLoi()
        {
            using (var dir = new TempDir())
            {
                var store = ToolStore.Load(dir.Combine("tools.json"));

                Library.DeleteCopy(store, "khong-co");

                Assert.False(Library.HasCopy(store, "khong-co"));
            }
        }

        [Fact]
        public void DeleteCopy_IdKhongHopLe_KhongChamVaoThuMucNgoai()
        {
            using (var dir = new TempDir())
            {
                var source = dir.WriteFile(@"src\tool.exe", "exe");
                var store = ToolStore.Load(dir.Combine("tools.json"));

                Library.DeleteCopy(store, "..");
                Library.DeleteCopy(store, @"..\src");

                Assert.True(File.Exists(source));
            }
        }
    }
}
