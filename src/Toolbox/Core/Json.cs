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
