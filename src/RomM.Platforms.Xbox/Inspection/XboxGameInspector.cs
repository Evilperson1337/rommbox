using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Xbox.Inspection
{
    public sealed class XboxGameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".xiso"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar"
        };

        private static readonly HashSet<string> UnsupportedContainerFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cci"
        };

        private static readonly Regex HexTitleIdRegex = new("[0-9A-Fa-f]{8}", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public XboxGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new XboxGameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No original Xbox source content available for inspection.";
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
                result.ErrorMessage = $"Original Xbox source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, XboxGameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = XboxContentFormat.Archive;
                result.Warnings.Add("Original Xbox archive detected. xemu does not support launching archives directly; extraction is required before launch artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing Xbox content.");
                logger?.Write(PlatformLogLevel.Info, "Extraction enabled: True");
                return;
            }

            if (UnsupportedContainerFormats.Contains(extension))
            {
                result.ErrorMessage = "Detected original Xbox CCI content, which is not supported for xemu launch.";
                result.Warnings.Add(result.ErrorMessage);
                logger?.Write(PlatformLogLevel.Warning, result.ErrorMessage);
                return;
            }

            if (!DirectFormats.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported original Xbox content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;
            logger?.Write(PlatformLogLevel.Info, $"Detected original Xbox content format: {candidate.Format}.");
            logger?.Write(PlatformLogLevel.Info, $"Detected candidate launch artifact: {candidate.Path}");
        }

        private static void AnalyzeDirectory(string rootPath, XboxGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = XboxContentFormat.Directory;
            result.RequiresExtraction = false;

            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var extension = NormalizeExtension(Path.GetExtension(file));
                if (UnsupportedContainerFormats.Contains(extension))
                {
                    result.Warnings.Add($"Ignoring unsupported Xbox container '{Path.GetFileName(file)}' (.cci is not supported for xemu launch).");
                    continue;
                }

                if (!DirectFormats.Contains(extension))
                {
                    continue;
                }

                result.CandidateArtifacts.Add(BuildCandidate(file, extension));
            }

            var hasDefaultXbe = files.Any(path => string.Equals(Path.GetFileName(path), "default.xbe", StringComparison.OrdinalIgnoreCase));
            if (hasDefaultXbe)
            {
                result.ContainsUnsupportedExtractedLayout = true;
                result.Warnings.Add("Detected extracted Xbox layout with default.xbe. xemu direct folder launch is not supported by this installer.");
                logger?.Write(PlatformLogLevel.Warning, "Detected extracted Xbox layout with no direct xemu launch support.");
            }
        }

        private static void FinalizeSelection(XboxGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var directCandidates = result.CandidateArtifacts
                .Where(candidate => candidate.IsDirectLaunchArtifact)
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directCandidates.Count == 0)
            {
                if (result.ContainsUnsupportedExtractedLayout)
                {
                    result.IsValid = false;
                    result.NormalizedFormat = XboxContentFormat.ExtractedLayout;
                    result.ErrorMessage = "Detected extracted Xbox layout with no direct xemu launch support.";
                    result.Warnings.Add("Install cannot continue for this format.");
                    logger?.Write(PlatformLogLevel.Warning, "Install cannot continue for this format.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to resolve original Xbox launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            var selected = directCandidates[0];
            result.IsValid = true;
            result.IsAmbiguous = directCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple Xbox launch candidates detected ({directCandidates.Count}). Selected '{selected.FileName}'.");
            }

            result.LaunchArtifactPath = selected.Path;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Version = selected.Version;

            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }
            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Xbox Title ID: {result.TitleId}");
            }
        }

        private static int CandidatePriority(XboxContentCandidate candidate)
        {
            return candidate.Format switch
            {
                XboxContentFormat.Xiso => 0,
                XboxContentFormat.Iso => 1,
                _ => 9
            };
        }

        private static XboxContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;

            return new XboxContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = XboxContentFormat.DiscImage,
                IsDirectLaunchArtifact = true,
                IsExtractedLayoutSignal = false,
                TitleId = ExtractTitleId(fileName),
                TitleName = InferTitleName(fileName),
                Version = ExtractVersion(fileName)
            };
        }

        private static XboxContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => XboxContentFormat.Iso,
                ".xiso" => XboxContentFormat.Xiso,
                ".cci" => XboxContentFormat.Unknown,
                ".zip" or ".7z" or ".rar" => XboxContentFormat.Archive,
                _ => XboxContentFormat.Unknown
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

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }
    }
}

