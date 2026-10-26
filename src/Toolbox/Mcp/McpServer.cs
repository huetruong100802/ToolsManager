using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Toolbox.Core;

namespace Toolbox.Mcp
{
    /// <summary>
    /// MCP server tối giản qua stdio (JSON-RPC, mỗi message một dòng), cho agent tra cứu và chạy tool trong danh sách.
    /// Chỉ hỗ trợ capability "tools": list_tools, run_tool.
    /// </summary>
    public sealed class McpServer
    {
        private const string DefaultProtocolVersion = "2025-06-18";
        private const int DefaultTimeoutSeconds = 60;
        private const int MaxTimeoutSeconds = 600;

        private readonly Func<ToolStore> _loadStore;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        /// <param name="loadStore">Đọc lại tools.json mỗi lần gọi để thấy ngay thay đổi từ GUI.</param>
        public McpServer(Func<ToolStore> loadStore)
        {
            _loadStore = loadStore;
        }

        public void Run(TextReader input, TextWriter output)
        {
            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var response = Handle(line);
                if (response == null)
                    continue;
                output.Write(response);
                output.Write('\n');
                output.Flush();
            }
        }

        /// <summary>Xử lý một message; trả về null với notification.</summary>
        internal string Handle(string line)
        {
            Dictionary<string, object> request;
            try
            {
                request = _json.Deserialize<Dictionary<string, object>>(line);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                return Error(null, -32700, "Parse error");
            }
            if (request == null)
                return Error(null, -32600, "Invalid request");

            var isNotification = !request.TryGetValue("id", out var id);
            var method = request.TryGetValue("method", out var m) ? m as string : null;
            var parameters = request.TryGetValue("params", out var p) ? p as Dictionary<string, object> : null;

            object result;
            try
            {
                switch (method)
                {
                    case "initialize":
                        result = Initialize(parameters);
                        break;
                    case "ping":
                        result = new Dictionary<string, object>();
                        break;
                    case "tools/list":
                        result = new Dictionary<string, object> { ["tools"] = ToolDefinitions() };
                        break;
                    case "tools/call":
                        result = CallTool(parameters);
                        break;
                    default:
                        return isNotification ? null : Error(id, -32601, "Method not found: " + method);
                }
            }
            catch (InvalidParamsException ex)
            {
                return isNotification ? null : Error(id, -32602, ex.Message);
            }

            return isNotification
                ? null
                : _json.Serialize(new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
        }

        private static object Initialize(Dictionary<string, object> parameters)
        {
            // Chỉ dùng phần "tools" ổn định giữa các phiên bản giao thức nên nhận version client đề nghị.
            var version = parameters != null && parameters.TryGetValue("protocolVersion", out var v) && v is string s ? s : DefaultProtocolVersion;
            return new Dictionary<string, object>
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new Dictionary<string, object> { ["tools"] = new Dictionary<string, object>() },
                ["serverInfo"] = new Dictionary<string, object>
                {
                    ["name"] = "toolbox",
                    ["version"] = Assembly.GetExecutingAssembly().GetName().Version.ToString(3),
                },
                ["instructions"] = "Toolbox chứa danh sách tool người dùng đã đăng ký trên máy. "
                    + "Dùng list_tools để tìm tool trước khi tự viết script mới; dùng run_tool để chạy.",
            };
        }

        private static object[] ToolDefinitions() => new object[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "list_tools",
                ["description"] = "Liệt kê hoặc tìm tool đã đăng ký trong Toolbox: id, tên, mô tả, tag, đường dẫn, tham số mặc định, file còn tồn tại không. "
                    + "Nên gọi trước khi viết script/tool mới để dùng lại tool có sẵn.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["query"] = Property("string", "Từ khoá tìm trong id/tên/mô tả/tag (không phân biệt dấu). Bỏ trống để lấy tất cả."),
                    ["tag"] = Property("string", "Chỉ lấy tool có tag này."),
                }),
            },
            new Dictionary<string, object>
            {
                ["name"] = "run_tool",
                ["description"] = "Chạy một tool đã đăng ký theo id. "
                    + "wait=false (mặc định): mở tool như người dùng bấm, trả về PID — dùng cho app có giao diện hoặc server giả lập. "
                    + "wait=true: chạy ẩn, chờ xong rồi trả exitCode + stdout/stderr — chỉ cho tool console (.exe/.com/.bat/.cmd/.ps1/.py).",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id của tool (lấy từ list_tools)."),
                    ["args"] = Property("string", "Tham số dòng lệnh. Bỏ trống thì dùng tham số mặc định đã đăng ký."),
                    ["wait"] = Property("boolean", "true: chờ tool chạy xong và lấy output."),
                    ["timeoutSeconds"] = Property("integer", $"Chỉ khi wait=true. Mặc định {DefaultTimeoutSeconds}, tối đa {MaxTimeoutSeconds}."),
                }, "id"),
            },
        };

        private object CallTool(Dictionary<string, object> parameters)
        {
            var name = parameters != null && parameters.TryGetValue("name", out var n) ? n as string : null;
            var arguments = parameters != null && parameters.TryGetValue("arguments", out var a) && a is Dictionary<string, object> d
                ? d
                : new Dictionary<string, object>();

            ToolStore store;
            try
            {
                store = _loadStore();
            }
            catch (InvalidDataException ex)
            {
                return ToolResult(ex.Message, isError: true);
            }

            switch (name)
            {
                case "list_tools":
                    return ListTools(store, arguments);
                case "run_tool":
                    return RunTool(store, arguments);
                default:
                    throw new InvalidParamsException("Unknown tool: " + name);
            }
        }

        private object ListTools(ToolStore store, Dictionary<string, object> arguments)
        {
            var tools = store.Search(GetString(arguments, "query"), GetString(arguments, "tag"))
                .Select(t => new Dictionary<string, object>
                {
                    ["id"] = t.Id,
                    ["name"] = t.Name,
                    ["description"] = t.Description ?? "",
                    ["tags"] = t.Tags,
                    ["path"] = t.ResolvedPath,
                    ["args"] = t.Args ?? "",
                    ["exists"] = t.PathExists,
                })
                .ToList();

            return tools.Count == 0
                ? ToolResult($"Không có tool nào khớp (tổng {store.Tools.Count} tool đã đăng ký).", isError: false)
                : ToolResult(_json.Serialize(tools), isError: false);
        }

        private static object RunTool(ToolStore store, Dictionary<string, object> arguments)
        {
            var id = GetString(arguments, "id");
            if (string.IsNullOrWhiteSpace(id))
                return ToolResult("Thiếu id. Dùng list_tools để xem id.", isError: true);

            var tool = store.Find(id);
            if (tool == null)
                return ToolResult($"Không có tool \"{id}\". Dùng list_tools để xem id.", isError: true);

            var args = GetString(arguments, "args");
            var wait = arguments.TryGetValue("wait", out var w) && w is bool b && b;
            var timeout = DefaultTimeoutSeconds;
            if (arguments.TryGetValue("timeoutSeconds", out var t) && t != null)
            {
                try { timeout = Math.Max(1, Math.Min(MaxTimeoutSeconds, Convert.ToInt32(t))); }
                catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { }
            }

            try
            {
                if (!wait)
                {
                    var pid = Runner.Start(tool, args);
                    return ToolResult($"Đã mở \"{tool.Name}\"" + (pid.HasValue ? $" (PID {pid})." : "."), isError: false);
                }

                var result = Runner.RunAndCapture(tool, args, TimeSpan.FromSeconds(timeout));
                var header = result.TimedOut
                    ? $"Quá {timeout}s, đã dừng tiến trình."
                    : $"exitCode: {result.ExitCode}";
                var text = $"{header}\n\n[stdout]\n{result.Output}\n\n[stderr]\n{result.Error}";
                return ToolResult(text, isError: result.TimedOut || result.ExitCode != 0);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is NotSupportedException
                                       || ex is InvalidOperationException || ex is Win32Exception)
            {
                return ToolResult(ex.Message, isError: true);
            }
        }

        private static Dictionary<string, object> ToolResult(string text, bool isError) => new Dictionary<string, object>
        {
            ["content"] = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError,
        };

        private static Dictionary<string, object> Schema(Dictionary<string, object> properties, params string[] required)
        {
            var schema = new Dictionary<string, object> { ["type"] = "object", ["properties"] = properties };
            if (required.Length > 0)
                schema["required"] = required;
            return schema;
        }

        private static Dictionary<string, object> Property(string type, string description) =>
            new Dictionary<string, object> { ["type"] = type, ["description"] = description };

        private static string GetString(Dictionary<string, object> arguments, string key) =>
            arguments.TryGetValue(key, out var value) ? value as string : null;

        private string Error(object id, int code, string message) =>
            _json.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message },
            });

        private sealed class InvalidParamsException : Exception
        {
            public InvalidParamsException(string message) : base(message) { }
        }
    }
}
