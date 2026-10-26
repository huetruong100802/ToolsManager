using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    public class McpServerTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static Dictionary<string, object> Call(McpServer server, string request) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(request));

        private static Dictionary<string, object> Result(Dictionary<string, object> response) =>
            (Dictionary<string, object>)response["result"];

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)Result(response)["content"])[0])["text"];

        private static McpServer ServerWith(TempDir dir, params ToolEntry[] tools)
        {
            var path = dir.Combine("tools.json");
            var store = ToolStore.Load(path);
            foreach (var tool in tools)
                store.Add(tool);
            store.Save();
            return new McpServer(() => ToolStore.Load(path));
        }

        [Fact]
        public void Initialize_TraVeVersionClientVaCapabilityTools()
        {
            using (var dir = new TempDir())
            {
                var response = Call(ServerWith(dir), "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}");

                Assert.Equal(1, response["id"]);
                Assert.Equal("2025-03-26", Result(response)["protocolVersion"]);
                Assert.True(((Dictionary<string, object>)Result(response)["capabilities"]).ContainsKey("tools"));
            }
        }

        [Fact]
        public void Notification_KhongTraLoi()
        {
            using (var dir = new TempDir())
                Assert.Null(ServerWith(dir).Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        }

        [Fact]
        public void MethodLa_TraLoiLoi32601()
        {
            using (var dir = new TempDir())
            {
                var response = Call(ServerWith(dir), "{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"method\":\"resources/list\"}");
                Assert.Equal(-32601, ((Dictionary<string, object>)response["error"])["code"]);
                Assert.Equal("a", response["id"]);
            }
        }

        [Fact]
        public void JsonHong_TraLoiParseError()
        {
            using (var dir = new TempDir())
                Assert.Equal(-32700, ((Dictionary<string, object>)Call(ServerWith(dir), "{hỏng")["error"])["code"]);
        }

        [Fact]
        public void ToolsList_CoListVaRun()
        {
            using (var dir = new TempDir())
            {
                var tools = (ArrayList)Result(Call(ServerWith(dir), "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))["tools"];
                var names = tools.Cast<Dictionary<string, object>>().Select(t => (string)t["name"]).ToArray();
                Assert.Equal(new[] { "list_tools", "run_tool" }, names);
            }
        }

        [Fact]
        public void ListTools_TimTheoTuKhoa()
        {
            using (var dir = new TempDir())
            {
                var server = ServerWith(dir,
                    new ToolEntry { Name = "Modbus Sim", Path = @"C:\x\sim.exe", Description = "Giả lập PLC", Tags = new List<string> { "modbus" } },
                    new ToolEntry { Name = "VSPE", Path = @"C:\x\vspe.exe" });

                var text = Text(Call(server, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tools\",\"arguments\":{\"query\":\"gia lap\"}}}"));
                var found = Json.Deserialize<List<Dictionary<string, object>>>(text);

                Assert.Equal("modbus-sim", found.Single()["id"]);
                Assert.Equal(false, found.Single()["exists"]);
            }
        }

        [Fact]
        public void RunTool_Wait_TraOutputVaExitCode()
        {
            using (var dir = new TempDir())
            {
                var bat = dir.WriteFile(@"t\echo.bat", "@echo xin chao %1\r\n@exit /b 3\r\n");
                var server = ServerWith(dir, new ToolEntry { Name = "Echo", Path = bat });

                var response = Call(server, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"run_tool\",\"arguments\":{\"id\":\"echo\",\"args\":\"ban\",\"wait\":true}}}");

                Assert.Contains("exitCode: 3", Text(response));
                Assert.Contains("xin chao ban", Text(response));
                Assert.Equal(true, Result(response)["isError"]);
            }
        }

        [Fact]
        public void RunTool_IdKhongCo_LaLoiCuaTool()
        {
            using (var dir = new TempDir())
            {
                var response = Call(ServerWith(dir), "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"run_tool\",\"arguments\":{\"id\":\"khong-co\"}}}");
                Assert.Equal(true, Result(response)["isError"]);
                Assert.Contains("list_tools", Text(response));
            }
        }

        [Fact]
        public void TenToolMcpLa_LaLoiGiaoThuc()
        {
            using (var dir = new TempDir())
            {
                var response = Call(ServerWith(dir), "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"xoa_may\"}}");
                Assert.Equal(-32602, ((Dictionary<string, object>)response["error"])["code"]);
            }
        }

        [Fact]
        public void Run_DocTungDongVaGhiTungDong()
        {
            using (var dir = new TempDir())
            {
                var input = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n\n{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}\n");
                var output = new StringWriter();

                ServerWith(dir).Run(input, output);

                var lines = output.ToString().Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(2, lines.Length);
                Assert.All(lines, l => Assert.Contains("\"result\":{}", l));
            }
        }
    }
}
