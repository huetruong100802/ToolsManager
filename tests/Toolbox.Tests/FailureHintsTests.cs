using System.Linq;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    public class FailureHintsTests
    {
        private static bool Exists(string path) => true;

        [Theory]
        [InlineData("Error: listen EADDRINUSE: address already in use :::3000", "3000")]
        [InlineData("Port 4200 is already in use.", "4200")]
        [InlineData("Unhandled exception. System.IO.IOException: Failed to bind to address http://127.0.0.1:5000: address already in use.", "")]
        [InlineData("listen tcp :8080: bind: Only one usage of each socket address (protocol/network address/port) is normally permitted.", "")]
        public void CongBan_NhanDangVaNoiSoCongNeuCo(string line, string port)
        {
            var hint = FailureHints.Analyze(1, new[] { "bắt đầu", line }, @"C:\w", Exists);
            Assert.Equal("port-busy", hint.Code);
            Assert.Equal(HintAction.OpenTasks, hint.Action);
            if (port.Length > 0)
                Assert.Contains(port, hint.Title);
        }

        [Theory]
        [InlineData("'foo' is not recognized as an internal or external command,", 9009, "foo")]
        [InlineData("The term 'bar' is not recognized as the name of a cmdlet, function", 1, "bar")]
        public void KhongTimThayLenh_NeuTenLenh(string line, int code, string name)
        {
            var hint = FailureHints.Analyze(code, new[] { line }, @"C:\w", Exists);
            Assert.Equal("command-not-found", hint.Code);
            Assert.Contains(name, hint.Title);
        }

        [Fact]
        public void KhongTimThayLenh_ExitCode9009KhongCoDong_VanNhanRa()
        {
            Assert.Equal("command-not-found", FailureHints.Analyze(9009, new string[0], @"C:\w", Exists).Code);
        }

        [Fact]
        public void LenhCuaDuAnNode_KhongTimThay_ThanhThieuGoi_VaGoiYNpmInstall()
        {
            var hint = FailureHints.Analyze(9009, new[] { "'ng' is not recognized as an internal or external command," }, @"C:\w", Exists, hasPackageJson: true);
            Assert.Equal("missing-modules", hint.Code);
            Assert.Equal(HintAction.RunInstall, hint.Action);
            Assert.Equal("npm install", hint.Command);

            // node, git... là công cụ cài trên máy: vẫn là thiếu lệnh dù có package.json.
            var global = FailureHints.Analyze(9009, new[] { "'git' is not recognized as an internal or external command," }, @"C:\w", Exists, hasPackageJson: true);
            Assert.Equal("command-not-found", global.Code);
        }

        [Fact]
        public void ThieuNodeModules_NhanTuLog()
        {
            var hint = FailureHints.Analyze(1, new[] { "Error: Cannot find module 'express'", "Require stack:" }, @"C:\w", Exists);
            Assert.Equal("missing-modules", hint.Code);
        }

        [Fact]
        public void ThuMucChayKhongCon_UuTienHonMoiLoiKhac()
        {
            var hint = FailureHints.Analyze(1, new[] { "EADDRINUSE" }, @"C:\da-xoa", p => false);
            Assert.Equal("cwd-missing", hint.Code);
            Assert.Equal(HintAction.RelocateWorkspace, hint.Action);
        }

        [Theory]
        [InlineData("EACCES: permission denied, open '/x'")]
        [InlineData("Access is denied.")]
        public void BiTuChoiQuyen(string line) =>
            Assert.Equal("denied", FailureHints.Analyze(1, new[] { line }, @"C:\w", Exists).Code);

        [Fact]
        public void KhongKhopMau_TraNull_KhongDoanBua()
        {
            Assert.Null(FailureHints.Analyze(1, new[] { "Build failed: 3 errors", "CS1002: ; expected" }, @"C:\w", Exists));
            Assert.Null(FailureHints.Analyze(null, null, null, Exists));
        }

        [Theory]
        [InlineData(@"C:\repo", @"C:\repo", ".")]
        [InlineData(@"C:\repo\", @"C:\repo\web\app", "web/app")]
        [InlineData(@"C:\repo", @"D:\khac\thu-muc", "thu-muc")]
        [InlineData(@"C:\repo", "", ".")]
        public void RelativeCwd_KhongLoDuongDanTuyetDoi(string root, string cwd, string expected) =>
            Assert.Equal(expected, FailureHints.RelativeCwd(root, cwd));

        [Fact]
        public void CopyChoAgent_Cuoi40DongKhongDuongDanTuyetDoi()
        {
            var log = Enumerable.Range(1, 100).Select(i => "dòng " + i).ToList();
            var hint = FailureHints.Analyze(1, new[] { "EADDRINUSE :4200" }, @"C:\Users\ai-do\repo\web", Exists);
            var text = FailureHints.CopyForAgent("Dev web", "npm run start", @"C:\Users\ai-do\repo", @"C:\Users\ai-do\repo\web", 1, log, hint);
            Assert.Contains("Lệnh: npm run start", text);
            Assert.Contains("web", text);
            Assert.DoesNotContain("ai-do", text);
            Assert.Contains("dòng 100", text);
            Assert.Contains("dòng 61", text);
            Assert.DoesNotContain("dòng 60\r", text);
            Assert.Contains("40 dòng log cuối", text);
            Assert.Contains("Toolbox nhận dạng", text);
        }
    }
}
