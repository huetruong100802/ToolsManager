using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Toolbox.Core
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>
    /// Giao diện sáng/tối. Màu nằm ở Themes/Tokens.Light.xaml và Tokens.Dark.xaml (cùng bộ khoá). Bộ brush đang dùng là các brush
    /// trong Application.Resources; chuyển giao diện là chép màu từ bảng đích vào đúng các brush đó, nên mọi nơi tham chiếu
    /// (DynamicResource, StaticResource, brush lấy bằng FindResource trong code) đều đổi theo ngay, không cần dựng lại giao diện.
    /// </summary>
    public static class Theme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private static readonly Dictionary<bool, ResourceDictionary> Palettes = new Dictionary<bool, ResourceDictionary>();
        private static bool _hooked;

        public static ThemeMode Mode { get; private set; } = ThemeMode.System;

        public static bool IsDark { get; private set; }

        /// <summary>Báo sau khi giao diện (sáng/tối) thực sự đổi.</summary>
        public static event Action Changed;

        public static ThemeMode Parse(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "light": return ThemeMode.Light;
                case "dark": return ThemeMode.Dark;
                default: return ThemeMode.System;
            }
        }

        public static string Name(ThemeMode mode) => mode == ThemeMode.Light ? "light" : mode == ThemeMode.Dark ? "dark" : "system";

        /// <summary>Gọi một lần lúc khởi động (sau khi App.xaml nạp xong) với chế độ đã lưu.</summary>
        public static void Initialize(ThemeMode mode)
        {
            Mode = mode;
            Apply(Resolve(mode));
            if (_hooked)
                return;
            _hooked = true;
            // Đang theo Windows thì đổi theo khi người dùng đổi chế độ sáng/tối của hệ thống.
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == ThemeMode.System && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(Resolve(ThemeMode.System))));
            };
            // Cửa sổ nào mở ra cũng được đổi màu thanh tiêu đề theo giao diện hiện tại.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) => ApplyTitleBar(s as Window)));
        }

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            Apply(Resolve(mode));
        }

        /// <summary>Chế độ kế tiếp khi bấm nút đổi: theo hệ thống, sáng, tối, rồi lại theo hệ thống.</summary>
        public static ThemeMode Next(ThemeMode mode) =>
            mode == ThemeMode.System ? ThemeMode.Light : mode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.System;

        public static string Label(ThemeMode mode) =>
            mode == ThemeMode.Light ? "Giao diện: Sáng" : mode == ThemeMode.Dark ? "Giao diện: Tối" : "Giao diện: Theo hệ thống";

        private static bool Resolve(ThemeMode mode) =>
            mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemPrefersDark());

        private static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return false;
            }
        }

        private static ResourceDictionary Palette(bool dark)
        {
            if (!Palettes.TryGetValue(dark, out var palette))
                Palettes[dark] = palette = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Toolbox;component/Themes/Tokens." + (dark ? "Dark" : "Light") + ".xaml"),
                };
            return palette;
        }

        private static void Apply(bool dark)
        {
            var app = Application.Current;
            if (app == null)
                return;
            var changed = dark != IsDark;
            IsDark = dark;
            var palette = Palette(dark);
            foreach (var key in palette.Keys)
            {
                if (!(palette[key] is SolidColorBrush target))
                    continue;
                if (app.TryFindResource(key) is SolidColorBrush live && !live.IsFrozen)
                    live.Color = target.Color;
                else
                    app.Resources[key] = new SolidColorBrush(target.Color); // brush bị đóng băng: thay thế, chỉ DynamicResource mới theo
            }
            foreach (Window window in app.Windows)
                ApplyTitleBar(window);
            if (changed)
                Changed?.Invoke();
        }

        // ---- Thanh tiêu đề tối (Windows 10 1809+ và 11) ----

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void ApplyTitleBar(Window window)
        {
            if (window == null)
                return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            var on = IsDark ? 1 : 0;
            try
            {
                // 20 là mã chính thức; 19 dùng ở bản Windows 10 trước 20H1.
                if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }
    }
}
