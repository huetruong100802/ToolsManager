using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class ProposalScopeTests
    {
        private static Proposal ArgsProposal(string workspaceId = "w1", string args = "--moi") => new Proposal
        {
            Id = "p1", AgentId = "a1", AgentName = "Claude", WorkspaceId = workspaceId, Kind = ProposalKinds.UpdateTool,
            ToolId = "hello", ToolName = "Hello", Fields = new Dictionary<string, string> { ["args"] = args },
            State = ProposalStates.Pending,
        };

        [Fact]
        public void Scope_ThieuHoacLa_LaShared()
        {
            Assert.Equal(ProposalScopes.Shared, new Proposal().EffectiveScope);
            Assert.Equal(ProposalScopes.Shared, ProposalScopes.Normalize("linh tinh"));
            Assert.Equal(ProposalScopes.Workspace, ProposalScopes.Normalize("WORKSPACE"));
        }

        [Fact]
        public void Scope_FileCu_DocNhuShared_VaKhongGhiKhiMacDinh()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("proposals.json");
                var store = ProposalStore.Load(file);
                store.Add(ArgsProposal());
                store.Save();

                Assert.DoesNotContain("scope", File.ReadAllText(file));
                var loaded = ProposalStore.Load(file).Proposals.Single();
                Assert.Null(loaded.Scope);
                Assert.Equal(ProposalScopes.Shared, loaded.EffectiveScope);
            }
        }

        [Fact]
        public void Decide_DuyetTheoWorkspace_GhiScope_TuChoiThiKhong()
        {
            using (var dir = new TempDir())
            {
                var file = dir.Combine("proposals.json");
                var store = ProposalStore.Load(file);
                var a = store.Add(ArgsProposal());
                var b = store.Add(ArgsProposal(args: "--khac"));
                store.Decide(a.Id, true, ProposalScopes.Workspace);
                store.Decide(b.Id, false, ProposalScopes.Workspace);
                store.Save();

                var loaded = ProposalStore.Load(file).Proposals;
                Assert.Equal(ProposalScopes.Workspace, loaded.Single(p => p.Id == a.Id).EffectiveScope);
                Assert.Equal(ProposalScopes.Shared, loaded.Single(p => p.Id == b.Id).EffectiveScope);
            }
        }

        [Fact]
        public void CanScopeToWorkspace_ChiKhiChiDoiArgsVaCoWorkspace()
        {
            Assert.True(ProposalApplier.CanScopeToWorkspace(ArgsProposal(), out var none));
            Assert.Null(none);

            var more = ArgsProposal();
            more.Fields["path"] = "C:\\x.exe";
            Assert.False(ProposalApplier.CanScopeToWorkspace(more, out var why));
            Assert.Contains("args", why);

            Assert.False(ProposalApplier.CanScopeToWorkspace(ArgsProposal(workspaceId: ""), out _));

            var del = ArgsProposal();
            del.Kind = ProposalKinds.DeleteTool;
            Assert.False(ProposalApplier.CanScopeToWorkspace(del, out _));
        }

        [Fact]
        public void ApplyToWorkspace_GhiArgsChoDungWorkspace_ThuVienVaWorkspaceKhacGiuNguyen()
        {
            using (var dir = new TempDir())
            {
                var tools = ToolStore.Load(dir.Combine("tools.json"));
                var tool = tools.Add(new ToolEntry { Id = "hello", Name = "Hello", Path = dir.WriteFile("h.cmd", "@echo hi"), Args = "goc" });
                tools.Save();

                var workspaces = WorkspaceStore.Load(dir.Combine("workspaces.json"));
                var w1 = workspaces.Add(new Workspace { Name = "A", Path = dir.Combine("a") });
                var w2 = workspaces.Add(new Workspace { Name = "B", Path = dir.Combine("b") });
                w1.BindLibrary(tool);
                w2.BindLibrary(tool);

                var proposal = ArgsProposal(w1.Id, "--moi");
                var message = ProposalApplier.ApplyToWorkspace(workspaces, proposal);

                Assert.Contains("A", message);
                var b1 = w1.FindLibrary("hello");
                Assert.Equal("--moi", b1.Args);
                Assert.Equal("--moi", b1.EffectiveArgs(tool));
                Assert.Equal("goc", w2.FindLibrary("hello").EffectiveArgs(tool));
                Assert.Equal("goc", ToolStore.Load(dir.Combine("tools.json")).Find("hello").Args);
            }
        }

        [Fact]
        public void ApplyToWorkspace_ThieuWorkspaceHoacChuaGan_NemLoiVaKhongGhi()
        {
            using (var dir = new TempDir())
            {
                var workspaces = WorkspaceStore.Load(dir.Combine("workspaces.json"));
                var ws = workspaces.Add(new Workspace { Name = "A", Path = dir.Combine("a") });

                Assert.Throws<InvalidDataException>(() => ProposalApplier.ApplyToWorkspace(workspaces, ArgsProposal("khong-co")));
                Assert.Throws<InvalidDataException>(() => ProposalApplier.ApplyToWorkspace(workspaces, ArgsProposal(ws.Id)));
                Assert.Empty(ws.LibraryBindings);

                var more = ArgsProposal(ws.Id);
                more.Fields["name"] = "Moi";
                Assert.Throws<InvalidOperationException>(() => ProposalApplier.ApplyToWorkspace(workspaces, more));
            }
        }
    }
}
