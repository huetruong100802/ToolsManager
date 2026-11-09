using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core.Agents;
using Xunit;

namespace Toolbox.Tests
{
    public class AgentStoreTests
    {
        [Fact]
        public void AgentStore_FileChuaCo_TraDanhSachRong()
        {
            using (var dir = new TempDir())
                Assert.Empty(AgentStore.Load(dir.Combine("agents.json")).Agents);
        }

        [Fact]
        public void AgentStore_AddSaveLoad_GiuDuLieuVaChuanHoaCheDoChay()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("agents.json");
                var store = AgentStore.Load(file);
                var agent = store.Add(new AgentRecord { Name = "Claude Code · DxFactory", Client = "claude", WorkspaceId = "ws1", RunMode = "linh tinh" });
                store.Save();

                var loaded = AgentStore.Load(file).Agents.Single();
                Assert.Equal(agent.Id, loaded.Id);
                Assert.Equal(8, loaded.Id.Length);
                Assert.Equal(RunModes.Auto, loaded.RunMode);
                Assert.False(loaded.IsGlobal);
                Assert.False(string.IsNullOrEmpty(loaded.CreatedAt));
            }
        }

        [Fact]
        public void AgentStore_ThieuTen_NemLoi()
        {
            using (var dir = new TempDir())
                Assert.Throws<ArgumentException>(() => AgentStore.Load(dir.Combine("a.json")).Add(new AgentRecord { Name = " " }));
        }

        [Fact]
        public void AgentStore_AgentToanMay_KhongCoWorkspace()
        {
            using (var dir = new TempDir())
            {
                var store = AgentStore.Load(dir.Combine("a.json"));
                Assert.True(store.Add(new AgentRecord { Name = "Toàn máy" }).IsGlobal);
            }
        }

        [Fact]
        public void AgentStore_DoiQuyenQuaUpdate_NguoiDocSauThayNgay()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("agents.json");
                var store = AgentStore.Load(file);
                var id = store.Add(new AgentRecord { Name = "A", WorkspaceId = "w" }).Id;
                store.Save();

                AgentStore.Update(file, s => { s.Find(id).RunMode = RunModes.None; return true; });
                Assert.Equal(RunModes.None, AgentStore.Load(file).Find(id).RunMode);
            }
        }

        [Fact]
        public void AgentStore_Touch_GhiLanCuoiVaBoQuaIdLa()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("agents.json");
                var store = AgentStore.Load(file);
                var id = store.Add(new AgentRecord { Name = "A" }).Id;
                store.Save();

                AgentStore.Touch(file, id);
                AgentStore.Touch(file, "khong-co");
                Assert.False(string.IsNullOrEmpty(AgentStore.Load(file).Find(id).LastSeenAt));
            }
        }

        [Fact]
        public void AgentStore_FileHong_NemLoiVaKhongGhiDe()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("agents.json");
                File.WriteAllText(file, "{ khong phai json");
                Assert.Throws<InvalidDataException>(() => AgentStore.Load(file));
                Assert.Equal("{ khong phai json", File.ReadAllText(file));
            }
        }

        [Fact]
        public void ProposalStore_ThemDeXuat_ChoDuyetVaGopTrung()
        {
            using (var dir = new TempDir())
            {
                var store = ProposalStore.Load(dir.Combine("p.json"));
                Proposal Make() => new Proposal
                {
                    AgentId = "a1", AgentName = "A", WorkspaceId = "w", Kind = ProposalKinds.UpdateTool, ToolId = "npm",
                    Fields = new Dictionary<string, string> { ["args"] = "--legacy-peer-deps" }, Reason = "lỗi peer",
                };
                var first = store.Add(Make());
                var again = store.Add(Make());
                store.Save();

                Assert.Same(first, again);
                var loaded = ProposalStore.Load(dir.Combine("p.json"));
                Assert.Single(loaded.Pending);
                Assert.Equal("--legacy-peer-deps", loaded.Proposals.Single().Fields["args"]);
            }
        }

        [Fact]
        public void ProposalStore_DeXuatKhacTruong_KhongGop()
        {
            using (var dir = new TempDir())
            {
                var store = ProposalStore.Load(dir.Combine("p.json"));
                store.Add(new Proposal { AgentId = "a", Kind = ProposalKinds.UpdateTool, ToolId = "npm", Fields = new Dictionary<string, string> { ["args"] = "x" } });
                store.Add(new Proposal { AgentId = "a", Kind = ProposalKinds.UpdateTool, ToolId = "npm", Fields = new Dictionary<string, string> { ["args"] = "y" } });
                Assert.Equal(2, store.Pending.Count());
            }
        }

        [Fact]
        public void ProposalStore_LoaiHoacToolSai_NemLoi()
        {
            using (var dir = new TempDir())
            {
                var store = ProposalStore.Load(dir.Combine("p.json"));
                Assert.Throws<ArgumentException>(() => store.Add(new Proposal { Kind = "xoa_sach", ToolId = "x" }));
                Assert.Throws<ArgumentException>(() => store.Add(new Proposal { Kind = ProposalKinds.DeleteTool, ToolId = "" }));
            }
        }

        [Fact]
        public void ProposalStore_Decide_ChiDoiMotLan()
        {
            using (var dir = new TempDir())
            {
                var store = ProposalStore.Load(dir.Combine("p.json"));
                var p = store.Add(new Proposal { AgentId = "a", Kind = ProposalKinds.DeleteTool, ToolId = "old" });

                var done = store.Decide(p.Id, approved: true);
                Assert.Equal(ProposalStates.Approved, done.State);
                Assert.False(string.IsNullOrEmpty(done.DecidedAt));
                Assert.Empty(store.Pending);
                Assert.Throws<InvalidOperationException>(() => store.Decide(p.Id, approved: false));
                Assert.Throws<KeyNotFoundException>(() => store.Decide("khong-co", approved: true));
            }
        }

        [Fact]
        public void ActivityLog_AppendRoiRead_MoiNhatTruoc()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("activity.jsonl");
                ActivityLog.Append(file, new ActivityEntry { AgentName = "A", Action = "tạo profile", Target = "lint fix", Result = "applied" });
                ActivityLog.Append(file, new ActivityEntry { AgentName = "A", Action = "đề xuất sửa tool", Target = "npm", Result = "pending" });

                var entries = ActivityLog.Read(file);
                Assert.Equal(2, entries.Count);
                Assert.Equal("npm", entries[0].Target);
                Assert.Equal("lint fix", entries[1].Target);
                Assert.False(string.IsNullOrEmpty(entries[0].Time));
            }
        }

        [Fact]
        public void ActivityLog_GioiHanSoDong_VaBoQuaDongHong()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("activity.jsonl");
                for (var i = 0; i < 5; i++)
                    ActivityLog.Append(file, new ActivityEntry { Action = "a", Target = "t" + i });
                File.AppendAllText(file, "{ dong hong" + Environment.NewLine);

                var entries = ActivityLog.Read(file, max: 3);
                Assert.Equal(3, entries.Count);
                Assert.Equal("t4", entries[0].Target);
            }
        }

        [Fact]
        public void ActivityLog_FileChuaCo_TraRong()
        {
            using (var dir = new TempDir())
                Assert.Empty(ActivityLog.Read(dir.Combine("khong-co.jsonl")));
        }
    }
}
