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

        public ImportWindow(PackManifest manifest, ToolStore store)
        {
            InitializeComponent();

            _rows = manifest.Tools.Select(t => new Row(t, store.Find(t.Id) != null)).ToList();
            RowList.ItemsSource = _rows;
            HeaderText.Text = $"Gói có {_rows.Count} tool"
                + (DateTime.TryParse(manifest.CreatedAt, out var created) ? $", tạo lúc {created:dd/MM/yyyy HH:mm}" : "")
                + ". Bỏ chọn tool không cần.";
            WarningBox.Visibility = manifest.Tools.Any(t => t.Pack != PackMode.None) ? Visibility.Visible : Visibility.Collapsed;
            DestinationBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Toolbox", "imported");
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
