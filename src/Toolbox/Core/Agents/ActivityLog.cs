using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Toolbox.Core.Agents
{
    /// <summary>Một việc agent đã làm. Result: applied, pending, rejected, failed.</summary>
    [DataContract]
    public class ActivityEntry
    {
        [DataMember(Name = "time")] public string Time { get; set; }
        [DataMember(Name = "agentId")] public string AgentId { get; set; }
        [DataMember(Name = "agentName")] public string AgentName { get; set; }
        [DataMember(Name = "workspaceId")] public string WorkspaceId { get; set; }
        [DataMember(Name = "action")] public string Action { get; set; }
        [DataMember(Name = "target")] public string Target { get; set; }
        [DataMember(Name = "result")] public string Result { get; set; }
    }

    /// <summary>Nhật ký việc của agent, mỗi dòng một JSON trong activity.jsonl. Chỉ ghi thêm, không sửa dòng cũ.</summary>
    public static class ActivityLog
    {
        private const string LockName = @"Global\ToolboxActivity";
        private const int MaxFileBytes = 2 * 1024 * 1024;

        public static string DefaultPath =>
            Path.Combine(DataDir.Root, "activity.jsonl");

        public static void Append(string path, ActivityEntry entry)
        {
            entry.Time = entry.Time ?? ListFile.Now();
            string line;
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ActivityEntry)).WriteObject(ms, entry);
                line = Encoding.UTF8.GetString(ms.ToArray());
            }

            using (ListFile.Lock(LockName))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                Trim(path);
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        /// <summary>Đọc tối đa <paramref name="max"/> dòng mới nhất, mới nhất trước. Dòng hỏng thì bỏ qua.</summary>
        public static List<ActivityEntry> Read(string path, int max = 200)
        {
            if (!File.Exists(path))
                return new List<ActivityEntry>();
            string[] lines;
            using (ListFile.Lock(LockName))
                lines = File.ReadAllLines(path, Encoding.UTF8);

            var result = new List<ActivityEntry>();
            foreach (var line in lines.Reverse())
            {
                if (result.Count >= max)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                        result.Add((ActivityEntry)new DataContractJsonSerializer(typeof(ActivityEntry)).ReadObject(ms));
                }
                catch (Exception ex) when (ex is SerializationException || ex is System.Xml.XmlException)
                {
                    // Dòng ghi dở: bỏ qua, không làm hỏng cả nhật ký.
                }
            }
            return result;
        }

        /// <summary>File quá lớn thì giữ nửa cuối, để nhật ký không phình mãi.</summary>
        private static void Trim(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
                return;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            File.WriteAllLines(path, lines.Skip(lines.Length / 2), new UTF8Encoding(false));
        }
    }
}
