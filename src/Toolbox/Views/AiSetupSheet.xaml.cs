using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Toolbox.Core;
using Toolbox.Core.Agents;
using Toolbox.Core.Workspaces;

namespace Toolbox.Views
{
    /// <summary>
    /// "AI thiết lập": gọi thẳng CLI agent (không mở terminal, không dán prompt), hiện các lệnh đề xuất để duyệt từng mục.
    /// Chỉ khi bấm "Thêm lệnh đã chọn" mới có gì thay đổi; huỷ hoặc đóng sheet giữa chừng thì dừng tiến trình agent.
    /// </summary>
    public partial class AiSetupSheet : SheetBase
    {
        private readonly Workspace _workspace;
        private readonly IReadOnlyList<string> _detected;
        private readonly Func<string, string, TimeSpan, CancellationToken, System.Threading.Tasks.Task<AgentRunResult>> _run;
        private CancellationTokenSource _cts;
        private bool _closed;

        public override double SheetWidth => 560;

        public AiSetupSheet(Workspace workspace, IEnumerable<string> detectedTools, Func<string, string, TimeSpan, CancellationToken, System.Threading.Tasks.Task<AgentRunResult>> run = null)
        {
            InitializeComponent();
            SheetTitle = "AI thiết lập";
            _workspace = workspace;
            _detected = (detectedTools ?? Enumerable.Empty<string>()).ToList();
            CommandBox.Text = string.IsNullOrWhiteSpace(Settings.Load().AgentCommand) ? AgentRunner.DefaultCommand : Settings.Load().AgentCommand;
            _run = run;
            Loaded += (s, e) => Start();
            Unloaded += (s, e) => StopRun();
        }

        /// <summary>Các mục người dùng đã duyệt (có khi sheet xác nhận).</summary>
        public IReadOnlyList<SetupItem> Approved { get; private set; } = new List<SetupItem>();

        private async void Start()
        {
            StopRun();
            var command = CommandBox.Text.Trim();
            Settings.Update(s => s.AgentCommand = string.Equals(command, AgentRunner.DefaultCommand, StringComparison.Ordinal) ? null : command);

            ErrorBox.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ApplyButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            RunningPanel.Visibility = Visibility.Visible;
            RunningText.Text = "Agent đang đọc dự án. Có thể mất tới vài phút; bấm Huỷ để dừng.";
            Status.Text = "";

            var cts = _cts = new CancellationTokenSource();
            var prompt = SetupProposals.BuildPrompt(_workspace, _detected);
            var cwd = _workspace.ResolvedPath;
            AgentRunResult result;
            try
            {
                result = _run != null
                    ? await _run(command, prompt, AgentRunner.DefaultTimeout, cts.Token)
                    : await AgentRunner.RunAsync(command, prompt, cwd, AgentRunner.DefaultTimeout, cts.Token);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                result = new AgentRunResult { Status = AgentRunStatus.Failed, Error = ex.Message };
            }

            if (_closed || cts != _cts)
                return;
            _cts = null;
            RunningPanel.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;

            if (!result.Ok)
            {
                if (result.Status != AgentRunStatus.Cancelled)
                    ShowError(result.Error);
                return;
            }

            var parsed = SetupProposals.Parse(result.Output, _workspace);
            if (!parsed.Ok)
            {
                ShowError(parsed.Error);
                return;
            }

            ItemList.ItemsSource = parsed.Items;
            ResultPanel.Visibility = Visibility.Visible;
            ApplyButton.Visibility = Visibility.Visible;
            UpdateApply();
            if (parsed.Skipped > 0)
                Status.Text = parsed.Skipped + " mục bị bỏ (trùng hoặc không hợp lệ).";
        }

        private void ShowError(string message)
        {
            ErrorBox.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void StopRun()
        {
            var cts = _cts;
            _cts = null;
            if (cts == null)
                return;
            cts.Cancel();
            cts.Dispose();
        }

        private void UpdateApply()
        {
            var items = ItemList.ItemsSource as IEnumerable<SetupItem>;
            var count = items?.Count(i => i.IsChecked) ?? 0;
            ApplyButton.IsEnabled = count > 0;
            ApplyButton.Content = count > 0 ? "Thêm " + count + " lệnh" : "Thêm lệnh đã chọn";
        }

        private void OnItemToggled(object sender, RoutedEventArgs e) => UpdateApply();

        private void OnRetry(object sender, RoutedEventArgs e) => Start();

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _closed = true;
            StopRun();
            Close();
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            var items = ItemList.ItemsSource as IEnumerable<SetupItem>;
            Approved = items?.Where(i => i.IsChecked).ToList() ?? new List<SetupItem>();
            _closed = true;
            DialogResult = Approved.Count > 0;
        }
    }
}
