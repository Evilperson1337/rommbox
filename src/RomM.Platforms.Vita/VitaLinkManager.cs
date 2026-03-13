using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Vita
{
    internal static class VitaLinkManager
    {
        public static void EnsureDirectoryLink(string linkPath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new InvalidOperationException("Vita link path and target path are required.");
            }

            var normalizedLinkPath = Path.GetFullPath(linkPath);
            var normalizedTargetPath = Path.GetFullPath(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(normalizedLinkPath) ?? string.Empty);
            Directory.CreateDirectory(normalizedTargetPath);

            if (TryGetLinkTarget(normalizedLinkPath, out var existingTarget)
                && string.Equals(existingTarget, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, $"Vita link already points to canonical target: '{normalizedLinkPath}' -> '{normalizedTargetPath}'.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(existingTarget))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Vita link points to wrong target and will be replaced: '{normalizedLinkPath}' -> '{existingTarget}', expected '{normalizedTargetPath}'.");
                DeletePath(normalizedLinkPath, logger);
            }
            else if (Directory.Exists(normalizedLinkPath))
            {
                throw new InvalidOperationException($"Expected Vita link path '{normalizedLinkPath}' to be a link, but a real directory exists there.");
            }
            else if (File.Exists(normalizedLinkPath))
            {
                throw new InvalidOperationException($"Expected Vita link path '{normalizedLinkPath}' to be a directory link, but a file exists there.");
            }

            CreateDirectoryLinkInternal(normalizedLinkPath, normalizedTargetPath, logger);
        }

        public static bool TryGetLinkTarget(string path, out string targetPath)
        {
            targetPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var directoryInfo = new DirectoryInfo(path);
                if (!directoryInfo.Exists)
                {
                    return false;
                }

                if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return false;
                }

                var resolved = directoryInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved == null)
                {
                    return false;
                }

                targetPath = Path.GetFullPath(resolved.FullName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void DeletePath(string path, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (Directory.Exists(path))
            {
                logger?.Write(PlatformLogLevel.Info, $"Deleting Vita directory entry: {path}");
                Directory.Delete(path, recursive: true);
                return;
            }

            if (File.Exists(path))
            {
                logger?.Write(PlatformLogLevel.Info, $"Deleting Vita file entry: {path}");
                File.Delete(path);
            }
        }

        private static void CreateDirectoryLinkInternal(string linkPath, string targetPath, IPlatformLogger? logger)
        {
            logger?.Write(PlatformLogLevel.Info, $"Creating Vita directory link: '{linkPath}' -> '{targetPath}'.");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(linkPath) ?? string.Empty
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start junction creation process.");
            }

            process.WaitForExit(10000);
            if (!process.HasExited || process.ExitCode != 0)
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"Failed to create Vita junction. ExitCode={process.ExitCode}. Output='{output}'. Error='{error}'.");
            }
        }
    }
}
