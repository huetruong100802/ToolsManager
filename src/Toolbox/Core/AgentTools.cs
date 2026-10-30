using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolbox.Core
{
    /// <summary>
    /// Đăng ký, sửa tool cho agent (MCP). Quy tắc chọn store và pack theo docs/design.md §4.
    /// Lỗi đầu vào ném <see cref="ArgumentException"/> trước khi thay đổi file gốc của tool, nhưng có thể đã chép dở vào thư viện.
    /// </summary>
    public static class AgentTools
    {
        private static readonly string[] InstallerExtensions = { ".exe", ".msi" };

        public sealed class Request
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public string Id { get; set; }
            public string Args { get; set; }
            public string Command { get; set; }
            public string Description { get; set; }
            public List<string> Tags { get; set; }
            public string Notes { get; set; }
            public string SetupPath { get; set; }
        }

        public sealed class Outcome
        {
            public ToolEntry Tool { get; set; }
            public bool Created { get; set; }
            /// <summary>Điều agent nên biết nhưng không phải lỗi, vd: file lớn giữ đường dẫn gốc.</summary>
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>
        /// Đăng ký tool. Trùng path (hoặc trùng fingerprint) với tool đã có thì cập nhật tool đó thay vì tạo mới.
        /// Không lưu: người gọi tự <see cref="ToolStore.Save"/> trong khoá <see cref="ToolStore.Lock"/>.
        /// </summary>
        public static Outcome Register(ToolStore store, Request request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Thiếu name.");
            if (string.IsNullOrWhiteSpace(request.Path))
                throw new ArgumentException("Thiếu path.");

            var path = Environment.ExpandEnvironmentVariables(request.Path);
            if (!Path.IsPathRooted(path))
                throw new ArgumentException("path phải là đường dẫn tuyệt đối.");

            var command = NullIfBlank(request.Command);
            var setupFile = ResolveSetup(request.SetupPath);
            if (command != null)
            {
                if (setupFile != null)
                    throw new ArgumentException("Lệnh không đi kèm setupPath.");
                if (!Directory.Exists(path))
                    throw new ArgumentException($"Với lệnh, path phải là thư mục chạy lệnh: {path}");
            }
            if (setupFile == null && !Exists(path))
                throw new ArgumentException($"Không tìm thấy path: {path}");

            var fingerprint = command == null ? Library.Fingerprint(path) : null;
            var existing = FindSame(store, path, fingerprint, command);
            return existing != null
                ? Refresh(store, existing, request, path, fingerprint, setupFile)
                : Create(store, request, path, fingerprint, setupFile);
        }

        /// <summary>Sửa các trường được truyền; trường bỏ trống thì giữ nguyên. Đổi path không chép lại file và đặt store=link.</summary>
        public static Outcome Update(ToolStore store, string id, Request request)
        {
            var entry = store.Find(id) ?? throw new ArgumentException($"Không có tool \"{id}\". Dùng list_tools để xem id.");

            string path = null;
            if (!string.IsNullOrWhiteSpace(request.Path))
            {
                path = Environment.ExpandEnvironmentVariables(request.Path);
                if (!Path.IsPathRooted(path) || !Exists(path))
                    throw new ArgumentException($"Không tìm thấy path tuyệt đối: {path}");
            }
            if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("name không được để trống.");
            if (request.Command != null && string.IsNullOrWhiteSpace(request.Command))
                throw new ArgumentException("command không được để trống; muốn bỏ lệnh thì unregister_tool rồi register_tool lại.");

            var command = request.Command == null ? entry.Command : request.Command.Trim();
            var target = path ?? entry.ResolvedPath;
            if (command != null && !Directory.Exists(target))
                throw new ArgumentException($"Với lệnh, path phải là thư mục chạy lệnh: {target}");

            if (request.Name != null)
                entry.Name = request.Name;
            if (command != null)
            {
                // Lệnh không chép và không đóng gói; path là thư mục làm việc.
                entry.Command = command;
                entry.Store = StoreMode.Link;
                entry.Pack = PackMode.None;
                entry.Fingerprint = null;
            }
            if (request.Args != null)
                entry.Args = request.Args;
            if (request.Description != null)
                entry.Description = request.Description;
            if (request.Tags != null)
                entry.Tags = request.Tags;
            if (request.Notes != null)
                entry.Notes = request.Notes;
            if (path != null)
            {
                entry.Path = path;
                entry.Store = StoreMode.Link;
                entry.Fingerprint = Library.Fingerprint(path);
            }

            store.Update(entry);
            return new Outcome { Tool = entry };
        }

        private static Outcome Create(ToolStore store, Request request, string path, string fingerprint, string setupFile)
        {
            if (Text.IsValidId(request.Id) && store.Find(request.Id) != null)
                throw new ArgumentException($"Đã có tool có id \"{request.Id}\". Dùng update_tool để sửa, hoặc bỏ trống id.");

            var command = NullIfBlank(request.Command);
            var entry = new ToolEntry
            {
                Id = NewId(store, Text.IsValidId(request.Id) ? request.Id : Text.ToId(request.Name)),
                Name = request.Name,
                Path = path,
                Args = request.Args,
                Command = command,
                Description = request.Description,
                Tags = request.Tags ?? new List<string>(),
                Notes = request.Notes,
                Fingerprint = fingerprint,
                Pack = command == null ? PackMode.Folder : PackMode.None,
            };
            var outcome = new Outcome { Created = true };

            try
            {
                // Lệnh không chép: path đã là thư mục làm việc.
                if (command == null && setupFile != null)
                    AttachSetup(store, entry, path, setupFile, outcome);
                else if (command == null)
                    Place(store, entry, path, outcome);
            }
            catch
            {
                // Chép dở thì bỏ bản chép, để thư viện không có thư mục mồ côi.
                DeleteFolder(store, entry.Id);
                throw;
            }

            store.Add(entry);
            outcome.Tool = entry;
            return outcome;
        }

        private static Outcome Refresh(ToolStore store, ToolEntry entry, Request request, string path, string fingerprint, string setupFile)
        {
            entry.Name = request.Name;
            entry.Args = request.Args ?? entry.Args;
            entry.Description = request.Description ?? entry.Description;
            entry.Tags = request.Tags ?? entry.Tags;
            entry.Notes = request.Notes ?? entry.Notes;
            entry.Fingerprint = fingerprint ?? entry.Fingerprint;

            var outcome = new Outcome { Created = false, Tool = entry };
            if (setupFile != null)
                AttachSetup(store, entry, path, setupFile, outcome);
            // Bản chép cũ không còn khớp nếu file gốc đổi; chép lại từ file gốc. Path đã trỏ vào thư viện thì không chép lại.
            else if (entry.Store == StoreMode.Copy && !Library.IsInside(path, Library.RootOf(store)))
                entry.Path = Library.CopyTarget(store, entry.Id, path);

            store.Update(entry);
            return outcome;
        }

        /// <summary>Không có bộ cài: chép nếu nhỏ, giữ đường dẫn gốc nếu lớn, không chép app đã cài trong Program Files.</summary>
        private static void Place(ToolStore store, ToolEntry entry, string path, Outcome outcome)
        {
            if (Library.IsInProgramFiles(path))
            {
                entry.Pack = PackMode.None;
                outcome.Warnings.Add("App nằm trong Program Files nên không chép và có pack=none. Để gửi kèm bộ cài, đăng ký lại với setupPath.");
                return;
            }

            // Đo đúng thứ sẽ được chép: cả thư mục chứa file, không chỉ file đó.
            if (Library.Measure(Library.CopyRootOf(path), Library.CopyLimitBytes) <= Library.CopyLimitBytes)
            {
                entry.Store = StoreMode.Copy;
                entry.Path = Library.CopyTarget(store, entry.Id, path);
                return;
            }

            outcome.Warnings.Add($"Lớn hơn {Mb(Library.CopyLimitBytes)} nên giữ đường dẫn gốc; nếu file bị di chuyển, tool sẽ mất đường dẫn.");
        }

        /// <summary>Gắn bộ cài đang có trên máy. Bộ cài quá lớn thì không chép, tool thành pack=none.</summary>
        private static void AttachSetup(ToolStore store, ToolEntry entry, string path, string setupFile, Outcome outcome)
        {
            entry.Store = StoreMode.Link;
            entry.Path = path;
            entry.Setup = null;

            if (Library.Measure(setupFile, Library.SetupLimitBytes) > Library.SetupLimitBytes)
            {
                entry.Pack = PackMode.None;
                outcome.Warnings.Add($"Bộ cài lớn hơn {Mb(Library.SetupLimitBytes)} nên không chép; tool có pack=none.");
                return;
            }

            entry.Setup = Library.CopySetup(store, entry.Id, setupFile);
            entry.Pack = PackMode.Setup;
        }

        private static string ResolveSetup(string setupPath)
        {
            if (string.IsNullOrWhiteSpace(setupPath))
                return null;

            var file = Environment.ExpandEnvironmentVariables(setupPath);
            if (!Path.IsPathRooted(file) || !File.Exists(file))
                throw new ArgumentException($"Không tìm thấy bộ cài (cần đường dẫn tuyệt đối): {file}");
            if (!InstallerExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("setupPath chỉ nhận file .exe hoặc .msi.");
            return file;
        }

        /// <summary>
        /// Tool đã có cùng path, hoặc cùng nội dung file (fingerprint) dù đường dẫn khác.
        /// Lệnh chỉ khớp lệnh cùng chuỗi trong cùng thư mục, và tool thường không khớp lệnh.
        /// </summary>
        private static ToolEntry FindSame(ToolStore store, string path, string fingerprint, string command) =>
            store.Tools.FirstOrDefault(t => t.IsCommand == (command != null) && SamePath(t.ResolvedPath, path)
                && (command == null || string.Equals(t.Command, command, StringComparison.OrdinalIgnoreCase)))
            ?? (fingerprint == null ? null : store.Tools.FirstOrDefault(t => !t.IsCommand && t.Fingerprint == fingerprint));

        private static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        /// <summary>Id chưa có trong danh sách và chưa có thư mục trong thư viện, để không ghi đè bản chép của tool đã gỡ.</summary>
        private static string NewId(ToolStore store, string baseId)
        {
            var id = store.UniqueId(baseId);
            for (var n = 2; Directory.Exists(Library.FolderOf(store, id)); n++)
                id = store.UniqueId($"{baseId}-{n}");
            return id;
        }

        private static void DeleteFolder(ToolStore store, string id)
        {
            try
            {
                var folder = Library.FolderOf(store, id);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Lỗi gốc quan trọng hơn; thư mục dở dang còn lại sẽ được NewId bỏ qua.
            }
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static string Mb(long bytes) => $"{bytes / (1024 * 1024)}MB";

        private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
