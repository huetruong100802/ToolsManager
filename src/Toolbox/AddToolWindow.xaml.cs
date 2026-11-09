using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Toolbox.Core;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>Một tool người dùng đã chọn để gắn vào workspace.</summary>
    public sealed class ToolPick
    {
        public ToolEntry Entry { get; set; }

        /// <summary>Chưa có trong thư viện: bên gọi thêm vào thư viện (và lưu) trước khi gắn.</summary>
        public bool IsNew { get; set; }

        /// <summary>Thư mục chạy riêng của workspace (từ nhận diện thư mục); null là dùng mặc định của tool.</summary>
        public string Cwd { get; set; }
    }

    /// <summary>
    /// Gắn tool vào workspace: chọn tool có sẵn trong thư viện, nhận gợi ý từ thư mục dự án, hoặc đăng ký tool mới
    /// bằng đúng form của Thư viện (<see cref="ToolEditWindow"/>). Mọi tool là một loại, không phân biệt dựng sẵn hay thư viện.
    /// </summary>
    public partial class AddToolWindow : Window
    {
        private const string SuggestedSection = "Gợi ý từ thư mục này";
        private const string NewSection = "Tool mới (sẽ đăng ký vào thư viện)";
        private const string LibrarySection = "Trong thư viện";

        private sealed class Option : INotifyPropertyChanged
        {
            private bool _isChecked;

            public string Section { get; set; }
            public string Title { get; set; }
            public string Detail { get; set; }
            public ToolEntry Entry { get; set; }
            public bool IsNew { get; set; }
            public string Cwd { get; set; }

            public bool IsChecked
            {
                get => _isChecked;
                set
                {
                    if (_isChecked == value)
                        return;
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly ObservableCollection<Option> _options = new ObservableCollection<Option>();

        public AddToolWindow(Workspace workspace, ToolStore library)
        {
            InitializeComponent();

            var detections = workspace.PathExists ? WorkspaceDetector.Detect(workspace.ResolvedPath) : new List<Detection>();
            foreach (var found in detections.Where(d => workspace.Binding(d.Kind) == null))
            {
                var existing = library.Find(found.Kind);
                var preset = ToolPresets.Find(found.Kind);
                var entry = existing ?? (preset == null ? null : ToolPresets.ToEntry(preset));
                if (entry == null)
                    continue;
                _options.Add(Track(new Option
                {
                    Section = SuggestedSection, Title = entry.Name, Entry = entry, IsNew = existing == null, Cwd = found.Cwd,
                    Detail = "Nhận diện được: " + found.Reason + (existing == null ? "  ·  sẽ đăng ký vào thư viện" : ""),
                }));
            }
            foreach (var tool in library.Tools.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (workspace.Binding(tool.Id) != null || _options.Any(o => string.Equals(o.Entry.Id, tool.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _options.Add(Track(new Option { Section = LibrarySection, Title = tool.Name, Entry = tool, Detail = Describe(tool) }));
            }

            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_options);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Option.Section)));
            OptionList.ItemsSource = view;
            Search.Attach(view, o => ((Option)o).Title + " " + ((Option)o).Detail);
            UpdateState();
        }

        /// <summary>Các tool đã tick khi người dùng bấm Thêm.</summary>
        public IReadOnlyList<ToolPick> Picks { get; private set; } = new List<ToolPick>();

        private Option Track(Option option)
        {
            option.PropertyChanged += (s, e) => UpdateState();
            return option;
        }

        private static string Describe(ToolEntry tool)
        {
            if (!tool.IsCommand)
                return tool.ResolvedPath;
            return string.IsNullOrWhiteSpace(tool.Path) ? tool.Command : tool.Command + "  ·  " + tool.ResolvedPath;
        }

        private void UpdateState()
        {
            EmptyText.Visibility = _options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            OptionList.Visibility = _options.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = _options.Any(o => o.IsChecked);
        }

        private void OnNewTool(object sender, RoutedEventArgs e)
        {
            var draft = new ToolEntry();
            var dialog = new ToolEditWindow(draft, isNew: true) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            _options.Insert(0, Track(new Option
            {
                Section = NewSection, Title = draft.Name, Entry = draft, IsNew = true, Detail = Describe(draft), IsChecked = true,
            }));
            UpdateState();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Picks = _options.Where(o => o.IsChecked)
                .Select(o => new ToolPick { Entry = o.Entry, IsNew = o.IsNew, Cwd = o.Cwd })
                .ToList();
            DialogResult = true;
        }
    }
}
