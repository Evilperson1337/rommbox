using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PSP.Inspection
{
    public sealed class PspGameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".cso",
            ".chd"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar",
            ".gz"
        };

        private static readonly Regex GameIdRegex = new(@"\b([A-Za-z]{4}[-_ ]?\d{5})\b", RegexOptions.Compiled);
        private static readonly Regex RegionTokenRegex = new(@"\b(USA|EUR|JPN|JAP|PAL|NTSC|NTSCU|NTSCJ)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RevisionRegex = new(@"\b(?:rev(?:ision)?|v(?:er(?:sion)?)?)\s*([0-9]+(?:\.[0-9]+)*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParamDiscIdRegex = new(@"DISC_ID[\x00-\x20:=]+([A-Za-z]{4}[-_ ]?\d{5})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParamTitleRegex = new(@"TITLE[\x00-\x20:=]+([^\x00\r\n]{2,120})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public PspGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new PspGameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No PlayStation Portable source content available for inspection.";
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
                result.ErrorMessage = $"PlayStation Portable source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, PspGameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = PspContentFormat.Archive;
                result.Warnings.Add("PlayStation Portable archive detected. Extraction is required before launch artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing PSP content");
                logger?.Write(PlatformLogLevel.Info, "Extraction enabled: True");
                return;
            }

            if (!DirectFormats.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported PlayStation Portable content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;

            logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation Portable content format: {candidate.Format}");
            logger?.Write(PlatformLogLevel.Info, $"Detected candidate launch artifact: {candidate.Path}");
        }

        private static void AnalyzeDirectory(string rootPath, PspGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = PspContentFormat.Directory;
            result.RequiresExtraction = false;

            var candidates = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => (Path: path, Extension: NormalizeExtension(Path.GetExtension(path))))
                .Where(item => DirectFormats.Contains(item.Extension))
                .Select(item => BuildCandidate(item.Path, item.Extension))
                .ToList();

            foreach (var candidate in candidates)
            {
                result.CandidateArtifacts.Add(candidate);
            }

            ApplyParamSfoMetadata(rootPath, result, logger);

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "No PSP launch artifacts (.iso/.cso/.chd) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {candidates.Count} PSP candidate artifact(s) in staged content.");
        }

        private static void FinalizeSelection(PspGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var directCandidates = result.CandidateArtifacts
                .Where(candidate => candidate.IsDirectLaunchArtifact)
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directCandidates.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to resolve PlayStation Portable launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            var selected = directCandidates[0];
            result.IsValid = true;
            result.IsAmbiguous = directCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple PSP game candidates detected ({directCandidates.Count}). Selected '{selected.FileName}'.");
                logger?.Write(PlatformLogLevel.Warning, "Multiple PSP game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {selected.Path}");
            }

            result.LaunchArtifactPath = selected.Path;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.GameId = selected.GameId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Revision = selected.Revision;
            result.Region = selected.Region;

            if (!string.IsNullOrWhiteSpace(result.GameId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Game ID: {result.GameId}");
            }

            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }
        }

        private static int CandidatePriority(PspContentCandidate candidate)
        {
            return candidate.Format switch
            {
                PspContentFormat.Chd => 0,
                PspContentFormat.Iso => 1,
                PspContentFormat.Cso => 2,
                _ => 9
            };
        }

        private static PspContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;

            return new PspContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = PspContentFormat.DiscImage,
                IsDirectLaunchArtifact = true,
                GameId = ExtractGameId(fileName),
                TitleName = InferTitleName(fileName),
                Revision = ExtractRevision(fileName),
                Region = ExtractRegion(fileName)
            };
        }

        private static PspContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => PspContentFormat.Iso,
                ".cso" => PspContentFormat.Cso,
                ".chd" => PspContentFormat.Chd,
                ".zip" or ".7z" or ".rar" or ".gz" => PspContentFormat.Archive,
                _ => PspContentFormat.Unknown
            };
        }

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ExtractGameId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = GameIdRegex.Match(text);
            if (!match.Success)
            {
                return string.Empty;
            }

            var raw = match.Groups[1].Value.ToUpperInvariant().Replace("_", "-").Replace(" ", string.Empty);
            var compact = raw.Replace("-", string.Empty);
            if (compact.Length < 9)
            {
                return raw;
            }

            var prefix = compact.Substring(0, 4);
            var suffix = compact.Substring(4);
            return $"{prefix}-{suffix}";
        }

        private static string ExtractRevision(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = RevisionRegex.Match(text);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractRegion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = RegionTokenRegex.Match(text);
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

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }

        private static void ApplyParamSfoMetadata(string rootPath, PspGameInspectionResult result, IPlatformLogger? logger)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                {
                    return;
                }

                var sfoPath = Directory
                    .EnumerateFiles(rootPath, "PARAM.SFO", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(sfoPath) || !File.Exists(sfoPath))
                {
                    return;
                }

                var bytes = File.ReadAllBytes(sfoPath);
                if (bytes.Length == 0)
                {
                    return;
                }

                var text = Encoding.UTF8.GetString(bytes);
                var idMatch = ParamDiscIdRegex.Match(text);
                var titleMatch = ParamTitleRegex.Match(text);
                var parsedId = idMatch.Success ? ExtractGameId(idMatch.Groups[1].Value) : string.Empty;
                var parsedTitle = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(parsedId) && string.IsNullOrWhiteSpace(parsedTitle))
                {
                    return;
                }

                for (var i = 0; i < result.CandidateArtifacts.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(result.CandidateArtifacts[i].GameId) && !string.IsNullOrWhiteSpace(parsedId))
                    {
                        result.CandidateArtifacts[i].GameId = parsedId;
                    }

                    if (string.IsNullOrWhiteSpace(result.CandidateArtifacts[i].TitleName) && !string.IsNullOrWhiteSpace(parsedTitle))
                    {
                        result.CandidateArtifacts[i].TitleName = parsedTitle;
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsedId))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Parsed Game ID from PARAM.SFO: {parsedId}");
                }

                if (!string.IsNullOrWhiteSpace(parsedTitle))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Parsed Title from PARAM.SFO: {parsedTitle}");
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to parse PARAM.SFO for PSP metadata: {ex.Message}");
            }
        }
    }
}

