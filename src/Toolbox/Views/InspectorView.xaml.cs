using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>Panel bên phải hiện log và trạng thái của một tác vụ. Log cập nhật theo nhịp, không theo từng dòng.</summary>
    public partial class InspectorView : UserControl
    {
        private const string WindowTaskNote = "Tác vụ này chạy ở cửa sổ terminal riêng nên Toolbox không ghi log.";
        private const string EmptyNote = "Chưa có output.";

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private TaskHost _tasks;
        private TaskInfo _task;
        // Đặt từ luồng tác vụ, đọc trên luồng giao diện: cờ đơn giản là đủ.
        private volatile bool _logDirty;

        public InspectorView()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Tick();
            Visibility = Visibility.Collapsed;
        }

        private Window Host => Window.GetWindow(this);

        public void Initialize(TaskHost tasks)
        {
            _tasks = tasks;
            tasks.LineAdded += task => _logDirty = true;
            // Kết thúc tác vụ không đi qua LineAdded, nên cần cờ riêng để có dòng trạng thái cuối.
            tasks.Changed += () => _logDirty = true;
            _timer.Start();
        }

        /// <summary>Mở panel cho tác vụ này.</summary>
        public void Show(TaskInfo task)
        {
            _task = task;
            _logDirty = true;
            Visibility = Visibility.Visible;
            RenderHeader();
            RenderHint();
            RenderLog(toBottom: true);
        }

        private void Tick()
        {
            if (_task == null || Visibility != Visibility.Visible)
                return;
            if (_logDirty)
            {
                _logDirty = false;
                RenderLog(toBottom: false);
            }
            if (_task.State != _shownState)
                RenderHint();
            RenderHeader();
        }

        private Func<string, string> _workspaceRoot = id => null;
        private FailureHint _hint;
        private TaskState _shownState;

        /// <summary>Người dùng bấm nút hành động của thẻ lỗi (chạy npm install, xem tác vụ, trỏ lại thư mục).</summary>
        public event Action<FailureHint, TaskInfo> HintActionRequested;

        /// <summary>Cho thẻ lỗi biết gốc workspace của tác vụ, để văn bản gửi agent chỉ có đường dẫn tương đối.</summary>
        public void SetWorkspaceRootResolver(Func<string, string> resolver) => _workspaceRoot = resolver ?? (id => null);

        private void RenderHint()
        {
            var task = _task;
            if (task == null || task.State != TaskState.Failed)
            {
                _hint = null;
                HintCard.Visibility = Visibility.Collapsed;
                return;
            }
            _shownState = task.State;
            var hasPackage = !string.IsNullOrEmpty(task.Spec.Cwd) && System.IO.File.Exists(System.IO.Path.Combine(task.Spec.Cwd, "package.json"));
            _hint = FailureHints.Analyze(task.ExitCode, task.Lines().Reverse().Take(FailureHints.CopyLogLines).Reverse(), task.Spec.Cwd, hasPackageJson: hasPackage);
            HintCard.Visibility = Visibility.Visible;
            HintTitle.Text = _hint?.Title ?? "Tác vụ bị lỗi" + (task.ExitCode.HasValue ? $" (mã thoát {task.ExitCode})" : "");
            HintExplain.Text = _hint?.Explain ?? "Toolbox chưa nhận ra lỗi này. Đọc log, hoặc sao chép cho agent để nhờ tìm nguyên nhân.";
            HintActionButton.Visibility = _hint != null && _hint.Action != HintAction.None ? Visibility.Visible : Visibility.Collapsed;
            HintActionButton.Content = _hint?.ActionText;
        }

        private void OnHintAction(object sender, RoutedEventArgs e)
        {
            if (_hint != null && _task != null)
                HintActionRequested?.Invoke(_hint, _task);
        }

        private void OnCopyForAgent(object sender, RoutedEventArgs e)
        {
            if (_task == null)
                return;
            var text = FailureHints.CopyForAgent(_task.Spec.Label, _task.Spec.CommandLine, _workspaceRoot(_task.Spec.WorkspaceId),
                _task.Spec.Cwd, _task.ExitCode, _task.Lines(), _hint);
            try
            {
                Clipboard.SetText(text);
                Dialogs.Success("Đã sao chép. Dán vào cuộc trò chuyện với agent.");
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Dialogs.Error(Host, "Không ghi được vào clipboard (đang bị chương trình khác giữ). Thử lại sau giây lát.");
            }
        }

        private void RenderHeader()
        {
            var task = _task;
            var running = task.IsRunning;
            Title.Text = task.Spec.Label;
            StateLabel.Text = TaskBoard.StateText(task);
            StateLabel.Foreground = running ? Res("Accent") : task.State == TaskState.Done ? Res("Ok")
                : task.State == TaskState.Failed ? Res("Danger") : Res("Muted");

            var parts = new List<string> { TaskBoard.FormatElapsed(TaskBoard.Elapsed(task, DateTime.UtcNow)) };
            if (task.Port.HasValue)
                parts.Add("cổng " + task.Port.Value);
            Meta.Text = string.Join(" · ", parts);

            Cwd.Text = "Thư mục: " + task.Spec.Cwd;
            Command.Text = task.Spec.CommandLine;

            StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            RestartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            OpenLogButton.Visibility = task.LogPath == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <param name="toBottom">Mở tác vụ mới: luôn xuống cuối. Cập nhật định kỳ: giữ vị trí người dùng đang đọc, trừ khi họ đang ở cuối.</param>
        private void RenderLog(bool toBottom)
        {
            if (_task == null)
                return;
            if (_task.LogPath == null)
            {
                Log.Text = WindowTaskNote;
                return;
            }

            var lines = _task.Lines();
            var atBottom = toBottom || Log.LineCount <= 1 || Log.GetLastVisibleLineIndex() >= Log.LineCount - 2;
            var firstVisible = Log.GetFirstVisibleLineIndex();

            Log.Text = lines.Length == 0 ? EmptyNote : string.Join(Environment.NewLine, lines);
            if (atBottom)
                Log.ScrollToEnd();
            else if (firstVisible >= 0)
                Log.ScrollToLine(Math.Min(firstVisible, Math.Max(0, Log.LineCount - 1)));
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            if (_task != null)
                _tasks.Stop(_task.Id);
        }

        private void OnRestart(object sender, RoutedEventArgs e)
        {
            if (_task == null)
                return;
            var fresh = TaskActions.Restart(_tasks, _task, Host);
            if (fresh != null)
                Show(fresh);
        }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            if (_task != null)
                TaskActions.OpenLogFile(_task, Host);
        }

        /// <summary>Đóng panel log (phím Esc). Trả về true nếu panel đang mở và vừa được đóng.</summary>
        public bool Hide()
        {
            if (Visibility != Visibility.Visible)
                return false;
            _task = null;
            Visibility = Visibility.Collapsed;
            return true;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            _task = null;
            Visibility = Visibility.Collapsed;
        }

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    }
}
