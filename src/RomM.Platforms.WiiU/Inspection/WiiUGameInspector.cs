using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.WiiU.Inspection
{
    public sealed class WiiUGameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".wud",
            ".wux",
            ".wua",
            ".rpx"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar"
        };

        private static readonly Regex TitleIdRegex = new("[0-9A-Fa-f]{16}", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public WiiUGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new WiiUGameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No Wii U source content available for inspection.";
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
                result.ErrorMessage = $"Wii U source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, WiiUGameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = WiiUContentFormat.Archive;
                result.Warnings.Add($"Detected archive containing Wii U content ('{extension}'). Extraction is required before final artifact selection.");
                logger?.Write(PlatformLogLevel.Info, $"Detected Nintendo Wii U content format: archive ({extension}).");
                return;
            }

            if (!DirectFormats.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported Wii U content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildDirectFileCandidate(filePath, extension);
            AddCandidate(result, candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;

            logger?.Write(PlatformLogLevel.Info, $"Detected Nintendo Wii U content format: {candidate.Format}.");
        }

        private static void AnalyzeDirectory(string rootPath, WiiUGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = WiiUContentFormat.Directory;
            result.RequiresExtraction = false;

            AddExtractedLayoutCandidates(rootPath, result);

            var files = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var file in files)
            {
                var extension = NormalizeExtension(Path.GetExtension(file));
                if (!DirectFormats.Contains(extension))
                {
                    continue;
                }

                if (extension.Equals(".rpx", StringComparison.OrdinalIgnoreCase)
                    && IsInsideExtractedCodeFolder(file))
                {
                    continue;
                }

                AddCandidate(result, BuildDirectFileCandidate(file, extension));
            }

            if (result.AllCandidates.Count == 0)
            {
                result.ErrorMessage = "No Wii U launch artifacts (.wud/.wux/.wua/.rpx or code/content/meta layout) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {result.AllCandidates.Count} Wii U candidate artifact(s) in staged content.");
        }

        private static void AddExtractedLayoutCandidates(string rootPath, WiiUGameInspectionResult result)
        {
            var rootCandidates = new List<string> { rootPath };
            rootCandidates.AddRange(Directory
                .EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

            foreach (var candidateRoot in rootCandidates)
            {
                var codeDir = Path.Combine(candidateRoot, "code");
                var contentDir = Path.Combine(candidateRoot, "content");
                var metaDir = Path.Combine(candidateRoot, "meta");
                if (!Directory.Exists(codeDir) || !Directory.Exists(contentDir) || !Directory.Exists(metaDir))
                {
                    continue;
                }

                var rpxFiles = Directory
                    .EnumerateFiles(codeDir, "*.rpx", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rpxFiles.Count == 0)
                {
                    continue;
                }

                var launchRpx = rpxFiles[0];
                var folderName = Path.GetFileName(candidateRoot) ?? string.Empty;
                var titleId = ExtractTitleId(folderName);
                var candidate = new WiiUContentCandidate
                {
                    Path = candidateRoot,
                    FileName = folderName,
                    Format = WiiUContentFormat.ExtractedLayout,
                    NormalizedFormat = WiiUContentFormat.Rpx,
                    Role = InferRoleFromName(folderName),
                    IsDirectLaunchArtifact = false,
                    LaunchArtifactPath = launchRpx,
                    TitleId = titleId,
                    TitleName = InferTitleNameFromPath(candidateRoot),
                    Version = ExtractVersion(folderName),
                    Region = InferRegion(titleId)
                };

                AddCandidate(result, candidate);
            }
        }

        private static bool IsInsideExtractedCodeFolder(string file)
        {
            var parent = Path.GetDirectoryName(file) ?? string.Empty;
            return string.Equals(Path.GetFileName(parent), "code", StringComparison.OrdinalIgnoreCase);
        }

        private static WiiUContentCandidate BuildDirectFileCandidate(string filePath, string extension)
        {
            var fileName = Path.GetFileName(filePath) ?? string.Empty;
            var titleId = ExtractTitleId(fileName);
            return new WiiUContentCandidate
            {
                Path = filePath,
                FileName = fileName,
                Format = MapFormat(extension),
                NormalizedFormat = MapFormat(extension),
                Role = InferRoleFromName(fileName),
                IsDirectLaunchArtifact = true,
                LaunchArtifactPath = filePath,
                TitleId = titleId,
                TitleName = InferTitleNameFromPath(filePath),
                Version = ExtractVersion(fileName),
                Region = InferRegion(titleId)
            };
        }

        private static void FinalizeCandidateSelection(WiiUGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var baseCandidates = result.BaseGameCandidates
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.LaunchArtifactPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.UpdateCandidates.Count > 0 || result.DlcCandidates.Count > 0)
            {
                logger?.Write(PlatformLogLevel.Info, $"Detected update content: {result.UpdateCandidates.Count}, DLC content: {result.DlcCandidates.Count}.");
                logger?.Write(PlatformLogLevel.Warning, "Automatic Cemu update/DLC installation is not implemented yet. Content is detected and logged.");
            }

            if (baseCandidates.Count == 0)
            {
                if (result.UpdateCandidates.Count > 0 || result.DlcCandidates.Count > 0)
                {
                    result.ErrorMessage = "Only Wii U update/DLC content detected. A base game artifact is required for launch artifact resolution.";
                    result.Warnings.Add(result.ErrorMessage);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    return;
                }

                result.ErrorMessage = "Unable to determine canonical Wii U launch artifact safely.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var selected = baseCandidates[0];
            result.IsAmbiguous = baseCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple Wii U game candidates detected ({baseCandidates.Count}). Selected '{selected.LaunchArtifactPath}'.");
            }

            result.IsValid = true;
            result.InstallSourcePath = selected.Path;
            result.LaunchArtifactPath = selected.LaunchArtifactPath;
            result.ContentRole = WiiUContentRole.BaseGame;
            result.SelectedFormat = selected.Format;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Version = selected.Version;
            result.Region = selected.Region;

            logger?.Write(PlatformLogLevel.Info, $"Detected candidate launch artifact: {result.LaunchArtifactPath}");
            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title ID: {result.TitleId}");
            }
            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }
            if (!string.IsNullOrWhiteSpace(result.Version))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Version: {result.Version}");
            }

            if (result.IsAmbiguous)
            {
                logger?.Write(PlatformLogLevel.Warning, "Multiple Wii U game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {result.LaunchArtifactPath}");
            }
        }

        private static int CandidatePriority(WiiUContentCandidate candidate)
        {
            return candidate.Format switch
            {
                WiiUContentFormat.Wua => 0,
                WiiUContentFormat.ExtractedLayout => 1,
                WiiUContentFormat.Wux => 2,
                WiiUContentFormat.Wud => 3,
                WiiUContentFormat.Rpx => 4,
                _ => 9
            };
        }

        private static void AddCandidate(WiiUGameInspectionResult result, WiiUContentCandidate candidate)
        {
            result.AllCandidates.Add(candidate);
            switch (candidate.Role)
            {
                case WiiUContentRole.Update:
                    result.UpdateCandidates.Add(candidate);
                    break;
                case WiiUContentRole.Dlc:
                    result.DlcCandidates.Add(candidate);
                    break;
                default:
                    result.BaseGameCandidates.Add(candidate);
                    break;
            }
        }

        private static WiiUContentRole InferRoleFromName(string value)
        {
            var lower = (value ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("dlc", StringComparison.Ordinal)
                || lower.Contains("add-on", StringComparison.Ordinal)
                || lower.Contains("addon", StringComparison.Ordinal)
                || lower.Contains("aoc", StringComparison.Ordinal))
            {
                return WiiUContentRole.Dlc;
            }

            if (lower.Contains("update", StringComparison.Ordinal)
                || lower.Contains("patch", StringComparison.Ordinal)
                || lower.Contains("upd", StringComparison.Ordinal))
            {
                return WiiUContentRole.Update;
            }

            return WiiUContentRole.BaseGame;
        }

        private static string InferTitleNameFromPath(string path)
        {
            var name = Directory.Exists(path)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);
            return (name ?? string.Empty).Replace('_', ' ').Trim();
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

        private static string InferRegion(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || titleId.Length < 12)
            {
                return string.Empty;
            }

            var region = char.ToUpperInvariant(titleId[11]);
            return region switch
            {
                'E' => "NTSC-U",
                'J' => "NTSC-J",
                'P' => "PAL",
                'K' => "NTSC-K",
                _ => string.Empty
            };
        }

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static WiiUContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".wud" => WiiUContentFormat.Wud,
                ".wux" => WiiUContentFormat.Wux,
                ".wua" => WiiUContentFormat.Wua,
                ".rpx" => WiiUContentFormat.Rpx,
                ".zip" or ".7z" or ".rar" => WiiUContentFormat.Archive,
                _ => WiiUContentFormat.Unknown
            };
        }
    }
}

