using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Agents;
using Xunit;

namespace Toolbox.Tests
{
    public class AgentPresenceTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        private static string Ago(double seconds) => Now.AddSeconds(-seconds).ToString("o");

        private static AgentRecord Agent(string id, string workspace, double seenSecondsAgo) =>
            new AgentRecord { Id = id, Name = id, WorkspaceId = workspace, LastSeenAt = Ago(seenSecondsAgo) };

        private static ActivityEntry Entry(string action, string workspace, double secondsAgo, string result = "applied", string target = "Dev API", string undoId = null) =>
            new ActivityEntry
            {
                Time = Ago(secondsAgo), AgentId = "a1", AgentName = "Claude", WorkspaceId = workspace, Action = action, Target = target, Result = result,
                Undo = undoId == null ? null : new ActivityUndoData { Type = "profile", Id = undoId },
            };

        [Fact]
        public void IsConnected_TrongHaiPhut()
        {
            Assert.True(AgentPresence.IsConnected(Agent("a", "w1", 30), Now));
            Assert.False(AgentPresence.IsConnected(Agent("a", "w1", 300), Now));
            Assert.False(AgentPresence.IsConnected(new AgentRecord { LastSeenAt = "khong-phai-ngay" }, Now));
            Assert.False(AgentPresence.IsConnected(null, Now));
        }

        [Fact]
        public void ConnectedFor_AgentCuaWorkspaceVaAgentToanMay()
        {
            var agents = new[] { Agent("rieng", "w1", 10), Agent("khac", "w2", 10), Agent("toanmay", null, 10), Agent("cu", "w1", 999) };
            Assert.Equal(new[] { "rieng", "toanmay" }, AgentPresence.ConnectedFor(agents, "w1", Now).Select(a => a.Id));
            Assert.Equal(new[] { "khac", "toanmay" }, AgentPresence.ConnectedFor(agents, "w2", Now).Select(a => a.Id));
        }

        [Fact]
        public void ConnectedWorkspaces_AgentToanMayLamMoiWorkspaceCoAgent()
        {
            var all = new[] { "w1", "w2", "w3" };
            Assert.Equal(new[] { "w1" }, AgentPresence.ConnectedWorkspaces(new[] { Agent("a", "w1", 5) }, all, Now).ToArray());
            Assert.Equal(3, AgentPresence.ConnectedWorkspaces(new[] { Agent("g", null, 5) }, all, Now).Count);
            Assert.Empty(AgentPresence.ConnectedWorkspaces(new[] { Agent("a", "w1", 500) }, all, Now));
        }

        [Fact]
        public void Fresh_ChiThayDoiDaApDungMoiHonMoc_TheoWorkspace_CuTruoc()
        {
            var log = new[]
            {
                Entry("create_profile", "w1", 100, target: "Cũ"),
                Entry("create_profile", "w1", 20, target: "Mới 1"),
                Entry("update_stack", "w1", 10, target: "Mới 2"),
                Entry("create_profile", "w2", 15, target: "Workspace khác"),
                Entry("approve", "w1", 5),                 // việc của chính bạn
                Entry("undo", "w1", 4),                    // hoàn tác
                Entry("run_profile", "w1", 3),             // chạy, không đổi dữ liệu
                Entry("update_tool", "w1", 2, result: "pending"), // mới chỉ là đề xuất
            };
            var mark = Now.AddSeconds(-60);
            var fresh = AgentPresence.Fresh(log, mark, "w1");
            Assert.Equal(new[] { "Mới 1", "Mới 2" }, fresh.Select(e => e.Target));
            Assert.Equal(3, AgentPresence.Fresh(log, mark).Count); // mọi workspace
        }

        [Fact]
        public void Watermark_LaDongMoiNhat_VaNhatKyRongLaMinValue()
        {
            var log = new[] { Entry("create_profile", "w1", 100), Entry("create_profile", "w1", 7) };
            Assert.Equal(Now.AddSeconds(-7), AgentPresence.Watermark(log));
            Assert.Equal(DateTime.MinValue, AgentPresence.Watermark(new ActivityEntry[0]));
            // Mốc ngay sau dòng mới nhất: lúc mở app không báo lại việc cũ.
            Assert.Empty(AgentPresence.Fresh(log, AgentPresence.Watermark(log), "w1"));
        }

        [Fact]
        public void Describe_NoiAiLamGiVoiCaiNao()
        {
            Assert.Equal("Claude vừa tạo profile \"Dev API\"", AgentPresence.Describe(Entry("create_profile", "w1", 1)));
            var anonymous = Entry("delete_stack", "w1", 1, target: null);
            anonymous.AgentName = null;
            Assert.Equal("Agent vừa xoá workflow", AgentPresence.Describe(anonymous));
        }

        [Fact]
        public void TouchedIds_GomIdChinhVaIdPhu()
        {
            var entry = Entry("create_profile", "w1", 1, undoId: "p1");
            entry.Undo.Also = new List<ActivityUndoData> { new ActivityUndoData { Type = "binding", Id = "git" } };
            Assert.Equal(new[] { "p1", "git" }, AgentPresence.TouchedIds(entry).ToArray());
            Assert.Empty(AgentPresence.TouchedIds(Entry("create_profile", "w1", 1)));
        }
    }
}

namespace Toolbox.Tests
{
    /// <summary>Phần giao diện của agent hiện diện: chấm ở rail và dấu "mới" trên mục agent vừa đổi.</summary>
    public class AgentPresenceUiTests
    {
        [Fact]
        public void WorkspaceRow_ChamAgentVaSoViecMoi_BaoDoiGiaTri()
        {
            var row = new Toolbox.WorkspaceRow(new Toolbox.Core.Workspaces.Workspace { Id = "w", Name = "W", Path = @"C:\w" });
            var raised = new List<string>();
            row.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            Assert.Equal(System.Windows.Visibility.Collapsed, row.AgentVisibility);

            row.SetAgentConnected(true);
            row.SetAgentConnected(true); // không đổi: không báo lần hai
            row.SetNewActivity(2);
            Assert.Equal(System.Windows.Visibility.Visible, row.AgentVisibility);
            Assert.Equal(System.Windows.Visibility.Visible, row.NewActivityVisibility);
            Assert.Equal(2, raised.Count(n => n == nameof(Toolbox.WorkspaceRow.AgentConnected) || n == nameof(Toolbox.WorkspaceRow.NewActivity)));

            row.SetNewActivity(0);
            Assert.Equal(System.Windows.Visibility.Collapsed, row.NewActivityVisibility);
        }

        [Fact]
        public void MarkNew_DanhDauProfileVaWorkflow_ClearNewBoDi()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                using (var dir = new TempDir())
                {
                    var ws = new Toolbox.Core.Workspaces.Workspace { Id = "w", Name = "W", Path = dir.Path };
                    ws.Profiles.Add(new Toolbox.Core.Workspaces.Profile { Id = "p1", Label = "Của agent", Kind = "x", ActionId = "a" });
                    ws.Profiles.Add(new Toolbox.Core.Workspaces.Profile { Id = "p2", Label = "Của bạn", Kind = "x", ActionId = "a" });
                    ws.Stacks.Add(new Toolbox.Core.Workspaces.StackDef { Id = "s1", Label = "Workflow agent" });
                    var view = new Toolbox.Views.WorkspaceView();
                    view.Initialize(new Toolbox.Core.Workspaces.TaskHost(dir.Combine("runs")), () => true);
                    view.Load(ws);

                    view.MarkNew(new[] { "p1", "s1" });
                    var profiles = ((System.Collections.IEnumerable)((System.Windows.Controls.ItemsControl)view.FindName("RunProfileList")).ItemsSource)
                        .Cast<Toolbox.Views.ProfileRow>().ToList();
                    Assert.True(profiles.Single(p => p.Profile.Id == "p1").IsNew);
                    Assert.False(profiles.Single(p => p.Profile.Id == "p2").IsNew);
                    var stacks = ((System.Collections.IEnumerable)((System.Windows.Controls.ItemsControl)view.FindName("RunStackList")).ItemsSource)
                        .Cast<Toolbox.Views.StackRow>().ToList();
                    Assert.True(stacks.Single().IsNew);

                    view.ClearNew();
                    view.Load(ws);
                    Assert.DoesNotContain(((System.Collections.IEnumerable)((System.Windows.Controls.ItemsControl)view.FindName("RunProfileList")).ItemsSource)
                        .Cast<Toolbox.Views.ProfileRow>(), p => p.IsNew);
                }
            });
        }
    }
}
