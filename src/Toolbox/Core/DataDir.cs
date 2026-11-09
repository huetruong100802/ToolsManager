using System;
using System.IO;

namespace Toolbox.Core
{
    /// <summary>
    /// Thư mục dữ liệu của Toolbox (tools.json, workspaces.json, agents.json, runs...). Mặc định %APPDATA%\Toolbox;
    /// đặt biến môi trường TOOLBOX_DATA để dùng thư mục khác (chạy thử, chụp ảnh, bản di động) mà không đụng dữ liệu thật.
    /// </summary>
    public static class DataDir
    {
        public const string EnvironmentVariable = "TOOLBOX_DATA";

        public static string Root
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable(EnvironmentVariable);
                return string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toolbox")
                    : custom;
            }
        }
    }
}
