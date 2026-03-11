using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Xbox360.Inspection
{
    public sealed class Xbox360GameInspector
    {
        private static readonly HashSet<string> IsoFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso"
        };

        private static readonly HashSet<string> XexFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xex"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar"
        };

        private static readonly Regex HexTitleIdRegex = new(@"(?:^|[^0-9A-Fa-f])([0-9A-Fa-f]{8})(?=[^0-9A-Fa-f]|$)", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RegionRegex = new(@"(?:^|[^A-Za-z0-9])(USA|EUR|JPN|JAP|PAL|NTSC|NTSCU|NTSCJ)(?=[^A-Za-z0-9]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MediaIdRegex = new(@"(?:mediaid|mid)[^0-9A-Fa-f]*([0-9A-Fa-f]{8})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Xbox360GameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new Xbox360GameInspectionResult
            {
                GodLayoutSupported = false
            };

            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No Xbox 360 source content available for inspection.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            result.SourcePath = sourcePath;
            result.StagedContentRoot = Directory.Exists(sourcePath)
                ? sourcePath
                : Path.GetDirectoryName(sourcePath) ?? string.Empty;

            if (File.Exists(sourcePath))
            {
                AnalyzeFile(sourcePath, result, logger);
            }
            else if (Directory.Exists(sourcePath))
            {
                AnalyzeDirectory(sourcePath, result, logger);
            }
            else
            {
                result.ErrorMessage = $"Xbox 360 source path does not exist: '{sourcePath}'.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            FinalizeSelection(result, gameName, logger);
            return result;
        }

        private static string ResolveSourcePath(string? archivePath, string? extractedPath)
        {
            if (!string.IsNullOrWhiteSpace(extractedPath) && (Directory.Exists(extractedPath) || File.Exists(extractedPath)))
            {
                return extractedPath;
            }

            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                return archivePath;
            }

            return string.Empty;
        }

        private static void AnalyzeFile(string filePath, Xbox360GameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = Xbox360ContentFormat.Archive;
                result.Warnings.Add("Detected archive containing Xbox 360 content. Extraction is required before launch artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing Xbox 360 content");
                logger?.Write(PlatformLogLevel.Info, "Extraction enabled: True");
                return;
            }

            if (!IsoFormats.Contains(extension) && !XexFormats.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported Xbox 360 content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;

            logger?.Write(PlatformLogLevel.Info, $"Detected Xbox 360 content format: {candidate.Format}");
            logger?.Write(PlatformLogLevel.Info, $"Detected candidate launch artifact: {candidate.Path}");
        }

        private static void AnalyzeDirectory(string rootPath, Xbox360GameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = Xbox360ContentFormat.Directory;
            result.RequiresExtraction = false;

            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var extension = NormalizeExtension(Path.GetExtension(file));
                if (!IsoFormats.Contains(extension) && !XexFormats.Contains(extension))
                {
                    continue;
                }

                result.CandidateArtifacts.Add(BuildCandidate(file, extension));
            }

            var hasDefaultXex = files.Any(path => string.Equals(Path.GetFileName(path), "default.xex", StringComparison.OrdinalIgnoreCase));
            if (hasDefaultXex)
            {
                result.ContainsExtractedLayout = true;
                result.Warnings.Add("Detected extracted Xbox 360 layout with default.xex.");
                logger?.Write(PlatformLogLevel.Info, "Detected extracted Xbox 360 layout with default.xex");
            }

            if (LooksLikeGodLayout(rootPath, files))
            {
                result.ContainsGodLayout = true;
                result.Warnings.Add("Detected Xbox 360 GOD/content layout but no supported install strategy is configured.");
                logger?.Write(PlatformLogLevel.Warning, "Detected Xbox 360 GOD/content layout but no supported install strategy is configured");
            }
        }

        private static void FinalizeSelection(Xbox360GameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var directCandidates = result.CandidateArtifacts
                .Where(candidate => candidate.IsDirectLaunchArtifact)
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directCandidates.Count == 0)
            {
                if (result.ContainsGodLayout && !result.GodLayoutSupported)
                {
                    result.IsValid = false;
                    result.NormalizedFormat = Xbox360ContentFormat.GodLayout;
                    result.ErrorMessage = "Detected Xbox 360 GOD/content layout but no supported install strategy is configured.";
                    result.Warnings.Add("Install cannot continue safely.");
                    logger?.Write(PlatformLogLevel.Warning, "Install cannot continue safely");
                    return;
                }

                if (result.ContainsExtractedLayout)
                {
                    result.IsValid = false;
                    result.NormalizedFormat = Xbox360ContentFormat.ExtractedLayout;
                    result.ErrorMessage = "Unable to determine canonical launch artifact safely from extracted Xbox 360 layout.";
                    result.Warnings.Add("Install cannot continue safely.");
                    logger?.Write(PlatformLogLevel.Warning, "Unable to determine canonical launch artifact safely");
                    return;
                }

                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to resolve Xbox 360 launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            var selected = directCandidates[0];
            result.IsValid = true;
            result.IsAmbiguous = directCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple Xbox 360 game candidates detected ({directCandidates.Count}). Selected '{selected.FileName}'.");
                logger?.Write(PlatformLogLevel.Warning, "Multiple Xbox 360 game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {selected.Path}");
            }

            result.LaunchArtifactPath = selected.Path;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Version = selected.Version;
            result.Region = selected.Region;
            result.MediaId = selected.MediaId;

            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title ID: {result.TitleId}");
            }

            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }

            if (!string.IsNullOrWhiteSpace(result.MediaId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Media ID: {result.MediaId}");
            }
        }

        private static int CandidatePriority(Xbox360ContentCandidate candidate)
        {
            return candidate.Format switch
            {
                Xbox360ContentFormat.Iso => 0,
                Xbox360ContentFormat.Xex => 1,
                _ => 9
            };
        }

        private static Xbox360ContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;
            var normalized = format == Xbox360ContentFormat.Xex
                ? Xbox360ContentFormat.Executable
                : Xbox360ContentFormat.DiscImage;

            return new Xbox360ContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = normalized,
                IsDirectLaunchArtifact = true,
                IsExtractedLayoutSignal = string.Equals(fileName, "default.xex", StringComparison.OrdinalIgnoreCase),
                IsGodLayoutSignal = false,
                TitleId = ExtractTitleId(fileName),
                TitleName = InferTitleName(fileName),
                Version = ExtractVersion(fileName),
                Region = ExtractRegion(fileName),
                MediaId = ExtractMediaId(fileName)
            };
        }

        private static bool LooksLikeGodLayout(string rootPath, IReadOnlyCollection<string> files)
        {
            var hasContentDirectory = Directory.Exists(Path.Combine(rootPath, "Content"));
            if (!hasContentDirectory)
            {
                return false;
            }

            var hasNumericContainerRoot = files.Any(path =>
            {
                var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                return normalized.IndexOf($"{Path.DirectorySeparatorChar}0000000000000000{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0;
            });

            return hasNumericContainerRoot;
        }

        private static Xbox360ContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => Xbox360ContentFormat.Iso,
                ".xex" => Xbox360ContentFormat.Xex,
                ".zip" or ".7z" or ".rar" => Xbox360ContentFormat.Archive,
                _ => Xbox360ContentFormat.Unknown
            };
        }

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ExtractTitleId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = HexTitleIdRegex.Match(text);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
        }

        private static string ExtractVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = VersionRegex.Match(text);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractRegion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = RegionRegex.Match(text);
            if (!match.Success)
            {
                return string.Empty;
            }

            var token = match.Groups[1].Value.ToUpperInvariant();
            return token switch
            {
                "NTSCU" => "USA",
                "NTSCJ" => "JPN",
                "JAP" => "JPN",
                _ => token
            };
        }

        private static string ExtractMediaId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = MediaIdRegex.Match(text);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
        }

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }
    }
}

