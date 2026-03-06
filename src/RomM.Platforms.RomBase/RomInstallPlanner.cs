using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.RomBase
{
    internal sealed class RomInstallPlanner
    {
        private readonly RomInstallProfile _profile;
        private readonly RomFileFinder _finder;
        private readonly RomInstallPathResolver _resolver;

        public RomInstallPlanner(RomInstallProfile profile)
        {
            _profile = profile;
            _finder = new RomFileFinder(profile.RomExtensions);
            _resolver = new RomInstallPathResolver(profile);
        }

        public RomInstallSelection Plan(
            string? gameName,
            string? archivePath,
            string? extractedPath,
            string? installDirectory,
            RomInstallSettings? settings,
            IPlatformLogger? logger)
        {
            var installRoot = _resolver.ResolveInstallDirectory(installDirectory ?? string.Empty, settings);
            var selection = new RomInstallSelection
            {
                SourceRoot = installRoot
            };

            var archivePolicy = settings?.ArchivePolicy ?? _profile.ArchivePolicy;
            var allowExtraction = archivePolicy != RomArchivePolicy.Preserve;

            var candidates = new List<string>();
            if (allowExtraction && !string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"ROM install planner scanning extracted path '{extractedPath}'.");
                candidates.AddRange(_finder.FindCandidates(extractedPath));
            }

            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(archivePath))
            {
                if (archivePolicy == RomArchivePolicy.Preserve || _finder.IsRomCandidate(archivePath))
                {
                    logger?.Write(PlatformLogLevel.Info, $"ROM install planner falling back to archive '{archivePath}'.");
                    candidates.Add(archivePath);
                    selection.ShouldPreserveArchive = true;
                }
            }

            selection.Candidates = candidates;

            if (candidates.Count == 0)
            {
                selection.Message = "No compatible ROM files found.";
                return selection;
            }

            var chosen = ChooseCandidate(candidates, gameName, logger);
            selection.SourcePath = chosen;
            if (string.IsNullOrWhiteSpace(chosen))
            {
                selection.Message = "No compatible ROM files found.";
                return selection;
            }

            if (string.IsNullOrWhiteSpace(installRoot))
            {
                selection.Message = "Install directory missing.";
                return selection;
            }

            var targetFileName = _resolver.ResolveTargetFileName(gameName, chosen);
            selection.TargetPath = Path.Combine(installRoot, targetFileName);
            return selection;
        }

        private static string? ChooseCandidate(IReadOnlyList<string> candidates, string? gameName, IPlatformLogger? logger)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var normalized = Normalize(gameName);
                var best = candidates
                    .Select(path => new { Path = path, Score = ScoreCandidate(path, normalized) })
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Path.Length)
                    .FirstOrDefault();
                if (best != null)
                {
                    logger?.Write(PlatformLogLevel.Info, $"ROM install planner selected candidate '{best.Path}' from {candidates.Count} options.");
                    return best.Path;
                }
            }

            var fallback = candidates.OrderBy(path => path.Length).First();
            logger?.Write(PlatformLogLevel.Info, $"ROM install planner selected shortest candidate '{fallback}' from {candidates.Count} options.");
            return fallback;
        }

        private static int ScoreCandidate(string path, string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var normalizedCandidate = Normalize(fileName);
            if (string.Equals(normalizedCandidate, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (normalizedCandidate.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            if (normalizedName.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return 60;
            }

            return 10;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
