using System;
using System.IO;

namespace RomMbox.Tests.Utilities
{
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rommbox-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string relativePath, byte[] contents)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            var directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(fullPath, contents ?? Array.Empty<byte>());
            return fullPath;
        }

        public string CreateTextFile(string relativePath, string contents)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            var directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, contents ?? string.Empty);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
            }
        }
    }
}
