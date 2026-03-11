using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PS2.Inspection
{
    public sealed class Ps2GameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".chd",
            ".cso",
            ".zso",
            ".bin"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar",
            ".gz"
        };

        private static readonly HashSet<string> UnsupportedDiscFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mdf",
            ".nrg"
        };

        private static readonly Regex DiscIdRegex = new(@"\b([A-Za-z]{4}[-_ ]?(?:\d{5}|\d{3}\.\d{2}))\b", RegexOptions.Compiled);
        private static readonly Regex RegionTokenRegex = new(@"\b(USA|EUR|JPN|JAP|PAL|NTSC|NTSCU|NTSCJ)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RevisionRegex = new(@"\b(?:rev(?:ision)?|v(?:er(?:sion)?)?)\s*([0-9]+(?:\.[0-9]+)*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SystemCnfBootRegex = new(@"BOOT\d?\s*=\s*[^\\]*\\\s*([A-Za-z]{4}[_-]?\d{3}\.\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Ps2GameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new Ps2GameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No PlayStation 2 source content available for inspection.";
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
                result.ErrorMessage = $"PlayStation 2 source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, Ps2GameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = Ps2ContentFormat.Archive;
                result.Warnings.Add("PlayStation 2 archive detected. Extraction is required before launch artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing PS2 content");
                logger?.Write(PlatformLogLevel.Info, "Extraction enabled: True");
                return;
            }

            if (!DirectFormats.Contains(extension))
            {
                if (UnsupportedDiscFormats.Contains(extension))
                {
                    result.ErrorMessage = $"Unsupported PlayStation 2 content extension '{extension}'. Convert to a PCSX2-supported launch format (.iso/.chd/.cso/.zso/.bin).";
                }
                else
                {
                    result.ErrorMessage = $"Unsupported PlayStation 2 content extension '{extension}'.";
                }

                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;

            logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation 2 content format: {candidate.Format}");
            logger?.Write(PlatformLogLevel.Info, $"Detected candidate launch artifact: {candidate.Path}");
        }

        private static void AnalyzeDirectory(string rootPath, Ps2GameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = Ps2ContentFormat.Directory;
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

            var systemCnfDiscId = ExtractDiscIdFromSystemCnf(rootPath, logger);
            if (!string.IsNullOrWhiteSpace(systemCnfDiscId))
            {
                for (var i = 0; i < result.CandidateArtifacts.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(result.CandidateArtifacts[i].DiscId))
                    {
                        result.CandidateArtifacts[i].DiscId = systemCnfDiscId;
                    }
                }

                logger?.Write(PlatformLogLevel.Info, $"Parsed Disc ID from SYSTEM.CNF: {systemCnfDiscId}");
            }

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "No PS2 launch artifacts (.iso/.chd/.cso/.zso/.bin) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {candidates.Count} PS2 candidate artifact(s) in staged content.");
        }

        private static void FinalizeSelection(Ps2GameInspectionResult result, string? gameName, IPlatformLogger? logger)
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
                    result.ErrorMessage = "Unable to resolve PlayStation 2 launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            var selected = directCandidates[0];
            result.IsValid = true;
            result.IsAmbiguous = directCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple PS2 game candidates detected ({directCandidates.Count}). Selected '{selected.FileName}'.");
                logger?.Write(PlatformLogLevel.Warning, "Multiple PS2 game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {selected.Path}");
            }

            result.LaunchArtifactPath = selected.Path;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.DiscId = selected.DiscId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Revision = selected.Revision;
            result.Region = selected.Region;

            if (!string.IsNullOrWhiteSpace(result.DiscId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Disc ID: {result.DiscId}");
            }

            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }
        }

        private static int CandidatePriority(Ps2ContentCandidate candidate)
        {
            return candidate.Format switch
            {
                Ps2ContentFormat.Chd => 0,
                Ps2ContentFormat.Iso => 1,
                Ps2ContentFormat.Cso => 2,
                Ps2ContentFormat.Zso => 3,
                Ps2ContentFormat.Bin => 4,
                _ => 9
            };
        }

        private static Ps2ContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;

            return new Ps2ContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = Ps2ContentFormat.DiscImage,
                IsDirectLaunchArtifact = true,
                DiscId = ExtractDiscId(fileName),
                TitleName = InferTitleName(fileName),
                Revision = ExtractRevision(fileName),
                Region = ExtractRegion(fileName)
            };
        }

        private static Ps2ContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => Ps2ContentFormat.Iso,
                ".chd" => Ps2ContentFormat.Chd,
                ".cso" => Ps2ContentFormat.Cso,
                ".zso" => Ps2ContentFormat.Zso,
                ".bin" => Ps2ContentFormat.Bin,
                ".zip" or ".7z" or ".rar" or ".gz" => Ps2ContentFormat.Archive,
                _ => Ps2ContentFormat.Unknown
            };
        }

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ExtractDiscId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = DiscIdRegex.Match(text);
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

        private static string ExtractDiscIdFromSystemCnf(string rootPath, IPlatformLogger? logger)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                {
                    return string.Empty;
                }

                var systemCnfFiles = Directory
                    .EnumerateFiles(rootPath, "SYSTEM.CNF", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var systemCnf in systemCnfFiles)
                {
                    var lines = File.ReadAllLines(systemCnf);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var match = SystemCnfBootRegex.Match(line);
                        if (!match.Success)
                        {
                            continue;
                        }

                        var token = match.Groups[1].Value.ToUpperInvariant().Replace("_", "-").Replace(" ", string.Empty);
                        var discId = ExtractDiscId(token);
                        if (!string.IsNullOrWhiteSpace(discId))
                        {
                            return discId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to parse SYSTEM.CNF for PS2 metadata: {ex.Message}");
            }

            return string.Empty;
        }
    }
}

