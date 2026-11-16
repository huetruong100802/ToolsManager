using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Toolbox.Core;

namespace Toolbox
{
    /// <summary>
    /// Cài cho user hiện tại, không cần quyền admin: copy exe vào %LOCALAPPDATA%\Programs\Toolbox,
    /// tạo shortcut Start Menu, gắn đuôi .toolbox để double-click là mở màn nhập gói, và ghi một mục trong
    /// "Installed apps" của Windows để gỡ được như mọi app khác.
    /// </summary>
    public sealed class Installer
    {
        public const string ProgId = "Toolbox.Package";
        private const string DefaultClassesKey = @"Software\Classes";

        private readonly string _classesKey;
        private readonly string _uninstallKey;

        /// <param name="classesKey">Khoá registry (dưới HKCU) cho đuôi file; test truyền khoá riêng, khi đó mục gỡ cài đặt cũng nằm dưới khoá test.</param>
        public Installer(string installDir = null, string shortcutDir = null, string classesKey = DefaultClassesKey)
        {
            InstallDir = installDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Toolbox");
            ShortcutDir = shortcutDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            _classesKey = classesKey;
            _uninstallKey = classesKey == DefaultClassesKey
                ? @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Toolbox"
                : classesKey + @"\Uninstall\Toolbox";
        }

        public string InstallDir { get; }
        public string ShortcutDir { get; }
        public string InstalledExe => Path.Combine(InstallDir, "Toolbox.exe");
        public string ShortcutPath => Path.Combine(ShortcutDir, "Toolbox.lnk");

        /// <summary>Khoá (dưới HKCU) của mục gỡ cài đặt.</summary>
        public string UninstallKey => _uninstallKey;

        /// <summary>Phiên bản đang chạy, dạng "1.2.3".</summary>
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

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
            RegisterUninstall(InstalledExe);
            return InstalledExe;
        }

        /// <summary>
        /// Gỡ những gì <see cref="Install"/> đã tạo: shortcut, liên kết đuôi .toolbox, mục "Installed apps". Không xoá dữ liệu
        /// (%APPDATA%\Toolbox) và không xoá exe: exe đang chạy thì không tự xoá được, xem <see cref="ScheduleRemoveFiles"/>.
        /// </summary>
        public void Uninstall()
        {
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);

            using (var extension = Registry.CurrentUser.OpenSubKey(_classesKey + @"\" + Packer.FileExtension, writable: true))
            {
                // Chỉ gỡ liên kết nếu nó vẫn trỏ về Toolbox; người dùng có thể đã gán đuôi này cho app khác.
                if (extension != null && string.Equals(extension.GetValue("") as string, ProgId, StringComparison.OrdinalIgnoreCase))
                    Registry.CurrentUser.DeleteSubKeyTree(_classesKey + @"\" + Packer.FileExtension, throwOnMissingSubKey: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(_classesKey + @"\" + ProgId, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(_uninstallKey, throwOnMissingSubKey: false);
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Xoá thư mục cài sau khi tiến trình này thoát: một cmd ẩn chờ vài giây rồi xoá. Chỉ xoá khi thư mục đúng là thư mục cài
        /// (có Toolbox.exe), để một đường dẫn sai không bao giờ xoá nhầm chỗ khác.
        /// </summary>
        public void ScheduleRemoveFiles()
        {
            if (!File.Exists(InstalledExe))
                return;
            var command = $"/c ping 127.0.0.1 -n 4 >nul & del /f /q \"{InstalledExe}\" & rmdir \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
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

        /// <summary>Mục trong Settings › Apps › Installed apps (cài theo user nên nằm ở HKCU, không cần quyền admin).</summary>
        private void RegisterUninstall(string exe)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(_uninstallKey))
            {
                key.SetValue("DisplayName", "Toolbox");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Toolbox");
                key.SetValue("DisplayIcon", $"\"{exe}\",0");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
                catch (IOException)
                {
                }
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
