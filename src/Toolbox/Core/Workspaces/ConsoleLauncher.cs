using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Toolbox.Core.Workspaces
{
    /// <summary>
    /// Mở cmd ở cửa sổ console riêng kèm biến môi trường. ShellExecute bỏ qua <see cref="ProcessStartInfo.Environment"/>,
    /// còn <c>UseShellExecute = false</c> không redirect thì cmd con kế thừa handle chuẩn và đọc trộm kênh MCP stdio.
    /// Ở đây gọi thẳng CreateProcessW: CREATE_NEW_CONSOLE, không kế thừa handle, khối môi trường Unicode riêng.
    /// </summary>
    internal static class ConsoleLauncher
    {
        private const uint CreateNewConsole = 0x00000010;
        private const uint CreateUnicodeEnvironment = 0x00000400;

        /// <summary>
        /// Chạy <c>cmd.exe {arguments}</c> ở cửa sổ mới. Môi trường = môi trường hiện tại của Toolbox, biến trong
        /// <paramref name="env"/> ghi đè biến cùng tên (không phân biệt hoa thường). Trả về <see cref="Process"/> theo dõi được
        /// (Exited, ExitCode, Id cho KillTree). Lỗi Win32 ném <see cref="Win32Exception"/>.
        /// </summary>
        public static Process Start(string arguments, string cwd, IDictionary<string, string> env)
        {
            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var commandLine = new StringBuilder("\"" + cmd + "\" " + arguments);
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)) };
            var block = Marshal.StringToHGlobalUni(BuildBlock(env));
            ProcessInformation pi;
            try
            {
                if (!CreateProcessW(cmd, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        CreateNewConsole | CreateUnicodeEnvironment, block, cwd, ref startup, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }

            try
            {
                // Giữ handle process tới khi lấy được Process theo pid: pid không bị dùng lại trong lúc đó.
                return Process.GetProcessById(pi.dwProcessId);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("Cửa sổ lệnh đóng ngay sau khi mở.");
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
        }

        /// <summary>
        /// Khối môi trường Unicode "TÊN=giá trị\0...\0\0", sắp theo tên không phân biệt hoa thường như Windows yêu cầu.
        /// Gồm môi trường hiện tại của Toolbox, biến trong <paramref name="env"/> ghi đè.
        /// </summary>
        internal static string BuildBlock(IDictionary<string, string> env)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = entry.Key as string;
                if (!string.IsNullOrEmpty(name))
                    merged[name] = entry.Value as string ?? "";
            }
            if (env != null)
                foreach (var pair in env)
                    if (!string.IsNullOrEmpty(pair.Key))
                        merged[pair.Key] = pair.Value ?? "";

            var sb = new StringBuilder();
            foreach (var pair in merged.OrderBy(p => p.Key.ToUpperInvariant(), StringComparer.Ordinal))
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            if (sb.Length == 0)
                sb.Append('\0');
            sb.Append('\0');   // StringToHGlobalUni thêm một ký tự null nữa: kết thúc bằng hai null là đủ
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory, ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
