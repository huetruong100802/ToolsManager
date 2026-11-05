using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>
    /// Sơ đồ bộ chạy sửa trực tiếp: Bắt đầu → các bước → Kết thúc. Bước nằm trong khung SONG SONG thì chạy cùng lúc.
    /// Mô hình trong bộ nhớ là danh sách đợt; mỗi đợt một bước (tuần tự) hoặc nhiều bước (một nhóm song song).
    /// Khi lưu, đợt nhiều bước được gán <see cref="StackStep.Group"/> 1, 2, 3… theo thứ tự.
    /// </summary>
    public partial class StackDiagram : UserControl
    {
        private const string DragFormat = "toolbox/profile-id";

        private readonly Workspace _workspace;
        private readonly StackDef _source;
        private readonly List<List<StackStep>> _batches = new List<List<StackStep>>();
        private Point _dragStart;
        private Profile _dragProfile;

        public StackDiagram(Workspace workspace, StackDef stack)
        {
            InitializeComponent();
            _workspace = workspace;
            _source = stack;
            LabelBox.Text = stack?.Label ?? "";
            Palette.ItemsSource = workspace.Profiles.ToList();
            PaletteHint.Text = workspace.Profiles.Count == 0
                ? "Workspace chưa có profile. Tạo profile ở tab Chạy trước."
                : "Kéo profile vào dấu +, hoặc bấm đúp để thêm vào cuối.";
            if (stack != null)
                LoadBatches(stack.Steps);
            Rebuild();
        }

        // ---- mô hình ----

        private void LoadBatches(IEnumerable<StackStep> steps)
        {
            int lastGroup = 0;
            foreach (var step in steps)
            {
                var copy = Clone(step);
                if (step.Group > 0 && step.Group == lastGroup && _batches.Count > 0)
                    _batches[_batches.Count - 1].Add(copy);
                else
                    _batches.Add(new List<StackStep> { copy });
                lastGroup = step.Group;
            }
        }

        private static StackStep Clone(StackStep s) => new StackStep
        {
            Kind = s.Kind, ActionId = s.ActionId, VariantId = s.VariantId, ProfileId = s.ProfileId,
            Wait = s.Wait, Port = s.Port, Group = s.Group,
        };

        private static StackStep NewStep(Profile profile)
        {
            var step = new StackStep { Kind = profile.Kind, ProfileId = profile.Id };
            // Profile loại thư viện/lệnh riêng không có action/biến thể; chỉ cần ProfileId (hợp đồng P10).
            if (!ProfileKind.IsRef(profile.Kind))
            {
                step.ActionId = profile.ActionId;
                step.VariantId = profile.VariantId;
            }
            return step;
        }

        /// <summary>Danh sách bước theo thứ tự chạy, Group đã gán lại theo đợt.</summary>
        public List<StackStep> FlatSteps()
        {
            var steps = new List<StackStep>();
            var group = 0;
            foreach (var batch in _batches)
            {
                var g = batch.Count >= 2 ? ++group : 0;
                foreach (var step in batch)
                {
                    var copy = Clone(step);
                    copy.Group = g;
                    steps.Add(copy);
                }
            }
            return steps;
        }

        /// <summary>Lỗi chặn lưu, hoặc null.</summary>
        public string Check()
        {
            if (string.IsNullOrWhiteSpace(LabelBox.Text))
                return "Bộ chạy cần có tên.";
            var steps = FlatSteps();
            if (steps.Count == 0)
                return "Bộ chạy cần ít nhất một bước. Kéo profile vào dấu +.";
            for (var i = 0; i < steps.Count; i++)
            {
                var resolved = StackResolver.Resolve(_workspace, steps[i], i);
                if (resolved.IsBroken)
                    return $"Bước {i + 1} \"{resolved.Label}\": {resolved.Problem}";
            }
            return StackPlan.Validate(steps);
        }

        public StackDef Build() => new StackDef
        {
            Id = _source?.Id,
            Label = LabelBox.Text.Trim(),
            Steps = FlatSteps(),
        };

        /// <summary>Mở sơ đồ trong cửa sổ; trả về bộ chạy đã sửa, hoặc null nếu huỷ.</summary>
        public static StackDef ShowDialog(Window owner, Workspace workspace, StackDef stack)
        {
            var diagram = new StackDiagram(workspace, stack);
            var window = new Window
            {
                Title = stack == null ? "Bộ chạy mới" : "Sơ đồ bộ chạy",
                Owner = owner, Width = 860, Height = 640, MinWidth = 640, MinHeight = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
                Style = (Style)Application.Current.Resources["AppWindow"],
            };
            StackDef result = null;

            var cancel = new Button { Content = "Huỷ", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = "Lưu", MinWidth = 90, Style = (Style)Application.Current.Resources["PrimaryButton"] };
            save.Click += (s, e) =>
            {
                var problem = diagram.Check();
                if (problem != null)
                {
                    Dialogs.Error(window, problem);
                    return;
                }
                result = diagram.Build();
                window.DialogResult = true;
            };
            var footer = new Border
            {
                Padding = new Thickness(20, 12, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0),
                Background = (Brush)Application.Current.Resources["Surface"],
                BorderBrush = (Brush)Application.Current.Resources["Line"],
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, save },
                },
            };
            diagram.Margin = new Thickness(20, 16, 20, 12);
            var root = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(diagram);
            window.Content = root;

            return window.ShowDialog() == true ? result : null;
        }

        // ---- thao tác ----

        private void InsertBatch(int position, Profile profile)
        {
            _batches.Insert(Math.Max(0, Math.Min(position, _batches.Count)), new List<StackStep> { NewStep(profile) });
            Rebuild();
        }

        private void AddToBatch(int batch, Profile profile)
        {
            _batches[batch].Add(NewStep(profile));
            Rebuild();
        }

        private void MoveBatch(int index, int delta)
        {
            var to = index + delta;
            if (to < 0 || to >= _batches.Count)
                return;
            var item = _batches[index];
            _batches.RemoveAt(index);
            _batches.Insert(to, item);
            Rebuild();
        }

        private void MoveStep(int batch, int index, int delta)
        {
            var list = _batches[batch];
            var to = index + delta;
            if (to < 0 || to >= list.Count)
                return;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(to, item);
            Rebuild();
        }

        private void RemoveStep(int batch, int index)
        {
            _batches[batch].RemoveAt(index);
            if (_batches[batch].Count == 0)
                _batches.RemoveAt(batch);
            Rebuild();
        }

        /// <summary>Gộp bước đơn này vào đợt ngay trước nó (chạy song song với đợt đó).</summary>
        private void JoinPrevious(int batch)
        {
            if (batch <= 0 || _batches[batch].Count != 1)
                return;
            _batches[batch - 1].AddRange(_batches[batch]);
            _batches.RemoveAt(batch);
            Rebuild();
        }

        /// <summary>Tách bước ra khỏi nhóm: thành đợt riêng, chạy ngay sau nhóm.</summary>
        private void Split(int batch, int index)
        {
            var step = _batches[batch][index];
            _batches[batch].RemoveAt(index);
            _batches.Insert(batch + 1, new List<StackStep> { step });
            Rebuild();
        }

        // ---- kéo thả từ bảng profile ----

        private void OnPalettePress(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragProfile = (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject))?.DataContext as Profile;
        }

        private void OnPaletteMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragProfile == null)
                return;
            var now = e.GetPosition(null);
            if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            var profile = _dragProfile;
            _dragProfile = null;
            DragDrop.DoDragDrop(Palette, new DataObject(DragFormat, profile.Id), DragDropEffects.Copy);
        }

        private void OnPaletteDouble(object sender, MouseButtonEventArgs e)
        {
            var profile = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Profile;
            if (profile != null)
                InsertBatch(_batches.Count, profile);
        }

        private Profile DroppedProfile(DragEventArgs e) =>
            e.Data.GetDataPresent(DragFormat)
                ? _workspace.Profiles.FirstOrDefault(p => string.Equals(p.Id, (string)e.Data.GetData(DragFormat), StringComparison.OrdinalIgnoreCase))
                : null;

        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null && !(node is T))
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            return node as T;
        }

        // ---- dựng giao diện ----

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        private static Style Sty(string key) => (Style)Application.Current.Resources[key];

        private void Rebuild()
        {
            Flow.Children.Clear();
            Flow.Children.Add(Terminal("Bắt đầu"));
            Flow.Children.Add(Slot(0));
            var flat = 0;
            for (var b = 0; b < _batches.Count; b++)
            {
                var batch = _batches[b];
                Flow.Children.Add(batch.Count == 1
                    ? Card(b, 0, flat)
                    : Group(b, flat));
                flat += batch.Count;
                Flow.Children.Add(Slot(b + 1));
            }
            Flow.Children.Add(Terminal("Kết thúc"));
        }

        private UIElement Terminal(string text) => new Border
        {
            Background = Res("Surface"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 5, 18, 5), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Res("Muted") },
        };

        /// <summary>Dấu + giữa hai đợt: kéo profile vào, hoặc bấm để chọn từ menu.</summary>
        private UIElement Slot(int position) => PlusButton("+", "Thêm bước", profile => InsertBatch(position, profile));

        private UIElement PlusButton(string text, string tip, Action<Profile> add, double width = 28)
        {
            var button = new Button
            {
                Content = text, Style = Sty("GhostButton"), Width = width, Height = 24, Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 3), AllowDrop = true,
                ToolTip = tip,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (s, e) => ShowMenu(button, add);
            button.DragOver += (s, e) =>
            {
                e.Effects = DroppedProfile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            button.Drop += (s, e) =>
            {
                var profile = DroppedProfile(e);
                if (profile != null)
                    add(profile);
                e.Handled = true;
            };
            return button;
        }

        private void ShowMenu(Button anchor, Action<Profile> add)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            if (_workspace.Profiles.Count == 0)
                menu.Items.Add(new MenuItem { Header = "Chưa có profile nào", IsEnabled = false });
            foreach (var profile in _workspace.Profiles)
            {
                var item = new MenuItem { Header = profile.Label };
                var captured = profile;
                item.Click += (s, e) => add(captured);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private UIElement Group(int batch, int firstFlat)
        {
            var inner = new StackPanel();
            for (var i = 0; i < _batches[batch].Count; i++)
            {
                var card = Card(batch, i, firstFlat + i);
                ((FrameworkElement)card).Margin = new Thickness(0, 0, 0, 6);
                inner.Children.Add(card);
            }
            inner.Children.Add(PlusButton("+ song song", "Thêm bước chạy song song trong nhóm", p => AddToBatch(batch, p), 110));

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var up = IconButton("▲", "Đưa nhóm lên", () => MoveBatch(batch, -1), batch > 0);
            var down = IconButton("▼", "Đưa nhóm xuống", () => MoveBatch(batch, 1), batch < _batches.Count - 1);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { up, down } };
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);
            head.Children.Add(new TextBlock
            {
                Text = "SONG SONG", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Accent"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            return new Border
            {
                BorderBrush = Res("Accent"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10), Width = 440, HorizontalAlignment = HorizontalAlignment.Center,
                Child = new StackPanel { Children = { head, inner } },
            };
        }

        private UIElement Card(int batch, int index, int flat)
        {
            var step = _batches[batch][index];
            var inGroup = _batches[batch].Count > 1;
            var resolved = StackResolver.Resolve(_workspace, step, flat);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = $"{flat + 1}. {resolved.Label}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (resolved.IsBroken)
                text.Children.Add(new TextBlock { Text = resolved.Problem, Foreground = Res("Danger"), FontSize = 12, TextWrapping = TextWrapping.Wrap });

            var wait = new ComboBox
            {
                ItemsSource = StepEditRow.WaitChoices, DisplayMemberPath = "Text", MinWidth = 150, FontSize = 12,
                Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Automation.AutomationProperties.SetName(wait, "Điều kiện sang bước sau");
            wait.SelectedItem = StepEditRow.WaitChoices.FirstOrDefault(w => w.Kind == (step.Wait ?? WaitKind.None)) ?? StepEditRow.WaitChoices[0];
            wait.SelectionChanged += (s, e) =>
            {
                if (!(wait.SelectedItem is WaitOption option))
                    return;
                var kind = option.Kind == WaitKind.None ? null : option.Kind;
                if (step.Wait == kind)
                    return;
                step.Wait = kind;
                Rebuild();
            };

            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (inGroup)
            {
                tools.Children.Add(IconButton("▲", "Đưa bước lên trong nhóm", () => MoveStep(batch, index, -1), index > 0));
                tools.Children.Add(IconButton("▼", "Đưa bước xuống trong nhóm", () => MoveStep(batch, index, 1), index < _batches[batch].Count - 1));
                tools.Children.Add(IconButton("⇥", "Đưa ra khỏi nhóm (chạy ngay sau nhóm)", () => Split(batch, index), true));
            }
            else
            {
                tools.Children.Add(IconButton("▲", "Đưa bước lên", () => MoveBatch(batch, -1), batch > 0));
                tools.Children.Add(IconButton("▼", "Đưa bước xuống", () => MoveBatch(batch, 1), batch < _batches.Count - 1));
                tools.Children.Add(IconButton("⫴", "Chạy song song với bước trước", () => JoinPrevious(batch), batch > 0));
            }
            tools.Children.Add(IconButton("✕", "Gỡ bước", () => RemoveStep(batch, index), true));

            var row = new DockPanel();
            DockPanel.SetDock(tools, Dock.Right);
            DockPanel.SetDock(wait, Dock.Right);
            row.Children.Add(tools);
            row.Children.Add(wait);
            row.Children.Add(text);

            return new Border
            {
                Background = Res("Surface"), BorderBrush = resolved.IsBroken ? Res("Danger") : Res("Line"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8), Width = inGroup ? 420 : 440,
                HorizontalAlignment = HorizontalAlignment.Center, Child = row,
            };
        }

        private static Button IconButton(string glyph, string tip, Action click, bool enabled)
        {
            var button = new Button
            {
                Content = glyph, Style = Sty("GhostButton"), Width = 26, Height = 26, Padding = new Thickness(0),
                IsEnabled = enabled, ToolTip = tip,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (s, e) => click();
            return button;
        }
    }
}
