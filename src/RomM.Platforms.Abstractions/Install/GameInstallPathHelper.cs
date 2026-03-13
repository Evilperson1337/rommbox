using System;
using System.IO;
using System.Linq;

namespace RomM.Platforms.Abstractions.Install
{
    public static class GameInstallPathHelper
    {
        public static string ResolvePlatformRoot(string installRoot)
        {
            return string.IsNullOrWhiteSpace(installRoot)
                ? string.Empty
                : Path.GetFullPath(installRoot);
        }

        public static string ResolveGameDirectory(string installRoot, params string?[] nameCandidates)
        {
            var platformRoot = ResolvePlatformRoot(installRoot);
            if (string.IsNullOrWhiteSpace(platformRoot))
            {
                return string.Empty;
            }

            var folderName = nameCandidates?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var normalized = NormalizePathSegment(folderName);
            return string.IsNullOrWhiteSpace(normalized)
                ? platformRoot
                : Path.Combine(platformRoot, normalized);
        }

        public static string ResolveTargetFilePath(string installRoot, string sourcePath, params string?[] nameCandidates)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var gameDirectory = ResolveGameDirectory(installRoot, nameCandidates);
            return string.IsNullOrWhiteSpace(gameDirectory)
                ? string.Empty
                : Path.Combine(gameDirectory, fileName);
        }

        public static string NormalizePathSegment(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalid, '_');
            }

            normalized = normalized.Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(normalized) ? "Game" : normalized;
        }

        public static bool IsPathUnderDirectory(string? candidatePath, string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            try
            {
                var candidate = Path.GetFullPath(candidatePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var directory = Path.GetFullPath(directoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(candidate, directory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
