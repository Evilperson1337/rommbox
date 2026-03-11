using System;
using System.IO;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.DolphinInternal
{
    public static class DolphinInstallHelpers
    {
        public static string ResolveInstallRoot(string? installDirectory, string? romRootPath)
        {
            if (!string.IsNullOrWhiteSpace(romRootPath))
            {
                return romRootPath;
            }

            return installDirectory ?? string.Empty;
        }

        public static bool EnsureDirectoryWritable(string directory, string platformName, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = $"{platformName} install directory missing.";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
                using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Install directory not writable: {ex.Message}";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                logger?.Write(PlatformLogLevel.Warning, $"{platformName} install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        public static string BuildLaunchArguments(string? launchTemplate, string romPath)
        {
            var template = launchTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "{rom}";
            }

            return template.Replace("{rom}", QuoteArgument(romPath));
        }

        public static bool IsInstalledArtifactValid(string? installedPath, Func<string, bool> extensionPredicate)
        {
            if (string.IsNullOrWhiteSpace(installedPath) || !File.Exists(installedPath))
            {
                return false;
            }

            return extensionPredicate(Path.GetExtension(installedPath) ?? string.Empty);
        }

        public static void MoveOrReplace(string sourcePath, string targetPath, string platformName, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"{platformName} source file not found.", sourcePath);
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDirectory);
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, $"Source and destination are identical; no move needed: '{targetPath}'.");
                return;
            }

            if (File.Exists(targetPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Removing existing {platformName} artifact at '{targetPath}'.");
                File.Delete(targetPath);
            }

            var sourceRoot = Path.GetPathRoot(sourcePath.Trim()) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetPath.Trim()) ?? string.Empty;
            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                File.Delete(sourcePath);
            }
        }

        public static int DeleteInstalledArtifactOnly(string? installedPath, string platformName, IPlatformLogger? logger, System.Collections.Generic.List<string> notes)
        {
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                return 0;
            }

            try
            {
                if (File.Exists(installedPath))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Deleting installed content: {installedPath}");
                    File.Delete(installedPath);
                    return 1;
                }

                if (Directory.Exists(installedPath))
                {
                    notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for {platformName} uninstall.");
                    logger?.Write(PlatformLogLevel.Warning, $"{platformName} uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                }
            }
            catch (Exception ex)
            {
                notes.Add($"Failed to delete installed {platformName} content '{installedPath}': {ex.Message}");
                logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed {platformName} content '{installedPath}': {ex.Message}");
            }

            return 0;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }
    }
}

