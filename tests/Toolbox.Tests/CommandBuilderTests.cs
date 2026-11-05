using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class CommandBuilderTests
    {
        // Cờ cổng lấy từ bảng khả năng (vai trò port), không từ catalog.
        private static VariantDef Serve => CapabilityFixtures.Angular("serve", CapabilityFixtures.Port("angular", "serve", "--port")).Default;

        // Ô trong dấu nháy kép: mẫu tự viết, vì mẫu sinh từ bảng khả năng chỉ có {port} và {args}.
        private static VariantDef Filter => new VariantDef("filter", "Lọc", "dotnet test --filter \"{filter}\"", new ParamDef("filter", "Bộ lọc", ""));

        [Fact]
        public void Build_ThieuCong_BaoLoiVoiCongRong()
        {
            var errors = CommandBuilder.Validate(Serve, null);

            Assert.Equal("Cổng phải là số từ 1024 đến 65535.", errors["port"]);
            Assert.Throws<ArgumentException>(() => CommandBuilder.Build(Serve));
        }

        [Fact]
        public void Build_KhongCoHauToThem_BoKhoangTrang() =>
            Assert.Equal("npx ng serve --port 4200", CommandBuilder.Build(Serve, new Dictionary<string, string> { ["port"] = "4200" }));

        [Fact]
        public void Build_DungGiaTriNguoiDungNhap() =>
            Assert.Equal("npx ng serve --port 4201 --open", CommandBuilder.Build(Serve, new Dictionary<string, string> { ["port"] = "4201", ["args"] = "--open" }));

        [Theory]
        [InlineData("80")]
        [InlineData("70000")]
        [InlineData("abc")]
        [InlineData("42 00")]
        [InlineData("-4200")]
        public void Validate_CongSai_BaoLoi(string port)
        {
            var errors = CommandBuilder.Validate(Serve, new Dictionary<string, string> { ["port"] = port });
            Assert.Equal("Cổng phải là số từ 1024 đến 65535.", errors["port"]);
        }

        [Fact]
        public void Validate_CongTrung_NeuTenTacVu()
        {
            var used = new Dictionary<int, string> { [4200] = "Angular · Chạy dev" };
            var errors = CommandBuilder.Validate(Serve, new Dictionary<string, string> { ["port"] = "4200" }, used);
            Assert.Contains("Angular · Chạy dev", errors["port"]);
        }

        [Fact]
        public void Build_GiaTriNguyHiem_NemLoi() =>
            Assert.Throws<ArgumentException>(() => CommandBuilder.Build(CapabilityFixtures.Sub("git", "log").Default,
                new Dictionary<string, string> { ["args"] = "-n 5 & del *" }));

        [Fact]
        public void Build_TrongDauNhay_ChoPhepHoacNhungKhongChoPhepNhayKep()
        {
            Assert.Equal("dotnet test --filter \"A|B\"", CommandBuilder.Build(Filter, new Dictionary<string, string> { ["filter"] = "A|B" }));
            Assert.Throws<ArgumentException>(() => CommandBuilder.Build(Filter, new Dictionary<string, string> { ["filter"] = "A\" & calc" }));
        }

        [Fact]
        public void PortOf_LayThamSoCong()
        {
            Assert.Null(CommandBuilder.PortOf(Serve, null));
            Assert.Equal(4300, CommandBuilder.PortOf(Serve, new Dictionary<string, string> { ["port"] = "4300" }));
            Assert.Null(CommandBuilder.PortOf(CapabilityFixtures.Sub("git", "status").Default, null));
        }

        [Fact]
        public void NextFree_BoQuaCongDangDung()
        {
            var used = new Dictionary<int, string> { [4200] = "a", [4201] = "b" };

            Assert.Equal(4202, PortRules.NextFree(4200, used));
            Assert.Equal(1024, PortRules.NextFree(10, null));
            Assert.Equal(-1, PortRules.NextFree(65536, null));
        }
    }
}
