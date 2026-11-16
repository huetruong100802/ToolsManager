using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Toolbox.Core
{
    internal static class Json
    {
        /// <summary>
        /// Đọc JSON. Bỏ qua BOM UTF-8 ở đầu file: Notepad và PowerShell hay thêm BOM khi lưu, còn bộ đọc JSON của .NET coi đó là ký tự lạ
        /// và từ chối cả file, khiến người dùng sửa tay tools.json xong thì app không mở được.
        /// </summary>
        public static T Read<T>(Stream stream)
        {
            if (stream.CanSeek && stream.Length - stream.Position >= 3)
            {
                var start = stream.Position;
                if (!(stream.ReadByte() == 0xEF && stream.ReadByte() == 0xBB && stream.ReadByte() == 0xBF))
                    stream.Position = start;
            }
            var serializer = new DataContractJsonSerializer(typeof(T));
            return (T)serializer.ReadObject(stream);
        }

        /// <summary>JSON gọn của một đối tượng, dùng để so sánh nội dung hai đối tượng cùng loại.</summary>
        public static string Canonical<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Bản sao sâu qua JSON.</summary>
        public static T Clone<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }

        public static void Write<T>(Stream stream, T value)
        {
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true, indentChars: "  "))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
                writer.Flush();
            }
        }
    }
}
