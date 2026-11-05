using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Agents;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Chế độ "Hỏi trước mỗi lần chạy": đề xuất, duyệt một lần, rồi xin lại.</summary>
    public class RunApprovalsTests
    {
        private static AgentRecord Agent() => new AgentRecord { Id = "a1", Name = "Claude Code", WorkspaceId = "w1" };

        [Fact]
        public void ChuaDuyet_TaoDeXuat_KhongTaoTrung_DuyetRoiChayDungMotLan()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("proposals.json");
                var args = new Dictionary<string, object> { ["profile"] = "dev" };

                var first = RunApprovals.Check(path, Agent(), "run_profile", args);
                var again = RunApprovals.Check(path, Agent(), "run_profile", args);

                Assert.NotNull(first);
                Assert.NotNull(again);
                var pending = ProposalStore.Load(path).Pending.ToList();
                Assert.Single(pending);
                Assert.Equal(ProposalKinds.Run, pending[0].Kind);

                var id = pending[0].Id;
                ProposalStore.Update(path, s => { s.Decide(id, approved: true); return true; });

                Assert.Null(RunApprovals.Check(path, Agent(), "run_profile", args));
                Assert.Equal(ProposalStates.Used, ProposalStore.Load(path).Proposals.Single(p => p.Id == id).State);
                Assert.NotNull(RunApprovals.Check(path, Agent(), "run_profile", args));
            }
        }

        [Fact]
        public void ThamSoKhacNhau_KhongDungChungDuyet()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("proposals.json");
                RunApprovals.Check(path, Agent(), "run_profile", new Dictionary<string, object> { ["profile"] = "dev" });
                var id = ProposalStore.Load(path).Pending.Single().Id;
                ProposalStore.Update(path, s => { s.Decide(id, approved: true); return true; });

                var other = RunApprovals.Check(path, Agent(), "run_profile", new Dictionary<string, object> { ["profile"] = "prod" });

                Assert.NotNull(other);
                Assert.Equal(2, ProposalStore.Load(path).Proposals.Count);
            }
        }

        [Fact]
        public void Print_BoThuTuThamSo()
        {
            var a = new Dictionary<string, object> { ["x"] = "1", ["y"] = "2" };
            var b = new Dictionary<string, object> { ["y"] = "2", ["x"] = "1" };

            Assert.Equal(RunApprovals.Print("run_stack", a), RunApprovals.Print("run_stack", b));
        }
    }
}
