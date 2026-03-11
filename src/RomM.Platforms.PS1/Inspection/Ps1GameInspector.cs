using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PS1.Inspection
{
    public sealed class Ps1GameInspector
    {
        private static readonly HashSet<string> SingleFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".chd",
            ".iso",
            ".pbp"
        };

        public Ps1GameInspectionResult Inspect(string sourcePath, string? gameName, IPlatformLogger? logger)
        {
            var result = new Ps1GameInspectionResult
            {
                SourceRootPath = sourcePath ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.Warnings.Add("Source path missing.");
                return result;
            }

            var files = EnumerateFiles(sourcePath).ToList();
            if (files.Count == 0)
            {
                result.Warnings.Add("No PS1-compatible files found in source content.");
                return result;
            }

            var m3uFiles = files.Where(path => HasExtension(path, ".m3u")).OrderBy(path => path.Length).ToList();
            if (m3uFiles.Count > 0)
            {
                result.Format = Ps1ContentFormat.PlaylistM3u;
                result.LaunchFilePath = m3uFiles[0];
                result.OwnedFiles = files;
                result.DiscFiles = ResolveM3uDiscFiles(result.LaunchFilePath, logger);
                result.IsMultiDisc = result.DiscFiles.Count > 1;
                result.InstallFolderName = ResolveInstallFolderName(gameName, result.LaunchFilePath);
                logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation content format: {result.Format}.");
                return result;
            }

            var cueFiles = files.Where(path => HasExtension(path, ".cue")).OrderBy(path => path.Length).ToList();
            if (cueFiles.Count > 0)
            {
                result.Format = Ps1ContentFormat.CueBin;
                result.OwnedFiles = files;
                result.DiscFiles = cueFiles;
                result.IsMultiDisc = cueFiles.Count > 1;
                result.LaunchFilePath = cueFiles[0];
                result.InstallFolderName = ResolveInstallFolderName(gameName, cueFiles[0]);

                foreach (var cue in cueFiles)
                {
                    var referencedBins = ParseCueReferencedFiles(cue);
                    if (referencedBins.Count == 0)
                    {
                        result.Warnings.Add($"CUE file '{cue}' does not reference any disc files.");
                        continue;
                    }

                    foreach (var referenced in referencedBins)
                    {
                        if (!File.Exists(referenced))
                        {
                            result.Warnings.Add($"CUE companion file missing: '{referenced}'.");
                        }
                    }
                }

                logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation content format: {result.Format}. Discs={cueFiles.Count}.");
                return result;
            }

            var ccdFiles = files.Where(path => HasExtension(path, ".ccd")).OrderBy(path => path.Length).ToList();
            if (ccdFiles.Count > 0)
            {
                result.Format = Ps1ContentFormat.CcdImgSub;
                result.OwnedFiles = files;
                result.DiscFiles = ccdFiles;
                result.IsMultiDisc = ccdFiles.Count > 1;
                result.LaunchFilePath = ccdFiles[0];
                result.InstallFolderName = ResolveInstallFolderName(gameName, ccdFiles[0]);

                foreach (var ccd in ccdFiles)
                {
                    var baseName = Path.Combine(Path.GetDirectoryName(ccd) ?? string.Empty, Path.GetFileNameWithoutExtension(ccd));
                    var img = baseName + ".img";
                    if (!File.Exists(img))
                    {
                        result.Warnings.Add($"CCD companion IMG missing: '{img}'.");
                    }
                }

                logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation content format: {result.Format}. Discs={ccdFiles.Count}.");
                return result;
            }

            var singleFiles = files
                .Where(path => SingleFileExtensions.Contains(Path.GetExtension(path) ?? string.Empty))
                .OrderBy(path => path.Length)
                .ToList();
            if (singleFiles.Count > 0)
            {
                var launch = singleFiles[0];
                var extension = Path.GetExtension(launch) ?? string.Empty;
                result.Format = extension.Equals(".chd", StringComparison.OrdinalIgnoreCase)
                    ? Ps1ContentFormat.Chd
                    : extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                        ? Ps1ContentFormat.Iso
                        : Ps1ContentFormat.Pbp;
                result.LaunchFilePath = launch;
                result.OwnedFiles = new List<string> { launch };
                result.DiscFiles = new List<string> { launch };
                result.InstallFolderName = ResolveInstallFolderName(gameName, launch);
                logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation content format: {result.Format}.");
                return result;
            }

            result.Warnings.Add("No supported PS1 launch artifact detected.");
            return result;
        }

        private static IEnumerable<string> EnumerateFiles(string sourcePath)
        {
            if (File.Exists(sourcePath))
            {
                return new[] { sourcePath };
            }

            if (!Directory.Exists(sourcePath))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories);
        }

        private static bool HasExtension(string path, string extension)
        {
            return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveInstallFolderName(string? gameName, string path)
        {
            var fallback = Path.GetFileNameWithoutExtension(path) ?? "Game";
            var value = string.IsNullOrWhiteSpace(gameName) ? fallback : gameName;
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Game" : cleaned;
        }

        internal static List<string> ParseCueReferencedFiles(string cuePath)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(cuePath) || !File.Exists(cuePath))
            {
                return results;
            }

            var folder = Path.GetDirectoryName(cuePath) ?? string.Empty;
            foreach (var line in File.ReadAllLines(cuePath))
            {
                var match = Regex.Match(line, "FILE\\s+\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                var value = match.Groups["name"].Value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var combined = Path.Combine(folder, value);
                results.Add(combined);
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ResolveM3uDiscFiles(string m3uPath, IPlatformLogger? logger)
        {
            var discs = new List<string>();
            if (string.IsNullOrWhiteSpace(m3uPath) || !File.Exists(m3uPath))
            {
                return discs;
            }

            var folder = Path.GetDirectoryName(m3uPath) ?? string.Empty;
            foreach (var line in File.ReadAllLines(m3uPath))
            {
                var value = line?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var resolved = Path.IsPathRooted(value) ? value : Path.Combine(folder, value);
                discs.Add(resolved);
            }

            logger?.Write(PlatformLogLevel.Info, $"Resolved {discs.Count} PS1 disc entries from playlist '{m3uPath}'.");
            return discs;
        }
    }
}
