using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Switch.Inspection
{
    public sealed class SwitchGameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".nsp",
            ".xci"
        };

        private static readonly HashSet<string> CompressedFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".nsz",
            ".xcz"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar"
        };

        private static readonly Regex TitleIdRegex = new("[0-9A-Fa-f]{16}", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SwitchGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new SwitchGameInspectionResult();

            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No Switch source content available for inspection.";
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
                result.ErrorMessage = $"Switch source path does not exist: '{sourcePath}'.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            FinalizeCandidateSelection(result, gameName, logger);
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

        private static void AnalyzeFile(string filePath, SwitchGameInspectionResult result, IPlatformLogger? logger)
        {
            var ext = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(ext);
            result.RequiresExtraction = ArchiveFormats.Contains(ext);

            if (ArchiveFormats.Contains(ext))
            {
                result.NormalizedFormat = SwitchContentFormat.Archive;
                result.Warnings.Add($"Switch archive detected '{ext}'. Extraction is required before final artifact selection.");
                logger?.Write(PlatformLogLevel.Info, $"Detected Switch content format: archive ({ext}).");
                return;
            }

            if (!IsSwitchPackageExtension(ext))
            {
                result.ErrorMessage = $"Unsupported Switch content extension '{ext}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, ext);
            AddCandidate(result, candidate);
            result.NormalizedFormat = candidate.RequiresDecompression
                ? (candidate.Format == SwitchContentFormat.Nsz ? SwitchContentFormat.Nsp : SwitchContentFormat.Xci)
                : candidate.Format;
            result.RequiresDecompression = candidate.RequiresDecompression;

            logger?.Write(PlatformLogLevel.Info, $"Detected Switch content format: {candidate.Format}.");
        }

        private static void AnalyzeDirectory(string rootPath, SwitchGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = SwitchContentFormat.Directory;
            result.RequiresExtraction = false;

            var candidates = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Where(path => IsSwitchPackageExtension(NormalizeExtension(Path.GetExtension(path))))
                .Select(path => BuildCandidate(path, NormalizeExtension(Path.GetExtension(path))))
                .ToList();

            foreach (var candidate in candidates)
            {
                AddCandidate(result, candidate);
            }

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "No Switch packages (.nsp/.xci/.nsz/.xcz) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {candidates.Count} Switch package candidate(s) in staged content.");
            if (candidates.Any(candidate => candidate.RequiresDecompression))
            {
                result.RequiresDecompression = true;
            }
        }

        private static void FinalizeCandidateSelection(SwitchGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var baseCandidates = result.BaseGameCandidates
                .OrderBy(candidate => CandidatePriority(candidate))
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var updateCount = result.UpdateCandidates.Count;
            var dlcCount = result.DlcCandidates.Count;
            if (updateCount > 0 || dlcCount > 0)
            {
                logger?.Write(PlatformLogLevel.Info, $"Detected {updateCount} update package(s) and {dlcCount} DLC package(s).");
            }

            if (baseCandidates.Count == 0)
            {
                if (result.UpdateCandidates.Count > 0 || result.DlcCandidates.Count > 0)
                {
                    result.ErrorMessage = "Only update/DLC content detected. A base NSP/XCI package is required for launch artifact resolution.";
                    result.Warnings.Add(result.ErrorMessage);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    return;
                }

                result.ErrorMessage = "Unable to resolve Switch launch artifact.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var selected = baseCandidates[0];
            result.IsAmbiguous = baseCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple base game candidates detected ({baseCandidates.Count}). Selected '{selected.FileName}'.");
            }

            result.IsValid = true;
            result.LaunchArtifactPath = selected.Path;
            result.ContentType = SwitchPackageType.BaseGame;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Version = selected.Version;
            result.NormalizedFormat = selected.RequiresDecompression
                ? (selected.Format == SwitchContentFormat.Nsz ? SwitchContentFormat.Nsp : SwitchContentFormat.Xci)
                : selected.Format;
            result.RequiresDecompression = selected.RequiresDecompression;

            logger?.Write(PlatformLogLevel.Info, $"Resolved Switch launch artifact: '{result.LaunchArtifactPath}'.");
            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Switch Title ID: {result.TitleId}.");
            }
            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Switch title: {result.TitleName}.");
            }
            if (!string.IsNullOrWhiteSpace(result.Version))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Switch version: {result.Version}.");
            }
        }

        private static int CandidatePriority(SwitchContentCandidate candidate)
        {
            var formatPriority = candidate.Format switch
            {
                SwitchContentFormat.Nsp => 0,
                SwitchContentFormat.Xci => 1,
                SwitchContentFormat.Nsz => 2,
                SwitchContentFormat.Xcz => 3,
                _ => 9
            };

            return (candidate.RequiresDecompression ? 10 : 0) + formatPriority;
        }

        private static void AddCandidate(SwitchGameInspectionResult result, SwitchContentCandidate candidate)
        {
            result.AllCandidates.Add(candidate);
            switch (candidate.PackageType)
            {
                case SwitchPackageType.Update:
                    result.UpdateCandidates.Add(candidate);
                    break;
                case SwitchPackageType.Dlc:
                    result.DlcCandidates.Add(candidate);
                    break;
                default:
                    result.BaseGameCandidates.Add(candidate);
                    break;
            }
        }

        private static SwitchContentCandidate BuildCandidate(string path, string extension)
        {
            var fileName = Path.GetFileName(path) ?? string.Empty;
            var lowerName = fileName.ToLowerInvariant();

            var titleId = ExtractTitleId(fileName);
            var version = ExtractVersion(fileName);
            var packageType = InferPackageType(lowerName);
            var format = MapFormat(extension);
            var requiresDecompression = CompressedFormats.Contains(extension);

            var candidate = new SwitchContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                PackageType = packageType,
                RequiresDecompression = requiresDecompression,
                NormalizedExtension = extension,
                TitleId = titleId,
                Version = version,
                TitleName = InferTitleName(fileName)
            };

            return candidate;
        }

        private static SwitchPackageType InferPackageType(string lowerFileName)
        {
            if (lowerFileName.Contains("dlc", StringComparison.Ordinal)
                || lowerFileName.Contains("add-on", StringComparison.Ordinal)
                || lowerFileName.Contains("addon", StringComparison.Ordinal))
            {
                return SwitchPackageType.Dlc;
            }

            if (lowerFileName.Contains("update", StringComparison.Ordinal)
                || lowerFileName.Contains("patch", StringComparison.Ordinal)
                || lowerFileName.Contains("upd", StringComparison.Ordinal))
            {
                return SwitchPackageType.Update;
            }

            return SwitchPackageType.BaseGame;
        }

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }

        private static string ExtractTitleId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = TitleIdRegex.Match(text);
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
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

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static bool IsSwitchPackageExtension(string extension)
        {
            return DirectFormats.Contains(extension) || CompressedFormats.Contains(extension);
        }

        private static SwitchContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".nsp" => SwitchContentFormat.Nsp,
                ".xci" => SwitchContentFormat.Xci,
                ".nsz" => SwitchContentFormat.Nsz,
                ".xcz" => SwitchContentFormat.Xcz,
                ".zip" or ".7z" or ".rar" => SwitchContentFormat.Archive,
                _ => SwitchContentFormat.Unknown
            };
        }
    }
}

