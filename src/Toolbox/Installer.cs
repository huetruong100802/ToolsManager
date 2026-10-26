using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>
    /// Cài cho user hiện tại, không cần quyền admin: copy exe vào %LOCALAPPDATA%\Programs\Toolbox,
    /// tạo shortcut Start Menu, gắn đuôi .toolbox để double-click là mở màn nhập gói.
    /// </summary>
    public sealed class Installer
    {
        public const string ProgId = "Toolbox.Package";

        private readonly string _classesKey;

        public Installer(string installDir = null, string shortcutDir = null, string classesKey = @"Software\Classes")
        {
            InstallDir = installDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Toolbox");
            ShortcutDir = shortcutDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            _classesKey = classesKey;
        }

        public string InstallDir { get; }
        public string ShortcutDir { get; }
        public string InstalledExe => Path.Combine(InstallDir, "Toolbox.exe");
        public string ShortcutPath => Path.Combine(ShortcutDir, "Toolbox.lnk");

        public bool IsInstalledCopy(string exePath) =>
            string.Equals(Path.GetFullPath(exePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>Trả về đường dẫn exe đã cài. Ném IOException nếu bản đã cài đang chạy (file bị khoá).</summary>
        public string Install(string currentExe)
        {
            Directory.CreateDirectory(InstallDir);
            if (!IsInstalledCopy(currentExe))
                File.Copy(currentExe, InstalledExe, overwrite: true);
            CreateShortcut(InstalledExe);
            RegisterFileType(InstalledExe);
            return InstalledExe;
        }

        private void CreateShortcut(string target)
        {
            Directory.CreateDirectory(ShortcutDir);
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic link = shell.CreateShortcut(ShortcutPath);
                link.TargetPath = target;
                link.WorkingDirectory = Path.GetDirectoryName(target);
                link.Description = "Quản lý, đóng gói và chạy tool";
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void RegisterFileType(string exe)
        {
            using (var extension = Registry.CurrentUser.CreateSubKey(_classesKey + @"\" + Packer.FileExtension))
                extension.SetValue("", ProgId);

            using (var progId = Registry.CurrentUser.CreateSubKey(_classesKey + @"\" + ProgId))
            {
                progId.SetValue("", "Gói Toolbox");
                using (var icon = progId.CreateSubKey("DefaultIcon"))
                    icon.SetValue("", $"\"{exe}\",0");
                using (var command = progId.CreateSubKey(@"shell\open\command"))
                    command.SetValue("", $"\"{exe}\" \"%1\"");
            }

            // Báo Explorer cập nhật liên kết đuôi file ngay.
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
