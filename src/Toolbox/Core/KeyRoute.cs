using System.Windows.Input;

namespace Toolbox.Core
{
    public enum WindowCommand { None, Palette, GoHome, GoLibrary, GoTasks, GoAgents, NextTab, PrevTab, Run, Stop, Help, Escape }

    /// <summary>Phím tắt toàn cửa sổ → lệnh. Tách khỏi MainWindow để test được và để bảng F1 đối chiếu với phím thật.</summary>
    public static class KeyRoute
    {
        public static WindowCommand Map(Key key, ModifierKeys modifiers)
        {
            var ctrl = (modifiers & ModifierKeys.Control) != 0;
            var shift = (modifiers & ModifierKeys.Shift) != 0;
            var alt = (modifiers & ModifierKeys.Alt) != 0;
            if (alt)
                return WindowCommand.None;

            if (ctrl && !shift)
            {
                switch (key)
                {
                    case Key.K: return WindowCommand.Palette;
                    case Key.D1: case Key.NumPad1: return WindowCommand.GoHome;
                    case Key.D2: case Key.NumPad2: return WindowCommand.GoLibrary;
                    case Key.D3: case Key.NumPad3: return WindowCommand.GoTasks;
                    case Key.D4: case Key.NumPad4: return WindowCommand.GoAgents;
                    case Key.Tab: return WindowCommand.NextTab;
                }
            }
            if (ctrl && shift && key == Key.Tab)
                return WindowCommand.PrevTab;
            if (!ctrl && key == Key.F5)
                return shift ? WindowCommand.Stop : WindowCommand.Run;
            if (!ctrl && !shift && key == Key.F1)
                return WindowCommand.Help;
            if (!ctrl && !shift && key == Key.Escape)
                return WindowCommand.Escape;
            return WindowCommand.None;
        }
    }
}
