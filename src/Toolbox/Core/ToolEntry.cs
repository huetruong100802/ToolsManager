using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;

namespace Toolbox.Core
{
    /// <summary>Khi đóng gói lấy gì từ tool.</summary>
    public enum PackMode
    {
        /// <summary>Cả thư mục chứa file chạy (hoặc chính thư mục nếu path là thư mục).</summary>
        Folder,
        /// <summary>Chỉ file chạy.</summary>
        File,
        /// <summary>Chỉ gửi thông tin, không kèm file (vd: app phải cài đặt).</summary>
        None,
        /// <summary>App phải cài đặt: gửi kèm file bộ cài (<see cref="ToolEntry.Setup"/>), path giữ nguyên như None.</summary>
        Setup,
    }

    /// <summary>Cách giữ file của tool.</summary>
    public enum StoreMode
    {
        /// <summary>Path trỏ tới chỗ gốc của file.</summary>
        Link,
        /// <summary>Path trỏ tới bản chép trong thư viện Toolbox (không mất khi file gốc bị di chuyển).</summary>
        Copy,
    }

    [DataContract]
    public class ToolEntry
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        /// <summary>File chạy hoặc thư mục; cho phép biến môi trường như %ProgramFiles%.</summary>
        [DataMember(Name = "path", Order = 2)]
        public string Path { get; set; }

        [DataMember(Name = "args", Order = 3, EmitDefaultValue = false)]
        public string Args { get; set; }

        [DataMember(Name = "description", Order = 4, EmitDefaultValue = false)]
        public string Description { get; set; }

        [DataMember(Name = "tags", Order = 5)]
        public List<string> Tags { get; set; } = new List<string>();

        [IgnoreDataMember]
        public PackMode Pack { get; set; } = PackMode.Folder;

        // Lưu enum dạng chuỗi ("folder"/"file"/"none"/"setup") cho dễ đọc khi mở tools.json.
        [DataMember(Name = "pack", Order = 6)]
        private string PackValue
        {
            get => Pack.ToString().ToLowerInvariant();
            set
            {
                if (string.IsNullOrEmpty(value))
                    Pack = PackMode.Folder;
                else if (Enum.TryParse(value, ignoreCase: true, out PackMode mode) && Enum.IsDefined(typeof(PackMode), mode))
                    Pack = mode;
                else
                    throw new SerializationException($"Giá trị pack không hợp lệ: \"{value}\" (chỉ nhận folder, file, none, setup).");
            }
        }

        /// <summary>File bộ cài (exe/msi) gửi kèm khi pack=setup. Trong gói là đường dẫn tương đối "id/...".</summary>
        [DataMember(Name = "setup", Order = 7, EmitDefaultValue = false)]
        public string Setup { get; set; }

        [IgnoreDataMember]
        public StoreMode Store { get; set; } = StoreMode.Link;

        // Mặc định link nên tools.json cũ không có trường này vẫn đọc đúng.
        [DataMember(Name = "store", Order = 8, EmitDefaultValue = false)]
        private string StoreValue
        {
            get => Store == StoreMode.Copy ? "copy" : null;
            set
            {
                if (string.IsNullOrEmpty(value) || string.Equals(value, "link", StringComparison.OrdinalIgnoreCase))
                    Store = StoreMode.Link;
                else if (string.Equals(value, "copy", StringComparison.OrdinalIgnoreCase))
                    Store = StoreMode.Copy;
                else
                    throw new SerializationException($"Giá trị store không hợp lệ: \"{value}\" (chỉ nhận link, copy).");
            }
        }

        /// <summary>Tên + dung lượng + SHA-256 của file chính, ghi khi đăng ký. Thư mục không có.</summary>
        [DataMember(Name = "fingerprint", Order = 9, EmitDefaultValue = false)]
        public string Fingerprint { get; set; }

        /// <summary>Ghi chú của agent: cách dùng, lưu ý.</summary>
        [DataMember(Name = "notes", Order = 10, EmitDefaultValue = false)]
        public string Notes { get; set; }

        [IgnoreDataMember]
        public DateTime? LastUsedAt { get; set; }

        // Lưu dạng ISO 8601 (UTC) thay vì định dạng /Date(...)/ của DataContractJsonSerializer.
        [DataMember(Name = "lastUsedAt", Order = 11, EmitDefaultValue = false)]
        private string LastUsedAtValue
        {
            get => LastUsedAt?.ToString("o", CultureInfo.InvariantCulture);
            set => LastUsedAt = string.IsNullOrEmpty(value)
                ? (DateTime?)null
                : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        /// <summary>Số lần chạy qua MCP run_tool.</summary>
        [DataMember(Name = "runCount", Order = 12, EmitDefaultValue = false)]
        public int RunCount { get; set; }

        [IgnoreDataMember]
        public string ResolvedPath => Environment.ExpandEnvironmentVariables(Path ?? "");

        [IgnoreDataMember]
        public bool PathExists => File.Exists(ResolvedPath) || Directory.Exists(ResolvedPath);

        [IgnoreDataMember]
        public string ResolvedSetup => Environment.ExpandEnvironmentVariables(Setup ?? "");

        /// <summary>Máy này chưa có app nhưng có bộ cài đi kèm.</summary>
        [IgnoreDataMember]
        public bool CanInstall => Pack == PackMode.Setup && !PathExists && File.Exists(ResolvedSetup);

        public ToolEntry Clone()
        {
            var copy = (ToolEntry)MemberwiseClone();
            copy.Tags = new List<string>(Tags ?? new List<string>());
            return copy;
        }

        // DataContractJsonSerializer không chạy constructor/initializer khi đọc.
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Tags == null)
                Tags = new List<string>();
        }
    }
}
