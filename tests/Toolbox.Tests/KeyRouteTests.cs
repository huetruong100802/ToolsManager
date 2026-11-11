using System;
using System.Linq;
using System.Windows.Input;
using Toolbox.Core;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    public class KeyRouteTests
    {
        [Theory]
        [InlineData(Key.K, ModifierKeys.Control, WindowCommand.Palette)]
        [InlineData(Key.D1, ModifierKeys.Control, WindowCommand.GoHome)]
        [InlineData(Key.D2, ModifierKeys.Control, WindowCommand.GoLibrary)]
        [InlineData(Key.D3, ModifierKeys.Control, WindowCommand.GoTasks)]
        [InlineData(Key.NumPad4, ModifierKeys.Control, WindowCommand.GoAgents)]
        [InlineData(Key.Tab, ModifierKeys.Control, WindowCommand.NextTab)]
        [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, WindowCommand.PrevTab)]
        [InlineData(Key.F5, ModifierKeys.None, WindowCommand.Run)]
        [InlineData(Key.F5, ModifierKeys.Shift, WindowCommand.Stop)]
        [InlineData(Key.F1, ModifierKeys.None, WindowCommand.Help)]
        [InlineData(Key.Escape, ModifierKeys.None, WindowCommand.Escape)]
        public void Map_PhimTat_RaDungLenh(Key key, ModifierKeys modifiers, WindowCommand expected) =>
            Assert.Equal(expected, KeyRoute.Map(key, modifiers));

        [Theory]
        [InlineData(Key.F, ModifierKeys.Control)] // Ctrl+F vẫn là tìm trong trang, không bị nuốt
        [InlineData(Key.K, ModifierKeys.None)]
        [InlineData(Key.D1, ModifierKeys.None)]
        [InlineData(Key.D1, ModifierKeys.Alt | ModifierKeys.Control)]
        [InlineData(Key.Tab, ModifierKeys.None)] // Tab thường để dời con trỏ
        [InlineData(Key.Enter, ModifierKeys.None)]
        [InlineData(Key.F5, ModifierKeys.Control)]
        public void Map_PhimKhac_KhongBiNuot(Key key, ModifierKeys modifiers) =>
            Assert.Equal(WindowCommand.None, KeyRoute.Map(key, modifiers));

        [Fact]
        public void BangF1_NeuDuMoiPhimTatThat()
        {
            var text = string.Join("\n", ShortcutsSheet.All.Select(s => s.Keys));
            foreach (var key in new[] { "Ctrl+K", "Ctrl+F", "Ctrl+1", "Ctrl+Tab", "F5", "Shift+F5", "Esc", "F1" })
                Assert.Contains(key, text);
        }

        [Fact]
        public void BangPhimTat_MoDongBangF1()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var sheet = new ShortcutsSheet();
                Assert.False(sheet.IsOpen);
                sheet.Toggle();
                Assert.True(sheet.IsOpen);
                sheet.Toggle();
                Assert.False(sheet.IsOpen);
            });
        }
    }
}
