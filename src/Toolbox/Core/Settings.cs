using System;
using System.IO;
using System.Runtime.Serialization;

namespace Toolbox.Core
{
    /// <summary>Cài đặt giao diện của người dùng, lưu ở settings.json trong thư mục dữ liệu. File hỏng hoặc thiếu thì dùng mặc định.</summary>
    [DataContract]
    public class Settings
    {
        /// <summary>"system" (theo Windows), "light" hoặc "dark".</summary>
        [DataMember(Name = "theme", EmitDefaultValue = false)]
        public string Theme { get; set; }

        public static string DefaultPath => Path.Combine(DataDir.Root, "settings.json");

        public static Settings Load(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                if (!File.Exists(path))
                    return new Settings();
                using (var stream = File.OpenRead(path))
                    return Json.Read<Settings>(stream) ?? new Settings();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)
            {
                return new Settings();
            }
        }

        /// <summary>Ghi bằng file tạm rồi thay thế. Lỗi ghi bị bỏ qua: cài đặt không đáng làm hỏng thao tác của người dùng.</summary>
        public void Save(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                var temp = path + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    Json.Write(stream, this);
                if (File.Exists(path))
                    File.Replace(temp, path, destinationBackupFileName: null);
                else
                    File.Move(temp, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
