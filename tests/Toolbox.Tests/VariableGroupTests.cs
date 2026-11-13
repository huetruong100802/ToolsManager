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
    /// <summary>Nhóm biến: tương thích file cũ, DPAPI, không lộ secret trong file/gói, luật tên biến, Resolve, chia sẻ.</summary>
    public class VariableGroupTests : IDisposable
    {
        private const string Secret = "S3cr3t-Value-XYZ";

        private readonly TempDir _dir = new TempDir();

        public void Dispose() => _dir.Dispose();

        private static VariableGroup NewGroup(string id = "g1", string label = "Dev-A")
        {
            var group = new VariableGroup { Id = id, Label = label, Description = "Chuỗi kết nối dev" };
            group.Variables.Add(new GroupVariable { Name = "ASPNETCORE_ENVIRONMENT", Value = "Development" });
            var cs = new GroupVariable { Name = "ConnectionStrings__Default" };
            VariableGroups.SetSecret(cs, Secret);
            group.Variables.Add(cs);
            return group;
        }

        // ---- Tương thích file cũ ----

        [Fact]
        public void Load_FileCuKhongCoVariableGroups_DocDuocVaListKhongNull()
        {
            var file = _dir.WriteFile("workspaces.json",
                @"{""workspaces"":[{""id"":""w1"",""name"":""x"",""path"":""C:\\x"",""color"":0,""bindings"":[],""customs"":[],
 ""profiles"":[{""id"":""p1"",""label"":""FE"",""kind"":""angular"",""action"":""serve"",""variant"":""dev"",""vals"":[]}],""stacks"":[]}],""schemaVersion"":2}");

            var ws = WorkspaceStore.Load(file, _dir.Combine("tools.json")).Workspaces.Single();

            Assert.NotNull(ws.VariableGroups);
            Assert.Empty(ws.VariableGroups);
            Assert.Null(ws.Profiles.Single().VariableGroupId);
        }

        [Fact]
        public void Save_KhongCoNhom_KhongGhiTruongMoi()
        {
            var file = _dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(file, _dir.Combine("tools.json"));
            store.Add(new Workspace { Name = "x", Path = _dir.Combine("repo") }).Profiles.Add(new Profile { Id = "p1", Label = "FE", Kind = "angular" });
            store.Save();

            var text = File.ReadAllText(file);
            Assert.DoesNotContain("variableGroups", text);
            Assert.DoesNotContain("\"variableGroup\"", text);
        }

        [Fact]
        public void Save_Load_GiuNhomVaProfileGan_AllowAgentThieuLaBat()
        {
            var file = _dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(file, _dir.Combine("tools.json"));
            var ws = store.Add(new Workspace { Name = "x", Path = _dir.Combine("repo") });
            ws.VariableGroups.Add(NewGroup());
            ws.Profiles.Add(new Profile { Id = "p1", Label = "Run", Kind = "dotnet", VariableGroupId = "g1" });
            store.Save();

            var back = WorkspaceStore.Load(file, _dir.Combine("tools.json")).Workspaces.Single();
            Assert.Equal("g1", back.Profiles.Single().VariableGroupId);
            var group = back.VariableGroups.Single();
            Assert.True(group.AllowAgent);
            Assert.Equal(2, group.Variables.Count);
            Assert.Equal("Development", group.Variables[0].Value);
            Assert.True(group.Variables[1].Secret);

            // Thiếu allowAgent trong JSON (nhóm viết tay) vẫn là bật.
            // File ghi có thụt lề ("allowAgent": true,), nên bỏ trường bằng regex chịu khoảng trắng.
            var json = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), "\"allowAgent\"\\s*:\\s*true\\s*,", "");
            Assert.DoesNotContain("allowAgent", json);
            var file2 = _dir.WriteFile("w2.json", json);
            Assert.True(WorkspaceStore.Load(file2, _dir.Combine("tools.json")).Workspaces.Single().VariableGroups.Single().AllowAgent);
        }

        // ---- DPAPI ----

        [Fact]
        public void ProtectUnprotect_RoundTrip_KhacPlaintext()
        {
            var enc = VariableGroups.Protect(Secret);

            Assert.DoesNotContain(Secret, enc);
            Assert.Equal(Secret, VariableGroups.Unprotect(enc));
            Assert.Equal("Tiếng Việt ✓ \"quote\" \\", VariableGroups.Unprotect(VariableGroups.Protect("Tiếng Việt ✓ \"quote\" \\")));
            Assert.NotEqual(enc, VariableGroups.Protect(Secret)); // DPAPI có salt: mỗi lần một chuỗi khác
        }

        [Fact]
        public void Json_DaGhi_KhongChuaPlaintextSecret()
        {
            var file = _dir.Combine("workspaces.json");
            var store = WorkspaceStore.Load(file, _dir.Combine("tools.json"));
            store.Add(new Workspace { Name = "x", Path = _dir.Combine("repo") }).VariableGroups.Add(NewGroup());
            store.Save();

            var text = File.ReadAllText(file);
            Assert.DoesNotContain(Secret, text);
            Assert.Contains("\"enc\"", text);
            Assert.Contains("Development", text); // biến thường vẫn là chữ thật
            var secretVar = WorkspaceStore.Load(file, _dir.Combine("tools.json")).Workspaces.Single().VariableGroups.Single().Variables[1];
            Assert.Null(secretVar.Value);
            Assert.Equal(Secret, VariableGroups.Unprotect(secretVar.Enc));
        }

        [Fact]
        public void SetSecret_Rong_LaCanNhap()
        {
            var v = new GroupVariable { Name = "K", Value = "cu" };
            VariableGroups.SetSecret(v, "");

            Assert.True(v.Secret);
            Assert.Null(v.Value);
            Assert.Null(v.Enc);
            Assert.True(v.NeedsInput);
        }

        // ---- Validate / Warnings ----

        [Theory]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("A=B", false)]
        [InlineData("A\tB", false)]
        [InlineData("A\nB", false)]
        [InlineData("FOO", true)]
        [InlineData("Conn__Default", true)]
        [InlineData("tên.có-dấu", true)]
        public void ValidateName_Luat(string name, bool valid)
        {
            Assert.Equal(valid, VariableGroups.ValidateName(name) == null);
        }

        [Fact]
        public void ValidateName_Toi255KyTu()
        {
            Assert.Null(VariableGroups.ValidateName(new string('A', 255)));
            Assert.NotNull(VariableGroups.ValidateName(new string('A', 256)));
        }

        [Fact]
        public void Validate_TrungTenKhongPhanBietHoaThuong_VaTenNhomTrong()
        {
            var group = new VariableGroup { Id = "g", Label = "G" };
            group.Variables.Add(new GroupVariable { Name = "Path2", Value = "1" });
            group.Variables.Add(new GroupVariable { Name = "path2", Value = "2" });
            group.Variables.Add(new GroupVariable { Name = "A=B", Value = "3" });

            var errors = VariableGroups.Validate(group);
            Assert.Equal(2, errors.Count);
            Assert.Contains(errors, e => e.Contains("trùng"));

            Assert.Empty(VariableGroups.Validate(NewGroup()));
            Assert.NotEmpty(VariableGroups.Validate(new VariableGroup { Label = " " }));
        }

        [Fact]
        public void Warnings_TenDeXungDot()
        {
            var group = new VariableGroup { Id = "g", Label = "G" };
            foreach (var name in new[] { "path", "Configuration", "PLATFORM", "OutDir", "TOOLBOX_DATA", "FOO" })
                group.Variables.Add(new GroupVariable { Name = name, Value = "x" });

            Assert.Equal(5, VariableGroups.Warnings(group).Count);
            Assert.Null(VariableGroups.WarningFor("FOO"));
        }

        // ---- Resolve / UsedBy ----

        [Fact]
        public void Resolve_NhomDayDu_TraEnvVaSecretDeChe()
        {
            var ws = new Workspace { Id = "w", Name = "w", Path = "C:\\x" };
            ws.VariableGroups.Add(NewGroup());
            ws.VariableGroups[0].Variables.Add(new GroupVariable { Name = "short", Secret = true, Enc = VariableGroups.Protect("abc") });

            var r = VariableGroups.Resolve(ws, "g1");

            Assert.True(r.Ok);
            Assert.Equal("Development", r.Env["aspnetcore_environment"]); // không phân biệt hoa thường
            Assert.Equal(Secret, r.Env["ConnectionStrings__Default"]);
            Assert.Equal("abc", r.Env["short"]);
            Assert.Equal(new[] { Secret }, r.Secrets); // "abc" < 4 ký tự: không che
            Assert.Equal("a *** b", VariableGroups.MaskSecrets("a " + Secret + " b", r.Secrets));
        }

        [Fact]
        public void Resolve_ThieuGiaTriSecret_LoiD5()
        {
            var ws = new Workspace { Id = "w", Name = "w", Path = "C:\\x" };
            var group = NewGroup();
            group.Variables[1].Enc = null;
            ws.VariableGroups.Add(group);

            var r = VariableGroups.Resolve(ws, "g1");

            Assert.False(r.Ok);
            Assert.Equal("Nhóm biến Dev-A chưa có giá trị cho ConnectionStrings__Default, nhập ở tab Biến", r.Error);
            Assert.Empty(r.Env);
            Assert.Empty(r.Secrets);
        }

        [Fact]
        public void Resolve_KhongGanHoacNhomMat()
        {
            var ws = new Workspace { Id = "w", Name = "w", Path = "C:\\x" };

            var none = VariableGroups.Resolve(ws, null);
            Assert.True(none.Ok);
            Assert.Empty(none.Env);

            var gone = VariableGroups.Resolve(ws, "khong-co");
            Assert.False(gone.Ok);
            Assert.Contains("khong-co", gone.Error);
        }

        [Fact]
        public void Resolve_EncHong_LoiKhongNemNgoaiLe()
        {
            var ws = new Workspace { Id = "w", Name = "w", Path = "C:\\x" };
            var group = NewGroup();
            group.Variables[1].Enc = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
            ws.VariableGroups.Add(group);

            var r = VariableGroups.Resolve(ws, "g1");

            Assert.False(r.Ok);
            Assert.Contains("ConnectionStrings__Default", r.Error);
        }

        [Fact]
        public void UsedBy_ChiTraProfileDangDungNhom()
        {
            var ws = new Workspace { Id = "w", Name = "w", Path = "C:\\x" };
            ws.Profiles.Add(new Profile { Id = "p1", Label = "A", Kind = "dotnet", VariableGroupId = "g1" });
            ws.Profiles.Add(new Profile { Id = "p2", Label = "B", Kind = "dotnet", VariableGroupId = "G1" });
            ws.Profiles.Add(new Profile { Id = "p3", Label = "C", Kind = "dotnet", VariableGroupId = "g2" });
            ws.Profiles.Add(new Profile { Id = "p4", Label = "D", Kind = "dotnet" });

            Assert.Equal(new[] { "p1", "p2" }, VariableGroups.UsedBy(ws, "g1").Select(p => p.Id).ToArray());
            Assert.Empty(VariableGroups.UsedBy(ws, "none"));
            Assert.Empty(VariableGroups.UsedBy(ws, null));
        }

        // ---- Chia sẻ (D8) ----

        private Workspace SourceWithGroup()
        {
            Directory.CreateDirectory(_dir.Combine("repo"));
            var ws = new Workspace { Id = "w-src", Name = "Nguồn", Path = _dir.Combine("repo") };
            ws.VariableGroups.Add(NewGroup());
            ws.VariableGroups.Add(NewGroup("g2", "Khác"));
            ws.Profiles.Add(new Profile { Id = "p1", Label = "Run", Kind = "dotnet", ActionId = "run", VariableGroupId = "g1" });
            return ws;
        }

        private string PackProfile(Workspace ws, out WorkspacePackPreview preview)
        {
            var library = ToolStore.Load(_dir.Combine("tools.json"));
            var zip = _dir.Combine("out", "ws" + Packer.FileExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(zip));
            preview = Packer.PackWorkspace(ws, library, new WorkspacePackSelection { ProfileIds = { "p1" } }, zip);
            return zip;
        }

        [Fact]
        public void Pack_KemNhomCuaProfile_KhongCoGiaTriSecret()
        {
            var ws = SourceWithGroup();

            var zip = PackProfile(ws, out var preview);

            string raw;
            using (var z = ZipFile.OpenRead(zip))
            using (var reader = new StreamReader(z.GetEntry(Packer.ManifestName).Open()))
                raw = reader.ReadToEnd();
            Assert.DoesNotContain(Secret, raw);
            Assert.DoesNotContain(ws.VariableGroups[0].Variables[1].Enc, raw);
            Assert.DoesNotContain("\"enc\"", raw);
            Assert.Contains("ConnectionStrings__Default", raw);
            Assert.Contains("Development", raw);
            Assert.DoesNotContain("Khác", raw); // nhóm không profile nào dùng thì không kèm
            Assert.Contains(preview.Warnings, w => w.Contains("secret"));

            var data = Packer.ReadManifest(zip).Workspace;
            var group = data.VariableGroups.Single();
            Assert.Equal("g1", data.Profiles.Single().VariableGroupId);
            Assert.True(group.Variables[1].Secret);
            Assert.Null(group.Variables[1].Value);
            Assert.Null(group.Variables[1].Enc);
            Assert.Equal("Development", group.Variables[0].Value);
        }

        [Fact]
        public void Import_NhomVaoWorkspaceKhac_SecretCanNhap_ProfileTroDungNhomMoi()
        {
            var zip = PackProfile(SourceWithGroup(), out _);
            var target = new Workspace { Id = "t1", Name = "Đích", Path = _dir.Combine("target") };

            var result = PackageImporter.ImportIntoWorkspace(ToolStore.Load(_dir.Combine("t-tools.json")), target, zip, _dir.Combine("inst"), null);

            Assert.Equal(1, result.VariableGroupsAdded);
            var group = target.VariableGroups.Single();
            Assert.NotEqual("g1", group.Id);
            Assert.Equal("Dev-A", group.Label);
            Assert.Equal("Development", group.Variables[0].Value);
            Assert.True(group.Variables[1].NeedsInput);
            Assert.Equal(group.Id, target.Profiles.Single().VariableGroupId);
            Assert.Contains(result.Warnings, w => w.Contains("ConnectionStrings__Default"));
            Assert.False(VariableGroups.Resolve(target, group.Id).Ok);

            // Nhập secret xong thì chạy được.
            VariableGroups.SetSecret(group.Variables[1], "gia-tri-moi-123");
            Assert.True(VariableGroups.Resolve(target, group.Id).Ok);
        }

        [Fact]
        public void Import_ReplaceNhomTrung_GiuSecretDaNhapOMayNhan()
        {
            var zip = PackProfile(SourceWithGroup(), out _);
            var target = new Workspace { Id = "t1", Name = "Đích", Path = _dir.Combine("target") };
            var mine = NewGroup("local", "dev-a"); // trùng nhãn không phân biệt hoa thường
            VariableGroups.SetSecret(mine.Variables[1], "local-secret-999");
            mine.Variables[0].Value = "Staging";
            target.VariableGroups.Add(mine);

            var result = PackageImporter.ImportIntoWorkspace(ToolStore.Load(_dir.Combine("t-tools.json")), target, zip, _dir.Combine("inst"),
                null, ConflictResolution.Replace);

            Assert.Equal(0, result.VariableGroupsAdded);
            var group = target.VariableGroups.Single();
            Assert.Equal("local", group.Id);
            Assert.Equal("Development", group.Variables[0].Value);
            Assert.Equal("local-secret-999", VariableGroups.Unprotect(group.Variables[1].Enc));
            Assert.Equal("local", target.Profiles.Single().VariableGroupId);
        }
    }
}
