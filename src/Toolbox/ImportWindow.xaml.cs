using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Toolbox.Core;

namespace Toolbox
{
    public partial class ImportWindow : Window
    {
        // Thứ tự khớp các ComboBoxItem trong ImportWindow.xaml.
        private static readonly ConflictResolution[] Resolutions =
            { ConflictResolution.KeepBoth, ConflictResolution.Replace, ConflictResolution.Skip };

        private readonly List<Row> _rows;

        public ImportWindow(PackManifest manifest, ToolStore store, IEnumerable<Toolbox.Core.Workspaces.Workspace> workspaces = null)
        {
            InitializeComponent();
            SetupTarget(manifest, workspaces);

            _rows = manifest.Tools.Select(t => new Row(t, store.Find(t.Id) != null)).ToList();
            RowList.ItemsSource = _rows;
            Search.Attach(_rows, o => ((Row)o).Tool.Name + " " + ((Row)o).Tool.Id + " " + ((Row)o).Info);
            HeaderText.Text = $"Gói có {_rows.Count} tool"
                + (DateTime.TryParse(manifest.CreatedAt, out var created) ? $", tạo lúc {created:dd/MM/yyyy HH:mm}" : "")
                + ". Bỏ chọn tool không cần.";
            WarningBox.Visibility = manifest.Tools.Any(t => t.Pack != PackMode.None) ? Visibility.Visible : Visibility.Collapsed;
            DestinationBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Toolbox", "imported");
        }

        // ---- Workspace nhận ----

        private const string NewWorkspaceLabel = "+ Tạo workspace mới từ thư mục…";
        private List<Toolbox.Core.Workspaces.Workspace> _workspaces = new List<Toolbox.Core.Workspaces.Workspace>();
        private int _lastTarget;
        private bool _suppressTarget;

        /// <summary>Gói có profile, workflow hoặc lệnh riêng: cần chọn workspace để nhận chúng.</summary>
        public bool HasWorkspaceContent { get; private set; }

        /// <summary>Id workspace có sẵn được chọn làm nơi nhận; null nếu tạo mới hoặc gói không có phần workspace.</summary>
        public string TargetWorkspaceId { get; private set; }

        /// <summary>Thư mục của workspace mới (khi chọn "Tạo workspace mới từ thư mục").</summary>
        public string NewWorkspaceFolder { get; private set; }

        private void SetupTarget(PackManifest manifest, IEnumerable<Toolbox.Core.Workspaces.Workspace> workspaces)
        {
            var data = manifest.Workspace;
            HasWorkspaceContent = data != null && (data.Profiles.Count > 0 || data.Stacks.Count > 0 || data.Customs.Count > 0);
            if (!HasWorkspaceContent)
                return;
            _workspaces = (workspaces ?? new Toolbox.Core.Workspaces.Workspace[0]).Where(w => !w.Archived).ToList();
            TargetPanel.Visibility = Visibility.Visible;
            foreach (var workspace in _workspaces)
                TargetCombo.Items.Add(workspace.Name);
            TargetCombo.Items.Add(NewWorkspaceLabel);
            _suppressTarget = true;
            TargetCombo.SelectedIndex = _workspaces.Count > 0 ? 0 : -1; // chưa có workspace: để người dùng tự chọn "Tạo mới", không bật hộp chọn thư mục ngay
            _suppressTarget = false;
            TargetHint.Text = $"Gói có {ShareSummary.Counts(0, data.Profiles.Count, data.Stacks.Count, data.Customs.Count)}. Nhập xong Toolbox kiểm máy này còn thiếu gì.";
            if (_workspaces.Count == 0)
                TargetHint.Text += " Chưa có workspace nào nên cần chọn thư mục để tạo.";
        }

        private void OnTargetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressTarget || !HasWorkspaceContent || TargetCombo.SelectedIndex < 0)
                return;
            if (TargetCombo.SelectedIndex < _workspaces.Count)
            {
                _lastTarget = TargetCombo.SelectedIndex;
                NewWorkspaceFolder = null;
                return;
            }
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục dự án cho workspace mới" })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    NewWorkspaceFolder = dialog.SelectedPath;
                    TargetHint.Text = "Sẽ tạo workspace mới tại: " + dialog.SelectedPath;
                    return;
                }
            }
            // Huỷ chọn thư mục: quay về lựa chọn trước (nếu chưa có workspace nào thì để trống, nút Nhập sẽ nhắc).
            TargetCombo.SelectedIndex = _workspaces.Count > 0 ? _lastTarget : -1;
        }

        public string Destination { get; private set; }

        public IDictionary<string, ConflictResolution> Choices { get; private set; }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục giải nén tool" })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    DestinationBox.Text = dialog.SelectedPath;
            }
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var selected = _rows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Dialogs.Error(this, "Chưa chọn tool nào.");
                return;
            }
            var destination = DestinationBox.Text.Trim().Trim('"');
            if (selected.Any(r => r.Tool.Pack != PackMode.None) && !Path.IsPathRooted(destination))
            {
                Dialogs.Error(this, "Thư mục giải nén phải là đường dẫn đầy đủ.");
                return;
            }

            if (HasWorkspaceContent)
            {
                if (TargetCombo.SelectedIndex < 0 || (TargetCombo.SelectedIndex >= _workspaces.Count && string.IsNullOrEmpty(NewWorkspaceFolder)))
                {
                    Dialogs.Error(this, "Chọn workspace nhận profile và workflow, hoặc chọn thư mục để tạo workspace mới.");
                    return;
                }
                TargetWorkspaceId = TargetCombo.SelectedIndex < _workspaces.Count ? _workspaces[TargetCombo.SelectedIndex].Id : null;
            }

            Destination = destination;
            Choices = selected.ToDictionary(r => r.Tool.Id, r => Resolutions[Math.Max(0, r.ResolutionIndex)]);
            DialogResult = true;
        }

        public sealed class Row
        {
            public Row(ToolEntry tool, bool isConflict)
            {
                Tool = tool;
                IsConflict = isConflict;
            }

            public ToolEntry Tool { get; }
            public bool IsConflict { get; }
            public bool IsChecked { get; set; } = true;
            public int ResolutionIndex { get; set; }

            public Visibility ConflictVisibility => IsConflict ? Visibility.Visible : Visibility.Collapsed;

            public string Info
            {
                get
                {
                    var kind = Tool.Pack == PackMode.None ? "Chỉ thông tin — " + Tool.Path
                        : Tool.Pack == PackMode.Setup ? "Kèm bộ cài " + Tool.Setup.Substring(Tool.Setup.LastIndexOf('/') + 1)
                        : "Kèm file";
                    return string.IsNullOrEmpty(Tool.Description) ? kind : kind + " · " + Tool.Description;
                }
            }
        }
    }
}
