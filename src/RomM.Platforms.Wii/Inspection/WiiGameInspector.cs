using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.DolphinInternal;

namespace RomM.Platforms.Wii.Inspection
{
    public sealed class WiiGameInspector
    {
        private static readonly string[] DirectFormats = { ".iso", ".wbfs", ".gcz", ".ciso", ".wia", ".rvz" };
        private static readonly string[] ArchiveFormats = { ".zip", ".7z", ".rar" };

        private static readonly Regex TitleIdRegex = new("[A-Z0-9]{6}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RevisionRegex = new(@"(?:rev|r)[\s_\.-]*(\d{1,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public WiiGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new WiiGameInspectionResult();
            var policy = new DolphinPlatformPolicy
            {
                SupportedExtensions = DirectFormats,
                ArchiveExtensions = ArchiveFormats,
                PreferredExtensions = new[] { ".rvz", ".wbfs", ".iso", ".wia", ".gcz", ".ciso" },
                AllowRecursiveSearch = true,
                RequireSingleCanonicalArtifact = false
            };

            var artifactResult = new DolphinArtifactInspector().Inspect(archivePath, extractedPath, policy, logger);
            result.SourcePath = artifactResult.SourcePath;
            result.StagedContentRoot = artifactResult.StagedContentRoot;
            result.RequiresExtraction = artifactResult.RequiresExtraction;
            result.IsAmbiguous = artifactResult.IsAmbiguous;
            result.ErrorMessage = artifactResult.ErrorMessage;
            foreach (var warning in artifactResult.Warnings)
            {
                result.Warnings.Add(warning);
            }

            if (artifactResult.IsAmbiguous)
            {
                result.Warnings.Add("Multiple Wii game candidates detected.");
            }

            if (!artifactResult.IsValid && string.Equals(artifactResult.ErrorMessage, "No supported Dolphin artifacts found in staged content.", StringComparison.Ordinal))
            {
                result.ErrorMessage = "No Nintendo Wii artifacts found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
            }

            foreach (var candidate in artifactResult.CandidateArtifacts)
            {
                var typed = BuildCandidate(candidate.Path, candidate.Extension);
                result.CandidateArtifacts.Add(typed);
            }

            if (!artifactResult.IsValid)
            {
                result.DetectedFormat = MapFormat(Path.GetExtension(result.SourcePath));
                if (result.RequiresExtraction)
                {
                    result.NormalizedFormat = WiiContentFormat.Archive;
                }

                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = artifactResult.ErrorMessage;
                }

                return result;
            }

            var selected = result.CandidateArtifacts
                .Where(candidate => string.Equals(candidate.Path, artifactResult.CanonicalArtifactPath, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault() ?? result.CandidateArtifacts.First();

            result.IsValid = true;
            result.LaunchArtifactPath = selected.Path;
            result.DetectedFormat = selected.Format;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Revision = selected.Revision;
            result.Region = selected.Region;

            logger?.Write(PlatformLogLevel.Info, $"Resolved Wii launch artifact: '{result.LaunchArtifactPath}'.");
            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title ID: {result.TitleId}");
            }
            if (!string.IsNullOrWhiteSpace(result.TitleName))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title: {result.TitleName}");
            }
            if (!string.IsNullOrWhiteSpace(result.Revision))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Revision: {result.Revision}");
            }
            if (!string.IsNullOrWhiteSpace(result.Region))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Region: {result.Region}");
            }

            if (result.IsAmbiguous)
            {
                logger?.Write(PlatformLogLevel.Warning, "Multiple Wii game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {result.LaunchArtifactPath}");
            }

            return result;
        }

        private static WiiContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;
            var titleId = ExtractTitleId(fileName);

            return new WiiContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = WiiContentFormat.DiscImage,
                IsDirectLaunchArtifact = true,
                TitleId = titleId,
                TitleName = InferTitleName(fileName),
                Revision = ExtractRevision(fileName),
                Region = InferRegion(titleId)
            };
        }

        private static WiiContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => WiiContentFormat.Iso,
                ".wbfs" => WiiContentFormat.Wbfs,
                ".gcz" => WiiContentFormat.Gcz,
                ".ciso" => WiiContentFormat.Ciso,
                ".wia" => WiiContentFormat.Wia,
                ".rvz" => WiiContentFormat.Rvz,
                ".zip" or ".7z" or ".rar" => WiiContentFormat.Archive,
                _ => WiiContentFormat.Unknown
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

            var match = TitleIdRegex.Match(text.ToUpperInvariant());
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
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

        private static string InferRegion(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || titleId.Length < 4)
            {
                return string.Empty;
            }

            var regionChar = char.ToUpperInvariant(titleId[3]);
            return regionChar switch
            {
                'E' => "NTSC-U",
                'J' => "NTSC-J",
                'P' => "PAL",
                'K' => "NTSC-K",
                _ => string.Empty
            };
        }

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }
    }
}

