using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Toolbox.Core
{
    public sealed class ShortcutInfo
    {
        public string Name { get; set; }
        public string ShortcutPath { get; set; }
        public string TargetPath { get; set; }
        public string Arguments { get; set; }
        public string Description { get; set; }
    }

    /// <summary>Đọc shortcut (.lnk): thêm app đã cài từ Start Menu, hoặc kéo thả shortcut thì lấy file thật.</summary>
    public static class Shortcuts
    {
        /// <summary>null nếu không đọc được hoặc file đích không tồn tại.</summary>
        public static ShortcutInfo Resolve(string lnkPath)
        {
            using (var shell = new Shell())
                return shell.Read(lnkPath);
        }

        /// <summary>App có shortcut trong Start Menu (của user và của máy), bỏ trình gỡ cài đặt và bản trùng.</summary>
        public static IList<ShortcutInfo> ListStartMenu()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            };

            var result = new List<ShortcutInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var shell = new Shell())
            {
                foreach (var lnk in roots.Where(r => r.Length > 0).SelectMany(r => EnumerateLinks(r)))
                {
                    var info = shell.Read(lnk);
                    if (info == null || !IsApp(info) || !seen.Add(info.TargetPath + "|" + info.Arguments))
                        continue;
                    result.Add(info);
                }
            }
            return result.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static bool IsApp(ShortcutInfo info)
        {
            if (!string.Equals(Path.GetExtension(info.TargetPath), ".exe", StringComparison.OrdinalIgnoreCase))
                return false;
            var name = info.Name.ToLowerInvariant();
            var file = Path.GetFileName(info.TargetPath).ToLowerInvariant();
            return !name.Contains("uninstall") && !name.Contains("gỡ cài") && !file.StartsWith("unins", StringComparison.Ordinal);
        }

        // Duyệt tay để một thư mục bị chặn quyền không làm hỏng cả danh sách.
        private static IEnumerable<string> EnumerateLinks(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                string[] files, subdirs;
                try
                {
                    files = Directory.GetFiles(dir, "*.lnk");
                    subdirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var file in files)
                    yield return file;
                foreach (var sub in subdirs)
                    pending.Push(sub);
            }
        }

        /// <summary>WScript.Shell gọi qua late binding để không cần tham chiếu COM interop.</summary>
        private sealed class Shell : IDisposable
        {
            private readonly Type _type = Type.GetTypeFromProgID("WScript.Shell");
            private readonly object _shell;

            public Shell()
            {
                _shell = Activator.CreateInstance(_type);
            }

            public ShortcutInfo Read(string lnkPath)
            {
                object link = null;
                try
                {
                    link = _type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, _shell, new object[] { lnkPath });
                    var info = new ShortcutInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(lnkPath),
                        ShortcutPath = lnkPath,
                        TargetPath = Get(link, "TargetPath"),
                        Arguments = Get(link, "Arguments"),
                        Description = Get(link, "Description"),
                    };

                    // Shortcut kiểu MSI ("advertised", vd Office) trỏ vào icon trong C:\Windows\Installer; hỏi MSI file thật.
                    if (!File.Exists(info.TargetPath) || IsUnderWindowsInstaller(info.TargetPath))
                        info.TargetPath = MsiTarget(lnkPath) ?? info.TargetPath;

                    return File.Exists(info.TargetPath) || Directory.Exists(info.TargetPath) ? info : null;
                }
                catch (Exception ex) when (ex is COMException || ex is TargetInvocationException || ex is ArgumentException)
                {
                    return null;
                }
                finally
                {
                    if (link != null)
                        Marshal.ReleaseComObject(link);
                }
            }

            public void Dispose() => Marshal.ReleaseComObject(_shell);

            private static string Get(object link, string property) =>
                (link.GetType().InvokeMember(property, BindingFlags.GetProperty, null, link, null) as string ?? "").Trim();
        }

        private static bool IsUnderWindowsInstaller(string path)
        {
            var installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer") + "\\";
            return path.StartsWith(installer, StringComparison.OrdinalIgnoreCase);
        }

        private const int InstallStateLocal = 3;

        private static string MsiTarget(string lnkPath)
        {
            var product = new StringBuilder(39);
            var feature = new StringBuilder(39);
            var component = new StringBuilder(39);
            try
            {
                if (MsiGetShortcutTarget(lnkPath, product, feature, component) != 0)
                    return null;
                uint length = 1024;
                var path = new StringBuilder((int)length);
                return MsiGetComponentPath(product.ToString(), component.ToString(), path, ref length) == InstallStateLocal
                    ? path.ToString()
                    : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return null;
            }
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetShortcutTarget(string shortcutPath, StringBuilder productCode, StringBuilder featureId, StringBuilder componentCode);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiGetComponentPath(string product, string component, StringBuilder path, ref uint length);
    }
}
