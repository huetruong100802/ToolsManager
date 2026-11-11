using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Toolbox.Core;

namespace Toolbox.Views
{
    /// <summary>Một dòng trên Trang chủ. Cập nhật chi tiết tại chỗ (thời gian đã chạy) để nút không bị dựng lại.</summary>
    public sealed class HomeRow : INotifyPropertyChanged
    {
        private string _detail;

        public HomeRow(HomeItem item, string brushKey)
        {
            Item = item;
            _detail = item.Detail;
            BrushKey = brushKey;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public HomeItem Item { get; private set; }
        public string BrushKey { get; }
        public string Glyph => Item.Glyph;
        public string Title => Item.Title;
        public string ActionText => Item.ActionText;
        public string SecondaryText => Item.SecondaryText;
        public Visibility SecondaryVisibility => string.IsNullOrEmpty(Item.SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
        public Brush GlyphBrush => (Brush)Application.Current.FindResource(BrushKey);

        public string Detail
        {
            get => _detail;
            private set
            {
                if (_detail == value)
                    return;
                _detail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            }
        }

        /// <summary>Khoá nhận diện dòng giữa hai lần dựng (không gồm phần chi tiết đổi theo thời gian).</summary>
        public string Key => string.Join("|", Item.Target, Item.Title, Item.ActionText, Item.WorkspaceId, Item.ToolId, Item.Task?.Id);

        public void Update(HomeItem fresh)
        {
            Item = fresh;
            Detail = fresh.Detail;
        }
    }

    /// <summary>Thẻ workspace trên Trang chủ: dòng "Mở workspace" và các dòng lệnh chạy được.</summary>
    public sealed class HomeCardRow
    {
        public HomeCardRow(HomeCard card)
        {
            Card = card;
            Open = new HomeRow(new HomeItem
            {
                Title = card.Name, Target = HomeTarget.Workspace, WorkspaceId = card.WorkspaceId, ActionText = "Mở workspace",
            }, "Accent");
            Rows = card.Commands.Select(c => new HomeRow(c, "Accent")).ToList();
        }

        public HomeCard Card { get; }
        public HomeRow Open { get; }
        public List<HomeRow> Rows { get; }
        public string Title => Card.Name;
        public string Path => Card.Path;
        public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Khoá nhận diện thẻ giữa hai lần dựng: workspace và các lệnh đang hiện.</summary>
        public string Key => Card.WorkspaceId + "|" + string.Join(",", Rows.Select(r => r.Key));
    }

    public sealed class OnboardingRow
    {
        public OnboardingRow(OnboardingStep step)
        {
            Step = step;
        }

        public OnboardingStep Step { get; }
        public string Title => Step.Title;
        public string Detail => Step.Detail;
        public string ActionText => Step.ActionText;
        public Visibility ActionVisibility => Step.Done ? Visibility.Collapsed : Visibility.Visible;
        public string Check => Step.Done ? "" : "";
        public Brush CheckBrush => (Brush)Application.Current.FindResource(Step.Done ? "Ok" : "Faint");
    }

    /// <summary>
    /// Trang chủ: việc cần bạn xử lý (đề xuất chờ duyệt, tác vụ lỗi, thứ mất đường dẫn), thứ đang chạy, và chỗ để tiếp tục;
    /// lần đầu mở thì có ba bước làm quen. Mỗi dòng chỉ tới đúng nơi cần làm.
    /// </summary>
    public partial class OverviewView : UserControl
    {
        private readonly List<HomeRow> _attention = new List<HomeRow>();
        private readonly List<HomeRow> _running = new List<HomeRow>();
        private readonly List<HomeRow> _continue = new List<HomeRow>();
        private readonly List<HomeRow> _tools = new List<HomeRow>();
        private List<HomeCardRow> _cards = new List<HomeCardRow>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private Func<HomeFeed> _feed;
        private bool _dismissed;

        /// <summary>Bấm nút chính (false) hoặc nút phụ (true) của một dòng.</summary>
        public event Action<HomeItem, bool> ItemActivated;

        /// <summary>Bấm nút của một bước làm quen.</summary>
        public event Action<OnboardingStep> StepActivated;

        public OverviewView()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => { if (IsVisible) Refresh(); };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && _feed != null)
                    _timer.Start();
                else
                    _timer.Stop();
            };
        }

        /// <param name="feed">Dựng nội dung Trang chủ từ dữ liệu hiện tại; gọi mỗi lần làm mới.</param>
        public void Initialize(Func<HomeFeed> feed)
        {
            _feed = feed;
            _dismissed = Settings.Load().OnboardingDismissed;
            if (IsVisible)
                _timer.Start();
        }

        /// <summary>Giữ số dòng đang hiện (để test).</summary>
        public int Visible => _attention.Count + _running.Count + _continue.Count + _cards.Count + _tools.Count;

        public void Refresh()
        {
            if (_feed == null)
                return;
            Show(_feed(), _dismissed);
        }

        public void Show(HomeFeed feed, bool onboardingDismissed)
        {
            var showOnboarding = !onboardingDismissed && !feed.OnboardingDone;
            OnboardingCard.Visibility = showOnboarding ? Visibility.Visible : Visibility.Collapsed;
            if (showOnboarding)
                OnboardingList.ItemsSource = feed.Onboarding.Select(s => new OnboardingRow(s)).ToList();

            Sync(_attention, AttentionList, AttentionSection, feed.Attention, "Danger");
            Sync(_running, RunningList, RunningSection, feed.Running, "Ok");
            SyncCards(feed.Cards);
            Sync(_tools, ToolsList, ToolsSection, feed.PinnedTools, "Accent");
            Sync(_continue, ContinueList, ContinueSection, feed.Continue, "Accent");

            var empty = Visible == 0 && !showOnboarding;
            AllQuiet.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            Subtitle.Text = feed.Attention.Count > 0
                ? $"Có {feed.Attention.Count} việc cần bạn xử lý."
                : feed.Running.Count > 0 ? $"Đang chạy {feed.Running.Count} tác vụ." : "Chào bạn. Chọn việc để tiếp tục, hoặc bấm Ctrl+K để tìm.";
        }

        /// <summary>Thẻ workspace: chỉ dựng lại khi workspace hoặc lệnh hiện trên thẻ đổi.</summary>
        private void SyncCards(List<HomeCard> cards)
        {
            var fresh = cards.Select(c => new HomeCardRow(c)).ToList();
            if (!fresh.Select(c => c.Key).SequenceEqual(_cards.Select(c => c.Key)))
            {
                _cards = fresh;
                CardsList.ItemsSource = null;
                CardsList.ItemsSource = _cards;
            }
            CardsSection.Visibility = _cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Cùng danh sách dòng thì chỉ cập nhật chi tiết; khác thì dựng lại.</summary>
        private static void Sync(List<HomeRow> rows, ItemsControl list, FrameworkElement section, List<HomeItem> items, string brushKey)
        {
            var keys = items.Select(i => new HomeRow(i, brushKey).Key).ToList();
            if (keys.SequenceEqual(rows.Select(r => r.Key)))
            {
                for (var i = 0; i < rows.Count; i++)
                    rows[i].Update(items[i]);
            }
            else
            {
                rows.Clear();
                rows.AddRange(items.Select(i => new HomeRow(i, brushKey)));
                list.ItemsSource = null;
                list.ItemsSource = rows;
            }
            section.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPrimary(object sender, RoutedEventArgs e) => Activate(sender, false);

        private void OnSecondary(object sender, RoutedEventArgs e) => Activate(sender, true);

        private void Activate(object sender, bool secondary)
        {
            if ((sender as FrameworkElement)?.Tag is HomeRow row)
                ItemActivated?.Invoke(row.Item, secondary);
        }

        private void OnStep(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is OnboardingRow row)
                StepActivated?.Invoke(row.Step);
        }

        private void OnDismissOnboarding(object sender, RoutedEventArgs e)
        {
            Settings.Update(s => s.OnboardingDismissed = true);
            _dismissed = true;
            OnboardingCard.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }
}
