using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Đóng gói theo workspace: kéo theo phụ thuộc, manifest không lộ đường dẫn máy, nhập vào workspace khác map đúng id.</summary>
    public class WorkspacePackTests : IDisposable
    {
        private readonly TempDir _dir = new TempDir();
        private readonly ToolStore _library;
        private readonly Workspace _ws;

        public WorkspacePackTests()
        {
            _library = ToolStore.Load(_dir.Combine("tools.json"));
            _library.Add(new ToolEntry { Id = "hello", Name = "Hello Tool", Path = _dir.WriteFile("lib\\hello\\hello.cmd", "@echo hello"), Args = "base" });
            _library.Add(new ToolEntry { Id = "free", Name = "Free Tool", Path = _dir.WriteFile("lib\\free\\free.cmd", "@echo free") });
            _library.Add(new ToolEntry { Id = "other", Name = "Other Tool", Path = _dir.WriteFile("lib\\other\\other.cmd", "@echo other") });
            _library.Save();

            Directory.CreateDirectory(_dir.Combine("repo", "sub"));
            _ws = new Workspace { Id = "w-src", Name = "Nguồn", Path = _dir.Combine("repo") };
            _ws.BindLibrary(_library.Find("hello"));
            _ws.LibraryBindings[0].Args = "--bound";
            _ws.Customs.Add(new CustomCommand { Id = "c1", Label = "Lệnh một", CommandLine = "echo one", Cwd = _dir.Combine("repo", "sub") });
            _ws.Customs.Add(new CustomCommand { Id = "c2", Label = "Lệnh hai", CommandLine = "echo two", Cwd = _dir.Combine("elsewhere") });
            _ws.Profiles.Add(new Profile { Id = "pl", Label = "Chạy hello", Kind = ProfileKind.Library, RefId = "hello", Vals = new Dictionary<string, string> { ["args"] = "-x" } });
            _ws.Profiles.Add(new Profile { Id = "pc", Label = "Chạy một", Kind = ProfileKind.Custom, RefId = "c1" });
            _ws.Profiles.Add(new Profile { Id = "pd", Label = "Build", Kind = "dotnet", ActionId = "build", VariantId = "" });
            _ws.Profiles.Add(new Profile { Id = "pz", Label = "Không dùng", Kind = ProfileKind.Library, RefId = "other" });
            _ws.Stacks.Add(new StackDef
            {
                Id = "s1", Label = "Cả bộ",
                Steps =
                {
                    new StackStep { Kind = ProfileKind.Library, ActionId = "hello", ProfileId = "pl", Group = 1 },
                    new StackStep { Kind = ProfileKind.Custom, ActionId = "c1", ProfileId = "pc", Group = 1 },
                    new StackStep { Kind = "custom", ActionId = "c2" },
                    new StackStep { Kind = ProfileKind.Library, ActionId = "free" },
                    new StackStep { Kind = "dotnet", ActionId = "build", ProfileId = "pd", Wait = WaitKind.Exit },
                },
            });
        }

        public void Dispose() => _dir.Dispose();

        private static WorkspacePackSelection Sel(string[] tools = null, string[] profiles = null, string[] stacks = null, string[] customs = null) =>
            new WorkspacePackSelection
            {
                ToolIds = (tools ?? new string[0]).ToList(),
                ProfileIds = (profiles ?? new string[0]).ToList(),
                StackIds = (stacks ?? new string[0]).ToList(),
                CustomIds = (customs ?? new string[0]).ToList(),
            };

        private string PackAll(out WorkspacePackPreview preview, WorkspacePackSelection sel = null)
        {
            var zip = _dir.Combine("out", "ws" + Packer.FileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(zip));
            preview = Packer.PackWorkspace(_ws, _library, sel ?? Sel(stacks: new[] { "s1" }), zip);
            return zip;
        }

        private static string RawManifest(string zip)
        {
            using (var z = ZipFile.OpenRead(zip))
            using (var reader = new StreamReader(z.GetEntry(Packer.ManifestName).Open()))
                return reader.ReadToEnd();
        }

        private static Workspace NewTarget(TempDir dir, string id) =>
            new Workspace { Id = id, Name = "Đích", Path = dir.Combine("target-" + id) };

        // ---- Resolve ----

        [Fact]
        public void Resolve_ChonBoChay_KeoTheoProfileToolVaLenhRieng()
        {
            var r = WorkspacePack.Resolve(_ws, Sel(stacks: new[] { "s1" }));

            Assert.Equal(new[] { "s1" }, r.StackIds);
            Assert.Equal(new[] { "pl", "pc", "pd" }, r.ProfileIds.OrderBy(x => x == "pl" ? 0 : x == "pc" ? 1 : 2).ToArray());
            Assert.DoesNotContain("pz", r.ProfileIds);
            Assert.Contains("hello", r.ToolIds);
            Assert.Contains("free", r.ToolIds);       // bước library không profile
            Assert.DoesNotContain("other", r.ToolIds);
            Assert.Contains("c1", r.CustomIds);        // qua profile pc
            Assert.Contains("c2", r.CustomIds);        // bước custom không profile
            Assert.Contains("Cả bộ", r.WhyPulled("profile", "pl"));
            Assert.Contains("Chạy hello", r.WhyPulled("tool", "hello"));
            Assert.Null(r.WhyPulled("profile", "khong-co"));
        }

        [Fact]
        public void Resolve_ProfileLibrary_KeoTheoTool_ProfileCustom_KeoTheoLenh()
        {
            var r = WorkspacePack.Resolve(_ws, Sel(profiles: new[] { "pl", "pc" }));

            Assert.Equal(new[] { "hello" }, r.ToolIds);
            Assert.Equal(new[] { "c1" }, r.CustomIds);
            Assert.Empty(r.StackIds);
            Assert.NotNull(r.WhyPulled("tool", "hello"));
        }

        [Fact]
        public void Resolve_MucTickTay_KhongCoLyDoKeoTheo_VaIdLaBiBo()
        {
            var r = WorkspacePack.Resolve(_ws, Sel(tools: new[] { "hello" }, profiles: new[] { "pl", "ma" }, customs: new[] { "ma2" }));

            Assert.Equal(new[] { "pl" }, r.ProfileIds);
            Assert.Empty(r.CustomIds);
            Assert.Equal(new[] { "hello" }, r.ToolIds);
            Assert.Null(r.WhyPulled("profile", "pl"));
        }

        [Fact]
        public void Preview_DemVaCanhBao()
        {
            _library.Remove("free");
            var p = WorkspacePack.Preview(_ws, _library, Sel(stacks: new[] { "s1" }));

            Assert.Equal(1, p.StackCount);
            Assert.Equal(3, p.ProfileCount);
            Assert.Equal(2, p.CustomCount);
            Assert.Equal(1, p.ToolCount); // chỉ hello còn trong thư viện
            Assert.Contains(p.Warnings, w => w.Contains("\"free\"") && w.Contains("thư viện"));
            Assert.Contains(p.Warnings, w => w.Contains("dotnet"));
            Assert.Contains(p.Warnings, w => w.Contains("Lệnh hai"));
        }

        // ---- Đóng gói ----

        [Fact]
        public void Pack_Manifest_KhongChuaDuongDanMay_VaDocLaiDuoc()
        {
            var zip = PackAll(out var preview);
            var raw = RawManifest(zip);
            var unescaped = raw.Replace("\\\\", "\\").Replace("\\/", "/");

            foreach (var text in new[] { raw, unescaped })
            {
                Assert.DoesNotContain(_dir.Path, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(_ws.Path, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("toolbox-tests", text, StringComparison.OrdinalIgnoreCase);
            }
            Assert.DoesNotContain("w-src", raw);   // id workspace nguồn
            Assert.DoesNotContain("Nguồn", raw);   // tên workspace nguồn
            Assert.DoesNotContain("worktree", raw, StringComparison.OrdinalIgnoreCase);

            var manifest = Packer.ReadManifest(zip);
            Assert.Equal(1, manifest.FormatVersion);
            Assert.NotNull(manifest.Workspace);
            Assert.Equal(new[] { "free", "hello" }, manifest.Tools.Select(t => t.Id).OrderBy(x => x).ToArray());
            Assert.Equal(3, manifest.Workspace.Profiles.Count);
            Assert.Single(manifest.Workspace.Stacks);
            Assert.Equal(5, manifest.Workspace.Stacks[0].Steps.Count);
            Assert.Equal(2, manifest.Workspace.Customs.Count);
            Assert.Equal("--bound", manifest.Workspace.Bindings.First(b => b.ToolId == "hello").Args);
            Assert.Contains("dotnet", manifest.Workspace.RequiredKinds);

            Assert.Equal("sub", manifest.Workspace.Customs.First(c => c.Id == "c1").Cwd);       // trong workspace -> tương đối
            Assert.True(string.IsNullOrEmpty(manifest.Workspace.Customs.First(c => c.Id == "c2").Cwd)); // ngoài -> bỏ
            Assert.Contains(preview.Warnings, w => w.Contains("Lệnh hai"));

            // Nguồn không bị đổi (Cwd tuyệt đối vẫn nguyên).
            Assert.Equal(_dir.Combine("repo", "sub"), _ws.Customs.First(c => c.Id == "c1").Cwd);
        }

        [Fact]
        public void Pack_ChiLenhRieng_KhongCanTool()
        {
            var zip = PackAll(out _, Sel(customs: new[] { "c1" }));
            var manifest = Packer.ReadManifest(zip);

            Assert.Empty(manifest.Tools);
            Assert.Single(manifest.Workspace.Customs);
        }

        [Fact]
        public void Pack_LuaChonRong_NemLoi()
        {
            Assert.Throws<ArgumentException>(() => PackAll(out _, Sel()));
        }

        // ---- Nhập ----

        [Fact]
        public void Import_GoiCu_KhongCoPhanWorkspace_VanNhapDuocVaGanVaoWorkspace()
        {
            var zip = _dir.Combine("old.toolbox");
            Packer.Pack(new[] { _library.Find("free") }, zip);
            Assert.Null(Packer.ReadManifest(zip).Workspace);

            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            var target = NewTarget(_dir, "t1");
            var result = PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"),
                new Dictionary<string, ConflictResolution> { ["free"] = ConflictResolution.KeepBoth });

            Assert.Single(result.Tools);
            Assert.NotNull(targetStore.Find("free"));
            Assert.NotNull(target.FindLibrary("free"));
            Assert.Empty(target.Profiles);
            Assert.Empty(target.Stacks);

            // Đường cũ vẫn nhập như trước.
            var store2 = ToolStore.Load(_dir.Combine("t2-tools.json"));
            var plain = PackageImporter.Import(store2, zip, _dir.Combine("inst2"),
                new Dictionary<string, ConflictResolution> { ["free"] = ConflictResolution.KeepBoth });
            Assert.Single(plain);
        }

        [Fact]
        public void Import_VaoWorkspaceKhac_MapDungId()
        {
            var zip = PackAll(out _);
            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            var target = NewTarget(_dir, "t1");
            var choices = new Dictionary<string, ConflictResolution>
            {
                ["hello"] = ConflictResolution.KeepBoth, ["free"] = ConflictResolution.KeepBoth,
            };

            var result = PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), choices);

            Assert.Equal(2, result.Tools.Count);
            Assert.Equal(2, result.BindingsAdded);
            Assert.Equal("--bound", target.FindLibrary("hello").Args);
            Assert.Equal(2, result.CustomsAdded);
            Assert.Equal(3, result.ProfilesAdded);
            Assert.Equal(1, result.StacksAdded);

            // Id mới, không dùng lại id nguồn.
            Assert.DoesNotContain(target.Profiles, p => p.Id == "pl" || p.Id == "pc" || p.Id == "pd");
            Assert.DoesNotContain(target.Customs, c => c.Id == "c1" || c.Id == "c2");
            Assert.All(target.Profiles.Select(p => p.Id).Concat(target.Customs.Select(c => c.Id)).Concat(target.Stacks.Select(s => s.Id)),
                id => Assert.False(string.IsNullOrWhiteSpace(id)));

            var stack = target.Stacks.Single();
            var libProfile = target.Profiles.Single(p => p.Label == "Chạy hello");
            var customProfile = target.Profiles.Single(p => p.Label == "Chạy một");
            var custom1 = target.Customs.Single(c => c.Label == "Lệnh một");
            var custom2 = target.Customs.Single(c => c.Label == "Lệnh hai");

            Assert.Equal(libProfile.Id, stack.Steps[0].ProfileId);
            Assert.Equal(customProfile.Id, stack.Steps[1].ProfileId);
            Assert.Equal(target.Profiles.Single(p => p.Label == "Build").Id, stack.Steps[4].ProfileId);
            Assert.Equal(custom1.Id, customProfile.RefId);
            Assert.Equal(custom2.Id, stack.Steps[2].ActionId);
            Assert.Equal("hello", libProfile.RefId);
            Assert.Equal("-x", libProfile.ExtraArgs);
            Assert.Equal("free", stack.Steps[3].ActionId);
            Assert.Equal(1, stack.Steps[0].Group);
            Assert.Equal(WaitKind.Exit, stack.Steps[4].Wait);
            Assert.Equal("sub", custom1.Cwd);
            Assert.Contains(result.Warnings, w => w.Contains("dotnet"));

            // Nguồn không bị đổi.
            Assert.Equal("pl", _ws.Stacks[0].Steps[0].ProfileId);
        }

        [Fact]
        public void Import_ToolTrungId_KeepBoth_RefIdTroDenToolMoi()
        {
            var zip = PackAll(out _);
            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            targetStore.Add(new ToolEntry { Id = "hello", Name = "Hello có sẵn", Path = _dir.WriteFile("mine\\hello.cmd", "@echo mine") });
            var target = NewTarget(_dir, "t1");

            var result = PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"),
                new Dictionary<string, ConflictResolution> { ["hello"] = ConflictResolution.KeepBoth, ["free"] = ConflictResolution.KeepBoth });

            Assert.Equal("hello-2", result.ToolMap["hello"]);
            Assert.Equal("Hello có sẵn", targetStore.Find("hello").Name);
            Assert.Equal("hello-2", target.Profiles.Single(p => p.Label == "Chạy hello").RefId);
            Assert.NotNull(target.FindLibrary("hello-2"));
            Assert.Null(target.FindLibrary("hello"));
        }

        [Fact]
        public void Import_HaiLan_KhongGhiDeThamLang()
        {
            var zip = PackAll(out _);
            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            var target = NewTarget(_dir, "t1");
            IDictionary<string, ConflictResolution> Choices() => new Dictionary<string, ConflictResolution>
            {
                ["hello"] = ConflictResolution.KeepBoth, ["free"] = ConflictResolution.KeepBoth,
            };

            PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), Choices());
            var firstStack = target.Stacks.Single();
            var firstStepProfile = firstStack.Steps[0].ProfileId;
            var firstProfile = target.Profiles.Single(p => p.Id == firstStepProfile);
            var firstSnapshot = firstProfile.Label + "|" + firstProfile.RefId;

            var second = PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), Choices());

            Assert.Equal(2, target.Stacks.Count);
            Assert.Equal(6, target.Profiles.Count);
            Assert.Equal(4, target.Customs.Count);
            Assert.Equal(3, second.ProfilesAdded);
            Assert.Equal(target.Stacks.Count, target.Stacks.Select(s => s.Label).Distinct().Count());
            Assert.Contains(target.Stacks, s => s.Label == "Cả bộ (2)");
            Assert.Contains(target.Profiles, p => p.Label == "Chạy hello (2)");

            // Lần 1 vẫn nguyên, lần 2 trỏ đúng profile/tool của lần 2.
            Assert.Equal(firstStepProfile, target.Stacks[0].Steps[0].ProfileId);
            Assert.Equal(firstSnapshot, firstProfile.Label + "|" + firstProfile.RefId);
            var secondStack = target.Stacks.Single(s => s.Label == "Cả bộ (2)");
            var secondProfile = target.Profiles.Single(p => p.Id == secondStack.Steps[0].ProfileId);
            Assert.Equal("Chạy hello (2)", secondProfile.Label);
            Assert.Equal("hello-2", secondProfile.RefId);
            Assert.NotEqual(firstStepProfile, secondProfile.Id);
        }

        [Fact]
        public void Import_Skip_DungLaiMucSanCo_KhongThemTrung()
        {
            var zip = PackAll(out _);
            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            var target = NewTarget(_dir, "t1");
            var choices = new Dictionary<string, ConflictResolution>
            {
                ["hello"] = ConflictResolution.Skip, ["free"] = ConflictResolution.Skip,
            };

            PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), choices, ConflictResolution.Skip);
            var again = PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), choices, ConflictResolution.Skip);

            Assert.Single(target.Stacks);
            Assert.Equal(3, target.Profiles.Count);
            Assert.Equal(2, target.Customs.Count);
            Assert.Equal(0, again.ProfilesAdded);
            Assert.Equal(0, again.StacksAdded);
            Assert.Equal(2, targetStore.Tools.Count);
            var stack = target.Stacks.Single();
            Assert.Contains(target.Profiles, p => p.Id == stack.Steps[0].ProfileId);
        }

        [Fact]
        public void Import_Replace_SuaTaiChoGiuId()
        {
            var zip = PackAll(out _);
            var targetStore = ToolStore.Load(_dir.Combine("t-tools.json"));
            var target = NewTarget(_dir, "t1");
            var choices = new Dictionary<string, ConflictResolution>
            {
                ["hello"] = ConflictResolution.KeepBoth, ["free"] = ConflictResolution.KeepBoth,
            };
            PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"), choices);
            var mine = target.Profiles.Single(p => p.Label == "Chạy một");
            mine.Vals["args"] = "người dùng sửa";
            var idBefore = mine.Id;

            PackageImporter.ImportIntoWorkspace(targetStore, target, zip, _dir.Combine("inst"),
                new Dictionary<string, ConflictResolution> { ["hello"] = ConflictResolution.Skip, ["free"] = ConflictResolution.Skip },
                ConflictResolution.Replace);

            Assert.Equal(3, target.Profiles.Count);
            Assert.Equal(idBefore, target.Profiles.Single(p => p.Label == "Chạy một").Id);
            Assert.DoesNotContain("người dùng sửa", target.Profiles.Single(p => p.Label == "Chạy một").Vals.Values);
        }

        [Fact]
        public void ReadManifest_PhanWorkspaceIdTrung_LaGoiHong()
        {
            var zip = PackAll(out _);
            var bad = _dir.Combine("bad.toolbox");
            File.Copy(zip, bad);
            using (var z = ZipFile.Open(bad, ZipArchiveMode.Update))
            {
                var entry = z.GetEntry(Packer.ManifestName);
                string json;
                using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
                entry.Delete();
                json = json.Replace("\"id\": \"pc\"", "\"id\": \"pl\"");
                var fresh = z.CreateEntry(Packer.ManifestName);
                using (var w = new StreamWriter(fresh.Open())) w.Write(json);
            }

            Assert.Throws<InvalidDataException>(() => Packer.ReadManifest(bad));
        }
    }
}
