using System;
using System.IO;
using System.Windows;
using Toolbox.Core.Agents;

namespace Toolbox.Views
{
    /// <summary>
    /// "Nhờ agent…": đưa prompt dựng sẵn vào clipboard để dán vào agent. Chưa có agent nào kết nối thì dẫn tới Kết nối agent
    /// thay vì sao chép một prompt mà người dùng chưa có chỗ để dán.
    /// </summary>
    internal static class AgentAsk
    {
        public static bool HasAgent()
        {
            try { return AgentStore.Load(AgentStore.DefaultPath).Agents.Count > 0; }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        /// <param name="prompt">Nội dung dán cho agent.</param>
        /// <param name="connect">Mở cửa sổ Kết nối agent (khi chưa có agent nào).</param>
        /// <returns>true nếu đã sao chép prompt.</returns>
        public static bool Send(string prompt, Action connect, Func<bool> hasAgent = null)
        {
            if (!(hasAgent ?? HasAgent)())
            {
                Toasts.Show("Chưa có agent nào kết nối với Toolbox.", ToastKind.Info, "Kết nối agent", connect, 10);
                return false;
            }
            try
            {
                Clipboard.SetText(prompt);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Toasts.Show("Không ghi được vào clipboard (đang bị chương trình khác giữ). Thử lại sau giây lát.", ToastKind.Error);
                return false;
            }
            Toasts.Show("Đã sao chép prompt kèm ngữ cảnh. Dán vào cuộc trò chuyện với agent.", ToastKind.Success, null, null, 7);
            return true;
        }
    }
}
