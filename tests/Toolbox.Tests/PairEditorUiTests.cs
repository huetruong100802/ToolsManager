using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Toolbox.Core.Workspaces;
using Toolbox.Views;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Dựng thật trình sửa cặp với style của app (luồng STA) để bắt lỗi giao diện như icon bị cắt mất.</summary>
    public class PairEditorUiTests
    {
        // Application.Current gắn với luồng đã tạo nó; mọi test UI chạy chung một luồng STA sống suốt tiến trình,
        // nếu không test sau thấy Application của luồng đã chết và style không áp dụng được.
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue =
            new System.Collections.Concurrent.BlockingCollection<Action>();
        private static readonly Thread UiThread = StartUiThread();

        private static Thread StartUiThread()
        {
            var thread = new Thread(() =>
            {
                foreach (var job in Queue.GetConsumingEnumerable())
                    job();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }

        private static void EnsureApp()
        {
            // Đóng cửa sổ cuối cùng sẽ tắt Application mặc định, các test sau mất dispatcher; giữ nó sống tới hết tiến trình.
            if (Application.Current == null)
                new App().InitializeComponent();
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        private static void RunSta(Action body)
        {
            Exception error = null;
            using (var done = new ManualResetEventSlim())
            {
                Queue.Add(() =>
                {
                    try { body(); }
                    catch (Exception ex) { error = ex; }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (error != null)
                throw new Exception(error.GetType().Name + ": " + error.Message, error);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var nested in Descendants<T>(child))
                    yield return nested;
            }
        }

        [Fact]
        public void SearchField_LocKhongPhanBietHoaThuongVaDau()
        {
            RunSta(() =>
            {
                EnsureApp();
                var items = new List<string> { "Đóng gói", "Build", "Chạy thử" };
                var field = new SearchField();
                var view = field.Attach(items, o => (string)o);

                field.Text = "dong";
                Assert.Equal(new[] { "Đóng gói" }, view.Cast<string>());
                field.Text = "BUI";
                Assert.Equal(new[] { "Build" }, view.Cast<string>());
                field.Text = "";
                Assert.Equal(3, view.Cast<string>().Count());
            });
        }

        [Fact]
        public void KeyCombo_GoDeLocTheoTenHoacMoTa()
        {
            RunSta(() =>
            {
                EnsureApp();
                var editor = new PairEditor();
                editor.SetSuggestions(new[]
                {
                    new CapabilityEntry { Name = "--project", TakesValue = true, Action = "run", Description = "file csproj" },
                    new CapabilityEntry { Name = "--no-build", Action = "run", Description = "bỏ qua build" },
                    new CapabilityEntry { Name = "--verbosity", TakesValue = true, Action = "run" },
                });
                editor.Load(new[] { new ParamPair("--no-build", "") });
                var host = new Window { Content = editor, Width = 600, Height = 300, ShowInTaskbar = false };
                host.Show();
                host.UpdateLayout();
                try
                {
                    Assert.True(Descendants<Button>(editor).Any(), "không có nút nào: hàng chưa dựng");
                    Assert.True(Descendants<ComboBox>(editor).Any(), "không có ComboBox; có " + string.Join(",", Descendants<FrameworkElement>(editor).Select(x => x.GetType().Name).Distinct()));
                    var combo = Descendants<ComboBox>(editor).First();
                    var view = (System.Windows.Data.ListCollectionView)combo.ItemsSource;
                    Assert.Equal(3, view.Count);
                    combo.Focus();
                    // Gõ vào chính ô nhập của combo (như người dùng), khớp mô tả chứ không khớp tên.
                    var box = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                    Assert.NotNull(box);
                    box.Text = "csproj";
                    host.UpdateLayout();
                    Assert.True(view.Cast<string>().SequenceEqual(new[] { "--project" }), "view=[" + string.Join("|", view.Cast<string>()) + "] text=" + combo.Text + " sel=" + combo.SelectedItem + " filter=" + (view.Filter != null));
                    Assert.Equal("csproj", combo.Text);
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void NutIcon_CoKichThuocThat_KhongBiPaddingCatMat()
        {
            RunSta(() =>
            {
                // Một Application cho cả tiến trình test; nạp style của app (App.xaml) để đo đúng như khi chạy thật.
                EnsureApp();

                var editor = new PairEditor();
                editor.SetSuggestions(new[] { new CapabilityEntry { Name = "--project", TakesValue = true, Action = "run" } });
                editor.Load(new[] { new ParamPair("--project", "a.csproj") });

                var host = new Window { Content = editor, Width = 700, Height = 300, ShowInTaskbar = false };
                host.Show();
                host.UpdateLayout();
                try
                {
                    // Chỉ xét nút icon của hàng (Content là Path); Path trong mẫu ComboBox/CheckBox không thuộc phạm vi này.
                    var buttons = Descendants<Button>(editor).Where(x => x.Content is Path && x.Visibility == Visibility.Visible).ToList();
                    Assert.Equal(3, buttons.Count);
                    foreach (var button in buttons)
                    {
                        var icon = (Path)button.Content;
                        // Path có Width cố định nên ActualWidth luôn bằng đó dù nút cha cắt mất; phải so với chỗ trống thật trong nút.
                        var room = button.ActualWidth - button.Padding.Left - button.Padding.Right
                            - button.BorderThickness.Left - button.BorderThickness.Right;
                        Assert.True(room >= icon.ActualWidth,
                            $"Icon \"{button.ToolTip}\" bị cắt: icon rộng {icon.ActualWidth} nhưng nút {button.ActualWidth} chỉ còn {room} sau padding {button.Padding}");
                    }
                    Assert.Contains(Descendants<Button>(editor), b => (b.Content as string) == "+ Thêm tham số");
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void NutGoiY_HienKhiDoiSoViTriCoGoiY_VaAnKhiKhong()
        {
            RunSta(() =>
            {
                EnsureApp();
                var data = new ToolCapabilities { Kind = "ui-demo" };
                data.Entries.Add(new CapabilityEntry { Action = "ef", Name = "migrations", Source = "scan" });
                data.Args.Add(new ArgHint { Action = "build", Name = "PROJECT", IsPath = true });

                var editor = new PairEditor();
                editor.SetPositionalSource(data, "ef", "");
                editor.Load(new[] { new ParamPair("", ""), new ParamPair("--no-build") });

                var host = new Window { Content = editor, Width = 700, Height = 300, ShowInTaskbar = false };
                host.Show();
                host.UpdateLayout();
                try
                {
                    Button Suggest(int row) => Descendants<Button>(editor).Where(b => (b.ToolTip as string) == "Chọn từ gợi ý").ElementAt(row);
                    Assert.Equal(Visibility.Visible, Suggest(0).Visibility);    // dòng key trống: gợi ý lệnh con của ef
                    Assert.Equal(Visibility.Collapsed, Suggest(1).Visibility);  // cờ --no-build không có gợi ý giá trị

                    var arrow = Suggest(0);
                    var icon = (Path)arrow.Content;
                    Assert.True(arrow.ActualWidth - arrow.Padding.Left - arrow.Padding.Right >= icon.ActualWidth, "mũi tên ▾ bị cắt");
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void KeyCombo_KhiHienLen_KhongGhiDeKeyDaCo()
        {
            RunSta(() =>
            {
                EnsureApp();
                var editor = new PairEditor();
                editor.SetSuggestions(new[]
                {
                    new CapabilityEntry { Name = "--context", TakesValue = true, Action = "ef" },
                    new CapabilityEntry { Name = "--no-build", TakesValue = false, Action = "ef" },
                    new CapabilityEntry { Name = "--project", TakesValue = true, Action = "ef" },
                });
                var input = new[] { new ParamPair("", "migrations"), new ParamPair("--project", "a.csproj"), new ParamPair("--no-build") };
                editor.Load(input);

                var host = new Window { Content = editor, Width = 700, Height = 300, ShowInTaskbar = false };
                host.Show();
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                try
                {
                    // Dropdown dựng xong (Loaded) không được tự chọn mục đầu và ghi đè key của dòng.
                    Assert.Equal(new[] { "", "--project", "--no-build" }, editor.GetPairs().Select(p => p.Key).ToArray());
                    Assert.Equal(new[] { "migrations", "a.csproj", "" }, editor.GetPairs().Select(p => p.Value).ToArray());
                    Assert.Equal(new[] { "", "--project", "--no-build" }, Descendants<ComboBox>(editor).Select(c => c.Text).ToArray());
                }
                finally
                {
                    host.Close();
                }
            });
        }
    }
}
