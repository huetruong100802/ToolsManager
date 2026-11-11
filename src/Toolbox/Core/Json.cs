using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Toolbox.Core
{
    internal static class Json
    {
        public static T Read<T>(Stream stream)
        {
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
