using System;
using System.IO;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install
{
    internal static class InstallContentRelocator
    {
        public static string RelocateExtractedContent(string extractedPath, string targetDirectory, LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || string.IsNullOrWhiteSpace(targetDirectory))
            {
                return extractedPath;
            }

            if (!Directory.Exists(extractedPath))
            {
                return extractedPath;
            }

            var normalizedExtracted = Path.GetFullPath(extractedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var normalizedTarget = Path.GetFullPath(targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(normalizedExtracted, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return extractedPath;
            }

            if (normalizedExtracted.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return extractedPath;
            }

            Directory.CreateDirectory(normalizedTarget);

            foreach (var entry in Directory.EnumerateFileSystemEntries(normalizedExtracted))
            {
                var name = Path.GetFileName(entry);
                var destination = Path.Combine(normalizedTarget, name);
                try
                {
                    if (Directory.Exists(entry))
                    {
                        if (Directory.Exists(destination))
                        {
                            Directory.Delete(destination, recursive: true);
                        }

                        if (IsSameVolume(entry, destination))
                        {
                            Directory.Move(entry, destination);
                        }
                        else
                        {
                            CopyDirectory(entry, destination);
                            Directory.Delete(entry, recursive: true);
                        }
                    }
                    else
                    {
                        if (File.Exists(destination))
                        {
                            File.Delete(destination);
                        }

                        if (IsSameVolume(entry, destination))
                        {
                            File.Move(entry, destination);
                        }
                        else
                        {
                            File.Copy(entry, destination, overwrite: true);
                            File.Delete(entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Failed to relocate extracted entry '{entry}' -> '{destination}': {ex.Message}");
                }
            }

            try
            {
                Directory.Delete(normalizedExtracted, recursive: false);
            }
            catch (Exception ex)
            {
                logger?.Debug($"Extracted source cleanup skipped: {ex.Message}");
            }

            logger?.Info($"Relocated extracted content '{normalizedExtracted}' -> '{normalizedTarget}'.");
            return normalizedTarget;
        }

        public static string RelocateArchive(string archivePath, string targetDirectory, LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(targetDirectory))
            {
                return archivePath;
            }

            if (!File.Exists(archivePath))
            {
                return archivePath;
            }

            Directory.CreateDirectory(targetDirectory);
            var destination = Path.Combine(targetDirectory, Path.GetFileName(archivePath));
            if (string.Equals(Path.GetFullPath(archivePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                return archivePath;
            }

            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                if (IsSameVolume(archivePath, destination))
                {
                    File.Move(archivePath, destination);
                }
                else
                {
                    File.Copy(archivePath, destination, overwrite: true);
                    File.Delete(archivePath);
                }

                logger?.Info($"Relocated archive '{archivePath}' -> '{destination}'.");
                return destination;
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to relocate archive '{archivePath}' -> '{destination}': {ex.Message}");
                return archivePath;
            }
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

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
