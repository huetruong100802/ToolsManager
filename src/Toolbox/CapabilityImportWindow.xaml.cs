using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.Workspaces;

namespace Toolbox
{
    /// <summary>
    /// Nhập lệnh con và cờ từ nguồn khác ngoài quét help: file JSON (khoá của object), file văn bản + regex (nhóm 1),
    /// hoặc văn bản dán. Chỉ dựng <see cref="Imported"/>; bên gọi ghi vào bảng lệnh.
    /// </summary>
    public partial class CapabilityImportWindow : Window
    {
        private readonly string _cwd;
        private List<CapabilityEntry> _items = new List<CapabilityEntry>();

        public CapabilityImportWindow(string workingDirectory)
        {
            _cwd = workingDirectory;
            InitializeComponent();
            OnModeChanged(null, null);
        }

        /// <summary>Các mục sẽ nhập: <c>Action</c> là phạm vi (rỗng = lệnh con của tool), <c>Name</c> là tên đầy đủ.</summary>
        public IReadOnlyList<CapabilityEntry> Imported => _items;

        private int Mode => Math.Max(0, ModeBox.SelectedIndex);

        private void OnModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilePanel == null)
                return;
            var paste = Mode == 2;
            FilePanel.Visibility = paste ? Visibility.Collapsed : Visibility.Visible;
            PastePanel.Visibility = paste ? Visibility.Visible : Visibility.Collapsed;
            if (Mode == 0)
            {
                KeyLabel.Text = "Khoá JSON chứa danh sách";
                KeyHint.Text = "Mỗi khoá của object thành một lệnh con. Vd. scripts trong package.json.";
                if (KeyBox.Text.Length == 0 || KeyBox.Text.StartsWith("^"))
                    KeyBox.Text = "scripts";
                if (FileBox.Text.Length == 0)
                    FileBox.Text = "package.json";
            }
            else if (Mode == 1)
            {
                KeyLabel.Text = "Regex (nhóm 1 là tên lệnh)";
                KeyHint.Text = @"Vd. Makefile: ^([A-Za-z][\w-]*):  — mỗi dòng khớp cho một lệnh con.";
                if (KeyBox.Text == "scripts")
                    KeyBox.Text = @"^([A-Za-z][\w-]*):";
                if (FileBox.Text == "package.json")
                    FileBox.Text = "Makefile";
            }
            Recompute();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e) => Recompute();

        private void Recompute()
        {
            if (PreviewText == null || ScopeBox == null)
                return;
            var scope = ScopeBox.Text.Trim();
            var items = new List<CapabilityEntry>();
            string problem = null;
            try
            {
                if (Mode == 2)
                    items = FromText(PasteBox.Text, scope);
                else
                {
                    var file = FileBox.Text.Trim();
                    var key = KeyBox.Text.Trim();
                    if (file.Length > 0 && key.Length > 0)
                    {
                        var source = Mode == 0 ? Core.Workspaces.ValueSource.FromJson(file, key) : Core.Workspaces.ValueSource.FromPattern(file, key);
                        var names = ToolCatalog.ReadSource(_cwd, source);
                        if (names.Count == 0)
                            problem = "Không đọc được mục nào (kiểm tra file, khoá hoặc regex).";
                        items = names.Select(n => new CapabilityEntry { Action = "", Name = Join(scope, n) }).ToList();
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                problem = ex.Message;
            }

            _items = items.Where(i => CapabilityStore.IsSuffix(i.Name)).ToList();
            PreviewText.Text = problem ?? (_items.Count == 0
                ? "(chưa có gì)"
                : string.Join("\n", _items.Take(40).Select(i =>
                    (CapabilityStore.IsFlag(i.Name) ? "[" + (string.IsNullOrEmpty(i.Action) ? "tool" : i.Action) + "] " : "") + i.Name + (i.TakesValue ? " <giá trị>" : "")))
                    + (_items.Count > 40 ? $"\n… và {_items.Count - 40} mục nữa" : ""));
            CountText.Text = _items.Count > 0 ? $"{_items.Count} mục" : "";
            ImportButton.IsEnabled = _items.Count > 0;
        }

        private static string Join(string scope, string name) => scope.Length == 0 ? name : scope + " " + name;

        /// <summary>Văn bản dán: đầu ra help (cờ + lệnh con); không nhận ra gì thì mỗi dòng một tên, từ đầu là tên, phần sau là mô tả.</summary>
        private static List<CapabilityEntry> FromText(string text, string scope)
        {
            var result = new List<CapabilityEntry>();
            foreach (var flag in HelpParser.Parse(text))
                result.Add(new CapabilityEntry { Action = scope, Name = flag.Name, TakesValue = flag.TakesValue, Description = flag.Description });
            foreach (var sub in HelpParser.ParseSubcommands(text))
                result.Add(new CapabilityEntry { Action = "", Name = Join(scope, sub.Name), Description = sub.Description });
            if (result.Count > 0)
                return result;

            foreach (var line in (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var desc = parts.Length > 1 ? parts[1].Trim() : null;
                if (parts[0].StartsWith("-", StringComparison.Ordinal))
                    result.Add(new CapabilityEntry { Action = scope, Name = parts[0], Description = desc });
                else
                    result.Add(new CapabilityEntry { Action = "", Name = Join(scope, parts[0]), Description = desc });
            }
            return result;
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
                return;
            DialogResult = true;
        }
    }
}
