using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Core;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Giao diện sáng/tối: hai bảng màu đủ khoá, đủ tương phản, đổi tại chỗ, và view không viết thẳng mã màu.</summary>
    public class ThemeTests
    {
        private static ResourceDictionary Load(string name) => new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Toolbox;component/Themes/Tokens." + name + ".xaml"),
        };

        private static double Luminance(Color c)
        {
            double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Contrast(Color a, Color b)
        {
            var (hi, lo) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
            return (hi + 0.05) / (lo + 0.05);
        }

        private static Color Colour(ResourceDictionary d, string key) => ((SolidColorBrush)d[key]).Color;

        [Fact]
        public void HaiBangMau_CoCungBoKhoa()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var light = Load("Light").Keys.Cast<string>().OrderBy(x => x).ToList();
                var dark = Load("Dark").Keys.Cast<string>().OrderBy(x => x).ToList();
                Assert.Equal(light, dark);
            });
        }

        // Chữ trên nền và chữ trên nút đặc phải đọc được ở cả hai giao diện (WCAG AA cho chữ thường: 4.5:1).
        public static IEnumerable<object[]> Pairs()
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                foreach (var surface in new[] { "Surface", "WindowBg" })
                    foreach (var ink in new[] { "Ink", "Muted", "Faint", "Accent", "Ok", "Danger" })
                        yield return new object[] { theme, ink, surface };
                yield return new object[] { theme, "WarnInk", "WarnSoft" };
                yield return new object[] { theme, "Ok", "OkSoft" };
                yield return new object[] { theme, "Danger", "DangerSoft" };
                yield return new object[] { theme, "Accent", "AccentSoft" };
                yield return new object[] { theme, "TagInk", "TagBg" };
                yield return new object[] { theme, "OnAccent", "AccentFill" };
                yield return new object[] { theme, "OnAccent", "AccentFillHover" };
                yield return new object[] { theme, "Ink", "CodeBg" };
            }
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void CapMau_DuTuongPhan(string theme, string foreground, string background)
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var d = Load(theme);
                var ratio = Contrast(Colour(d, foreground), Colour(d, background));
                Assert.True(ratio >= 4.5, $"{theme}: {foreground} trên {background} chỉ đạt {ratio:0.00}:1");
            });
        }

        [Fact]
        public void SetMode_DoiMauBrushDangDungVaCaChoDynamicResource()
        {
            PairEditorUiTests.RunSta(() =>
            {
                PairEditorUiTests.EnsureApp();
                var original = Theme.Mode;
                try
                {
                    var border = new Border();
                    border.SetResourceReference(Border.BackgroundProperty, "Surface");
                    var window = new Window { Content = border, Width = 100, Height = 100 };
                    window.Show();
                    var seen = new List<bool>();
                    Action handler = () => seen.Add(Theme.IsDark);
                    Theme.Changed += handler;
                    try
                    {
                        Theme.SetMode(ThemeMode.Light);
                        var light = ((SolidColorBrush)border.Background).Color;
                        Theme.SetMode(ThemeMode.Dark);
                        var dark = ((SolidColorBrush)border.Background).Color;
                        Assert.NotEqual(light, dark);
                        Assert.True(Theme.IsDark);
                        Assert.Equal(Colour(Load("Dark"), "Surface"), ((SolidColorBrush)Application.Current.FindResource("Surface")).Color);
                        Theme.SetMode(ThemeMode.Light);
                        Assert.Equal(light, ((SolidColorBrush)border.Background).Color);
                        Assert.Equal(new[] { true, false }, seen.Skip(seen.Count - 2).ToArray());
                    }
                    finally
                    {
                        Theme.Changed -= handler;
                        window.Close();
                    }
                }
                finally
                {
                    Theme.SetMode(original);
                }
            });
        }

        [Theory]
        [InlineData("dark", ThemeMode.Dark)]
        [InlineData("LIGHT", ThemeMode.Light)]
        [InlineData("system", ThemeMode.System)]
        [InlineData(null, ThemeMode.System)]
        [InlineData("khong-biet", ThemeMode.System)]
        public void Parse_DocCheDoDaLuu(string value, ThemeMode expected) => Assert.Equal(expected, Theme.Parse(value));

        [Fact]
        public void Settings_GhiRoiDoc_VaFileHongThiDungMacDinh()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("settings.json");
                new Settings { Theme = "dark" }.Save(path);
                Assert.Equal("dark", Settings.Load(path).Theme);
                File.WriteAllText(path, "{ không phải json");
                Assert.Null(Settings.Load(path).Theme);
                Assert.Null(Settings.Load(dir.Combine("khong-co.json")).Theme);
            }
        }

        [Fact]
        public void XamlCuaView_KhongVietThangMaMau()
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            string source = null;
            for (var dir = new DirectoryInfo(root); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Toolbox");
                if (Directory.Exists(candidate))
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null)
                return; // chạy ngoài cây mã nguồn
            var allowed = new[] { "App.xaml" }; // BrandGradient là màu thương hiệu, không đổi theo giao diện
            var hex = new Regex("\"#[0-9A-Fa-f]{6,8}\"");
            var offenders = Directory.GetFiles(source, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "Themes" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                            && !allowed.Contains(Path.GetFileName(f)))
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => new { f, i, line }))
                .Where(x => hex.IsMatch(x.line) && !x.line.Contains("DropShadowEffect"))
                .Select(x => Path.GetFileName(x.f) + ":" + (x.i + 1))
                .ToList();
            Assert.True(offenders.Count == 0, "Mã màu viết thẳng ở: " + string.Join(", ", offenders));
        }
    }
}
