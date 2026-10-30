namespace Toolbox.Core
{
    /// <summary>
    /// desk-mcp (github.com/DigitalDog1/desk-mcp, Apache-2.0): MCP server cho agent đọc và thao tác giao diện Windows
    /// (chụp màn hình, OCR, chuột, bàn phím). Toolbox chỉ khai báo nó vào Claude Code; npx tải gói khi server khởi động.
    /// </summary>
    public static class DeskMcp
    {
        public const string ServerName = "desk-mcp";

        /// <summary>Trên Windows npx là npx.cmd nên phải chạy qua cmd /c.</summary>
        public const string CommandArgs = "cmd /c npx -y desk-mcp";

        /// <summary>desk-mcp cần Node.js 20+; kiểm tra npx có trên PATH.</summary>
        public static bool NpxAvailable() => ClaudeCode.FindOnPath("npx.cmd", "npx.exe") != null;
    }
}
