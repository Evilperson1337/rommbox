using System;
using System.IO;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.RomBase
{
    internal sealed class RomInstallFileMover
    {
        public string StageAndMove(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("ROM source file not found.", sourcePath);
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDir);
            if (File.Exists(targetPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Removing existing ROM at '{targetPath}'.");
                File.Delete(targetPath);
            }

            if (IsSameVolume(sourcePath, targetPath))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                File.Delete(sourcePath);
            }

            logger?.Write(PlatformLogLevel.Info, $"ROM installed to '{targetPath}'.");
            return targetPath;
        }

        private static bool IsSameVolume(string source, string destination)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            {
                return false;
            }

            var sourceRoot = Path.GetPathRoot(source.Trim());
            var destinationRoot = Path.GetPathRoot(destination.Trim());
            return string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
