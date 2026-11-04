using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Mcp;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Agent không được báo "sẵn sàng" nhầm vì cổng đang do tiến trình khác nghe.</summary>
    public class WorkspacePortTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        [Fact]
        public void RunCustom_CongDaCoTienTrinhKhacNghe_TuChoiKhongChay()
        {
            using (var dir = new TempDir())
            {
                var storePath = dir.Combine("workspaces.json");
                var store = WorkspaceStore.Load(storePath);
                var workspace = store.Add(new Workspace { Name = "Repo", Path = dir.Path });
                workspace.Customs.Add(new CustomCommand { Id = "web", Label = "Web", CommandLine = "ping -n 5 127.0.0.1 > nul", Cwd = "." });
                store.Save();
                var server = new McpServer(() => ToolStore.Load(dir.Combine("tools.json")), workspace.Id, () => WorkspaceStore.Load(storePath), dir.Combine("runs"));

                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                    var response = Call(server, "run_custom", "{\"id\":\"web\",\"port\":" + port + ",\"waitReady\":true}");

                    Assert.True(IsError(response));
                    Assert.Contains("đã có tiến trình khác", Text(response));
                }
                finally
                {
                    listener.Stop();
                    server.Close();
                }
            }
        }

        private static Dictionary<string, object> Call(McpServer server, string name, string argumentsJson) =>
            Json.Deserialize<Dictionary<string, object>>(server.Handle(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}}"));

        private static string Text(Dictionary<string, object> response) =>
            (string)((Dictionary<string, object>)((ArrayList)((Dictionary<string, object>)response["result"])["content"])[0])["text"];

        private static bool IsError(Dictionary<string, object> response) =>
            (bool)((Dictionary<string, object>)response["result"])["isError"];
    }
}
