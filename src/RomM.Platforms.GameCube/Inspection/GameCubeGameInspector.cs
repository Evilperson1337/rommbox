using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.DolphinInternal;

namespace RomM.Platforms.GameCube.Inspection
{
    public sealed class GameCubeGameInspector
    {
        private static readonly string[] DirectFormats = { ".iso", ".gcz", ".ciso", ".wia", ".rvz" };
        private static readonly string[] ArchiveFormats = { ".zip", ".7z", ".rar" };

        private static readonly Regex GameIdRegex = new(@"(?<![A-Z0-9])[A-Z0-9]{4}[0-9]{2}(?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RevisionRegex = new(@"(?:rev|r)[\s_\.-]*(\d{1,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public GameCubeGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new GameCubeGameInspectionResult();
            var policy = new DolphinPlatformPolicy
            {
                SupportedExtensions = DirectFormats,
                ArchiveExtensions = ArchiveFormats,
                PreferredExtensions = new[] { ".rvz", ".iso", ".gcz", ".wia", ".ciso" },
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
                result.Warnings.Add("Multiple GameCube game candidates detected.");
            }

            if (!artifactResult.IsValid && string.Equals(artifactResult.ErrorMessage, "No supported Dolphin artifacts found in staged content.", StringComparison.Ordinal))
            {
                result.ErrorMessage = "No Nintendo GameCube artifacts found in staged content.";
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
                    result.NormalizedFormat = GameCubeContentFormat.Archive;
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
            result.GameId = selected.GameId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Revision = selected.Revision;
            result.Region = selected.Region;

            logger?.Write(PlatformLogLevel.Info, $"Resolved GameCube launch artifact: '{result.LaunchArtifactPath}'.");
            if (!string.IsNullOrWhiteSpace(result.GameId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed GameCube Game ID: {result.GameId}");
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
                logger?.Write(PlatformLogLevel.Warning, "Multiple GameCube game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {result.LaunchArtifactPath}");
            }

            return result;
        }

        private static GameCubeContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;
            var gameId = ExtractGameId(fileName);

            return new GameCubeContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = GameCubeContentFormat.DiscImage,
                IsDirectLaunchArtifact = true,
                GameId = gameId,
                TitleName = InferTitleName(fileName),
                Revision = ExtractRevision(fileName),
                Region = InferRegion(gameId)
            };
        }

        private static GameCubeContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".iso" => GameCubeContentFormat.Iso,
                ".gcz" => GameCubeContentFormat.Gcz,
                ".ciso" => GameCubeContentFormat.Ciso,
                ".wia" => GameCubeContentFormat.Wia,
                ".rvz" => GameCubeContentFormat.Rvz,
                ".zip" or ".7z" or ".rar" => GameCubeContentFormat.Archive,
                _ => GameCubeContentFormat.Unknown
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

            var match = GameIdRegex.Match(text.ToUpperInvariant());
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

        private static string InferRegion(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || gameId.Length < 4)
            {
                return string.Empty;
            }

            var regionChar = char.ToUpperInvariant(gameId[3]);
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

