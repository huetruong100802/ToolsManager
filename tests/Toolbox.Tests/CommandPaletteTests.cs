using System;
using System.Collections.Generic;
using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Bảng lệnh Ctrl+K dựng thật (STA): lọc theo chữ gõ, Enter chạy, Ctrl+Enter mở nơi chứa, Esc đóng.</summary>
    public class CommandPaletteTests
    {
        private static void Sta(Action body) => PairEditorUiTests.RunSta(() =>
        {
            PairEditorUiTests.EnsureApp();
            body();
        });

        private static SearchIndex Index()
        {
            var workspace = new Workspace
            {
                Id = "w1", Name = "DxFactory", Path = @"C:\dx",
                Profiles = new List<Profile> { new Profile { Id = "p1", Label = "Dev API cổng 4200" } },
                Stacks = new List<StackDef> { new StackDef { Id = "s1", Label = "Dev toàn bộ" } },
            };
            var actions = new[] { new SearchEntry { Kind = SearchKind.Action, Title = "Thêm workspace", TargetId = "add-workspace" } };
            return SearchIndex.Build(new[] { new ToolEntry { Id = "git", Name = "Git", Command = "git" } }, new[] { workspace }, actions);
        }

        private static CommandPalette Make(TempDir dir)
        {
            var palette = new CommandPalette();
            palette.Initialize(Index, UsageStore.Load(dir.Combine("usage.json")));
            return palette;
        }

        [Fact]
        public void Open_HienGoiYKhiOTrong_VaGoLocKhongDau()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var palette = Make(dir);
                    Assert.False(palette.IsOpen);
                    palette.Open();
                    Assert.True(palette.IsOpen);
                    Assert.Equal("p1", palette.Shown[0].TargetId); // lệnh đã ghim đứng đầu, hành động điều hướng sau cùng
                    Assert.Equal("add-workspace", palette.Shown[1].TargetId);

                    palette.SetQuery("dev toan");
                    Assert.Equal("s1", palette.Shown.Single().TargetId);
                    Assert.True(palette.Selected.Runnable);
                }
            });
        }

        [Fact]
        public void Commit_ChayVaMoNoiChua_KhacNhau_VaDongBang()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var palette = Make(dir);
                    SearchEntry ran = null, revealed = null;
                    palette.Activated += e => ran = e;
                    palette.RevealRequested += e => revealed = e;

                    palette.Open();
                    palette.SetQuery("4200");
                    palette.Commit(false);
                    Assert.Equal("p1", ran?.TargetId);
                    Assert.False(palette.IsOpen);

                    palette.Open();
                    palette.SetQuery("4200");
                    palette.Commit(true);
                    Assert.Equal("p1", revealed?.TargetId);
                }
            });
        }

        [Fact]
        public void Commit_KhongCoKetQua_KhongLamGi_Toggle_DongMo()
        {
            Sta(() =>
            {
                using (var dir = new TempDir())
                {
                    var palette = Make(dir);
                    var fired = 0;
                    palette.Activated += e => fired++;
                    palette.Open();
                    palette.SetQuery("khong-co-gi-ca");
                    Assert.Empty(palette.Shown);
                    palette.Commit(false);
                    Assert.Equal(0, fired);
                    Assert.True(palette.IsOpen); // không có gì để chọn thì giữ bảng mở
                    palette.Toggle();
                    Assert.False(palette.IsOpen);
                    palette.Toggle();
                    Assert.True(palette.IsOpen);
                    palette.Close();
                }
            });
        }
    }
}
