using System;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace Toolbox.Tests
{
    public class InstallerTests
    {
        [Fact]
        public void Install_CopyExe_TaoShortcut_GanDuoiFile()
        {
            using (var dir = new TempDir())
            {
                // Ghi registry vào key test riêng trong HKCU rồi xoá, không đụng Software\Classes thật.
                var testKey = @"Software\ToolboxTests\" + Guid.NewGuid().ToString("N");
                var source = dir.WriteFile(@"download\Toolbox.exe", "exe");
                var installer = new Installer(dir.Combine("install"), dir.Combine("startmenu"), testKey + @"\Classes");
                try
                {
                    var installed = installer.Install(source);

                    Assert.Equal("exe", File.ReadAllText(installed));
                    Assert.True(installer.IsInstalledCopy(installed));
                    Assert.False(installer.IsInstalledCopy(source));
                    Assert.True(File.Exists(installer.ShortcutPath));
                    using (var ext = Registry.CurrentUser.OpenSubKey(testKey + @"\Classes\.toolbox"))
                        Assert.Equal(Installer.ProgId, ext.GetValue(""));
                    using (var cmd = Registry.CurrentUser.OpenSubKey(testKey + @"\Classes\" + Installer.ProgId + @"\shell\open\command"))
                        Assert.Equal($"\"{installed}\" \"%1\"", cmd.GetValue(""));

                    installer.Install(installed); // cài lại từ chính bản đã cài: không copy đè lên chính nó
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
