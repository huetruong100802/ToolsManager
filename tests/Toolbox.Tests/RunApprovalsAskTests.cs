using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Hỏi trước khi chạy: luôn cho lệnh này, từ chối kèm lý do, quyết định từ thông báo, và hiện ở Trang chủ.</summary>
    public class RunApprovalsAskTests
    {
        private static readonly Dictionary<string, object> Args = new Dictionary<string, object> { ["profile"] = "dev" };

        private sealed class Paths
        {
            public string Proposals, Agents, Activity;
        }

        private static Paths Setup(TempDir dir, out AgentRecord agent)
        {
            var paths = new Paths { Proposals = dir.Combine("proposals.json"), Agents = dir.Combine("agents.json"), Activity = dir.Combine("activity.jsonl") };
            var store = AgentStore.Load(paths.Agents);
            var added = store.Add(new AgentRecord { Name = "Claude Code", WorkspaceId = "w1", RunMode = RunModes.Ask });
            store.Save();
            agent = added;
            return paths;
        }

        private static AgentRecord Fresh(Paths paths, string id) => AgentStore.Load(paths.Agents).Find(id);

        [Fact]
        public void LuonChoLenhNay_LanSauChayNgay_KhongTaoDeXuat_VaLenhKhacVanHoi()
        {
            using (var dir = new TempDir())
            {
                var paths = Setup(dir, out var agent);
                Assert.NotNull(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));
                var proposal = ProposalStore.Load(paths.Proposals).Pending.Single();

                RunApprovals.Decide(paths.Proposals, paths.Agents, paths.Activity, proposal, approve: true, always: true);

                Assert.Null(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));
                Assert.Null(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args)); // không bị tiêu thụ
                Assert.Empty(ProposalStore.Load(paths.Proposals).Pending);

                var other = RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", new Dictionary<string, object> { ["profile"] = "prod" });
                Assert.NotNull(other);
                Assert.Single(ProposalStore.Load(paths.Proposals).Pending);
            }
        }

        [Fact]
        public void QuenLenhDaCho_AgentPhaiXinLai()
        {
            using (var dir = new TempDir())
            {
                var paths = Setup(dir, out var agent);
                RunApprovals.AllowAlways(paths.Agents, agent.Id, RunApprovals.Print("run_profile", Args));
                Assert.Null(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));

                Assert.Equal(1, RunApprovals.ForgetAllowed(paths.Agents, agent.Id));
                Assert.Equal(0, RunApprovals.ForgetAllowed(paths.Agents, agent.Id));
                Assert.NotNull(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));
            }
        }

        [Fact]
        public void TuChoi_AgentNhanLyDoMotLan_RoiLanSauLaXinMoi()
        {
            using (var dir = new TempDir())
            {
                var paths = Setup(dir, out var agent);
                RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args);
                var proposal = ProposalStore.Load(paths.Proposals).Pending.Single();
                RunApprovals.Decide(paths.Proposals, paths.Agents, paths.Activity, proposal, approve: false);

                var reply = RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args);
                Assert.Equal(RunApprovals.RejectedMessage, reply);
                Assert.Empty(ProposalStore.Load(paths.Proposals).Pending); // chưa hỏi dồn người dùng

                Assert.NotNull(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));
                Assert.Single(ProposalStore.Load(paths.Proposals).Pending);
            }
        }

        [Fact]
        public void Decide_GhiNhatKy_VaChiDuyetMotLanThiKhongNho()
        {
            using (var dir = new TempDir())
            {
                var paths = Setup(dir, out var agent);
                RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args);
                var proposal = ProposalStore.Load(paths.Proposals).Pending.Single();

                RunApprovals.Decide(paths.Proposals, paths.Agents, paths.Activity, proposal, approve: true, always: false);

                Assert.Equal("approve", ActivityLog.Read(paths.Activity).Single().Action);
                Assert.Null(Fresh(paths, agent.Id).AllowRuns);
                Assert.Null(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args)); // duyệt một lần
                Assert.NotNull(RunApprovals.Check(paths.Proposals, Fresh(paths, agent.Id), "run_profile", Args));
            }
        }

        [Fact]
        public void PendingCount_ChiDemDeXuatChayDangCho()
        {
            var proposals = new[]
            {
                new Proposal { Kind = ProposalKinds.Run, State = ProposalStates.Pending },
                new Proposal { Kind = ProposalKinds.Run, State = ProposalStates.Approved },
                new Proposal { Kind = ProposalKinds.UpdateTool, State = ProposalStates.Pending },
            };
            Assert.Equal(1, RunApprovals.PendingCount(proposals));
        }

        [Fact]
        public void TrangChu_YeuCauChayHienRiengVaKhongDemHaiLan()
        {
            using (var dir = new TempDir())
            {
                var ws = new List<Workspace> { new Workspace { Id = "w1", Name = "W", Path = dir.Path } };
                var feed = HomeFeed.Build(ws, new List<ToolEntry>(), new List<TaskInfo>(), pendingProposals: 3, hasAgent: true, entries: null, usage: null,
                    now: DateTime.UtcNow, pathExists: p => true, pendingRunRequests: 2);
                Assert.Contains(feed.Attention, i => i.Title.Contains("xin chạy 2 lệnh"));
                Assert.Contains(feed.Attention, i => i.Title.StartsWith("1 đề xuất")); // 3 - 2 yêu cầu chạy
            }
        }

        [Fact]
        public void Toast_NutPhu_ChayDungHanhDongVaDong()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var host = new Toolbox.Views.ToastHost();
                var log = new List<string>();
                var toast = host.Show("Claude xin chạy", Toolbox.Views.ToastKind.Info, "Cho chạy", () => log.Add("cho"), 30,
                    new[] { new Toolbox.Views.ToastAction("Luôn cho", () => log.Add("luon")), new Toolbox.Views.ToastAction("Từ chối", () => log.Add("tuchoi")) });
                Assert.Equal(2, toast.Extra.Count);
                Assert.Equal(1, host.Count);
                toast.Extra[1].Action();
                Assert.Equal(new[] { "tuchoi" }, log);
            });
        }
    }
}
