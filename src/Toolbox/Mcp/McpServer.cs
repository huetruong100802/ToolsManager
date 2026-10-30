using System;
using System.Collections;
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
    /// MCP server tối giản qua stdio (JSON-RPC, mỗi message một dòng), cho agent tra cứu, chạy, đăng ký và sửa tool trong danh sách.
    /// Chỉ hỗ trợ capability "tools": list_tools, run_tool, register_tool, update_tool, unregister_tool.
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
                    + "wait=true: chạy ẩn, chờ xong rồi trả exitCode + stdout/stderr — chỉ cho tool console (.exe/.com/.bat/.cmd/.ps1/.py) và lệnh.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id của tool (lấy từ list_tools)."),
                    ["args"] = Property("string", "Tham số dòng lệnh. Bỏ trống thì dùng tham số mặc định đã đăng ký."),
                    ["wait"] = Property("boolean", "true: chờ tool chạy xong và lấy output."),
                    ["timeoutSeconds"] = Property("integer", $"Chỉ khi wait=true. Mặc định {DefaultTimeoutSeconds}, tối đa {MaxTimeoutSeconds}."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "register_tool",
                ["description"] = "Đăng ký một tool trên máy để dùng lại sau. Trùng path hoặc cùng nội dung file với tool đã có thì cập nhật tool đó. "
                    + "File/thư mục nhỏ (≤50MB) được chép vào thư viện Toolbox để không mất khi file gốc bị di chuyển; lớn hơn thì giữ đường dẫn gốc. "
                    + "App đã cài trong Program Files không được chép: truyền setupPath là file bộ cài (.exe/.msi) để Toolbox chép bộ cài, người khác có thể cài từ đó. "
                    + "Chạy một lệnh (vd: python -m ai_autopilot) thay vì file: truyền command, khi đó path là thư mục làm việc và không chép file. "
                    + "Đọc warnings trong kết quả.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["name"] = Property("string", "Tên hiển thị."),
                    ["path"] = Property("string", "Đường dẫn tuyệt đối tới file chạy hoặc thư mục. Cho phép %ENV%. Với command thì là thư mục làm việc."),
                    ["id"] = Property("string", "Tuỳ chọn. Id kebab-case; bỏ trống thì sinh từ name. Trùng id của tool khác thì báo lỗi."),
                    ["args"] = Property("string", "Tham số dòng lệnh mặc định."),
                    ["command"] = Property("string", "Lệnh chạy tại thư mục path, vd: python -m ai_autopilot. Tool thành lệnh: không chép, không đóng gói, không dùng setupPath."),
                    ["description"] = Property("string", "Mô tả ngắn để tìm kiếm và để agent khác hiểu."),
                    ["tags"] = ArrayProperty("Tag để lọc, vd: [\"modbus\", \"plc\"]."),
                    ["notes"] = Property("string", "Ghi chú: cách dùng, lưu ý, lý do đăng ký."),
                    ["setupPath"] = Property("string", "Đường dẫn tuyệt đối tới bộ cài .exe/.msi của app, nếu app cần cài trên máy khác."),
                }, "name", "path"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "update_tool",
                ["description"] = "Sửa thông tin của một tool đã đăng ký. Chỉ các trường được truyền mới đổi. "
                    + "Đổi path không chép lại file và đưa tool về store=link; muốn chép lại thì unregister_tool rồi register_tool.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id của tool (lấy từ list_tools)."),
                    ["name"] = Property("string", "Tên hiển thị mới."),
                    ["path"] = Property("string", "Đường dẫn tuyệt đối mới. Cho phép %ENV%."),
                    ["args"] = Property("string", "Tham số dòng lệnh mặc định mới."),
                    ["command"] = Property("string", "Lệnh mới chạy tại thư mục path (đổi tool thành lệnh). Xem register_tool."),
                    ["description"] = Property("string", "Mô tả mới."),
                    ["tags"] = ArrayProperty("Danh sách tag mới (thay toàn bộ)."),
                    ["notes"] = Property("string", "Ghi chú mới."),
                }, "id"),
            },
            new Dictionary<string, object>
            {
                ["name"] = "unregister_tool",
                ["description"] = "Bỏ một tool khỏi danh sách. Không xoá file gốc và không xoá bản chép trong thư viện, nên có thể khôi phục từ sao lưu.",
                ["inputSchema"] = Schema(new Dictionary<string, object>
                {
                    ["id"] = Property("string", "Id của tool (lấy từ list_tools)."),
                }, "id"),
            },
        };

        private object CallTool(Dictionary<string, object> parameters)
        {
            var name = parameters != null && parameters.TryGetValue("name", out var n) ? n as string : null;
            var arguments = parameters != null && parameters.TryGetValue("arguments", out var a) && a is Dictionary<string, object> d
                ? d
                : new Dictionary<string, object>();

            try
            {
                switch (name)
                {
                    case "list_tools":
                        return ListTools(_loadStore(), arguments);
                    case "run_tool":
                        return RunTool(_loadStore(), arguments);
                    case "register_tool":
                    case "update_tool":
                    case "unregister_tool":
                        return WriteTool(name, arguments);
                    default:
                        throw new InvalidParamsException("Unknown tool: " + name);
                }
            }
            // Lỗi đầu vào hoặc lỗi file là kết quả tool (isError), không phải lỗi JSON-RPC để agent thử lại theo cách khác.
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                return ToolResult(ex.Message, isError: true);
            }
        }

        /// <summary>
        /// register_tool, update_tool, unregister_tool. Đọc-sửa-ghi trong một khoá để không ghi đè thay đổi của GUI.
        /// Lỗi đầu vào ném trước khi Save, nên danh sách trên đĩa không đổi.
        /// </summary>
        private object WriteTool(string name, Dictionary<string, object> arguments)
        {
            using (ToolStore.Lock())
            {
                var store = _loadStore();
                store.Actor = "mcp";

                switch (name)
                {
                    case "register_tool":
                        var registered = AgentTools.Register(store, ToRequest(arguments));
                        store.Save();
                        return ToolResult(Describe(registered), isError: false);
                    case "update_tool":
                        var updated = AgentTools.Update(store, GetString(arguments, "id"), ToRequest(arguments));
                        store.Save();
                        return ToolResult(Describe(updated), isError: false);
                    default:
                        var id = GetString(arguments, "id");
                        if (string.IsNullOrWhiteSpace(id))
                            return ToolResult("Thiếu id. Dùng list_tools để xem id.", isError: true);
                        if (!store.Remove(id))
                            return ToolResult($"Không có tool \"{id}\". Dùng list_tools để xem id.", isError: true);
                        store.Save();
                        return ToolResult($"Đã bỏ \"{id}\" khỏi danh sách. File gốc và bản chép trong thư viện vẫn còn.", isError: false);
                }
            }
        }

        private static AgentTools.Request ToRequest(Dictionary<string, object> arguments) => new AgentTools.Request
        {
            Name = GetString(arguments, "name"),
            Path = GetString(arguments, "path"),
            Id = GetString(arguments, "id"),
            Args = GetString(arguments, "args"),
            Command = GetString(arguments, "command"),
            Description = GetString(arguments, "description"),
            Tags = GetStrings(arguments, "tags"),
            Notes = GetString(arguments, "notes"),
            SetupPath = GetString(arguments, "setupPath"),
        };

        private string Describe(AgentTools.Outcome outcome)
        {
            var tool = outcome.Tool;
            var view = new Dictionary<string, object>
            {
                ["created"] = outcome.Created,
                ["id"] = tool.Id,
                ["name"] = tool.Name,
                ["path"] = tool.ResolvedPath,
                ["store"] = tool.Store.ToString().ToLowerInvariant(),
                ["pack"] = tool.Pack.ToString().ToLowerInvariant(),
                ["exists"] = tool.PathExists,
                ["warnings"] = outcome.Warnings.ToArray(),
            };
            if (tool.Fingerprint != null)
                view["fingerprint"] = tool.Fingerprint;
            if (tool.IsCommand)
                view["command"] = tool.Command;
            return _json.Serialize(view);
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
                    ["command"] = t.Command ?? "",
                    ["exists"] = t.PathExists,
                })
                .ToList();

            return tools.Count == 0
                ? ToolResult($"Không có tool nào khớp (tổng {store.Tools.Count} tool đã đăng ký).", isError: false)
                : ToolResult(_json.Serialize(tools), isError: false);
        }

        private object RunTool(ToolStore store, Dictionary<string, object> arguments)
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
                    RecordUse(tool.Id);
                    return ToolResult($"Đã mở \"{tool.Name}\"" + (pid.HasValue ? $" (PID {pid})." : "."), isError: false);
                }

                var result = Runner.RunAndCapture(tool, args, TimeSpan.FromSeconds(timeout));
                RecordUse(tool.Id);
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

        private static Dictionary<string, object> ArrayProperty(string description) => new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object> { ["type"] = "string" },
            ["description"] = description,
        };

        private static string GetString(Dictionary<string, object> arguments, string key) =>
            arguments.TryGetValue(key, out var value) ? value as string : null;

        private static List<string> GetStrings(Dictionary<string, object> arguments, string key) =>
            arguments.TryGetValue(key, out var value) && value is IEnumerable items && !(value is string)
                ? items.Cast<object>().OfType<string>().ToList()
                : null;

        /// <summary>Ghi nhận lần chạy qua MCP. Best-effort: lỗi ghi file không làm mất việc đã chạy.</summary>
        private void RecordUse(string id)
        {
            try
            {
                using (ToolStore.Lock())
                {
                    var store = _loadStore();
                    store.MarkUsed(id);
                    store.Save();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
            }
        }

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
