using System;
using System.IO;

namespace Toolbox.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "toolbox-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts) => System.IO.Path.Combine(Path, System.IO.Path.Combine(parts));

        public string WriteFile(string relative, string content)
        {
            var full = Combine(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            // Git tạo object chỉ đọc (nhất là trong worktree), nên bỏ thuộc tính đó trước khi xoá.
            try
            {
                if (!Directory.Exists(Path))
                    return;
                foreach (var file in Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
