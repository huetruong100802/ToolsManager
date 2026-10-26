using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    public class ShortcutsTests
    {
        [Fact]
        public void Resolve_LayFileThatThamSoMoTa()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"app\Modbus Poll.exe", "exe");
                var lnk = CreateShortcut(dir.Combine("Modbus Poll.lnk"), exe, "-port 502", "Test PLC");

                var info = Shortcuts.Resolve(lnk);

                Assert.Equal(exe, info.TargetPath);
                Assert.Equal("-port 502", info.Arguments);
                Assert.Equal("Test PLC", info.Description);
                Assert.Equal("Modbus Poll", info.Name);
            }
        }

        [Fact]
        public void FromPath_Shortcut_DangKyFileThatThayViLnk()
        {
            using (var dir = new TempDir())
            {
                var exe = dir.WriteFile(@"app\tool.exe", "exe");
                var lnk = CreateShortcut(dir.Combine("Tool hay.lnk"), exe, "", "");

                var entry = ToolDefaults.FromPath(lnk);

                Assert.Equal(exe, entry.Path);
                Assert.Equal("Tool hay", entry.Name);
                Assert.Null(entry.Args);
                Assert.Null(entry.Description);
            }
        }

        [Fact]
        public void FromPath_ShortcutTroFileMat_GiuNguyenLnk()
        {
            using (var dir = new TempDir())
            {
                var lnk = CreateShortcut(dir.Combine("Mat.lnk"), dir.Combine(@"app\mat.exe"), "", "");

                Assert.Null(Shortcuts.Resolve(lnk));
                Assert.Equal(lnk, ToolDefaults.FromPath(lnk).Path);
            }
        }

        private static string CreateShortcut(string lnkPath, string target, string arguments, string description)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            var shell = Activator.CreateInstance(type);
            var link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            var linkType = link.GetType();
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
            return lnkPath;
        }
    }
}
