using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.DolphinInternal
{
    public sealed class DolphinArtifactInspector
    {
        public DolphinArtifactInspectionResult Inspect(string? archivePath, string? extractedPath, DolphinPlatformPolicy policy, IPlatformLogger? logger)
        {
            var result = new DolphinArtifactInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No source content available for Dolphin artifact inspection.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            result.SourcePath = sourcePath;
            result.StagedContentRoot = Directory.Exists(sourcePath)
                ? sourcePath
                : Path.GetDirectoryName(sourcePath) ?? string.Empty;

            var normalizedSupported = NormalizeSet(policy?.SupportedExtensions);
            var normalizedArchives = NormalizeSet(policy?.ArchiveExtensions);
            var preferred = NormalizePreferred(policy?.PreferredExtensions, normalizedSupported);

            if (File.Exists(sourcePath))
            {
                AnalyzeFile(sourcePath, normalizedSupported, normalizedArchives, result, logger);
            }
            else if (Directory.Exists(sourcePath))
            {
                AnalyzeDirectory(sourcePath, normalizedSupported, policy?.AllowRecursiveSearch ?? true, result, logger);
            }
            else
            {
                result.ErrorMessage = $"Source path does not exist: '{sourcePath}'.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            FinalizeSelection(result, preferred, policy?.RequireSingleCanonicalArtifact ?? false, logger);
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

        private static void AnalyzeFile(
            string filePath,
            HashSet<string> supported,
            HashSet<string> archives,
            DolphinArtifactInspectionResult result,
            IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.RequiresExtraction = archives.Contains(extension);

            if (archives.Contains(extension))
            {
                result.Warnings.Add($"Archive detected '{extension}'. Extraction is required before selecting canonical Dolphin artifact.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing Dolphin content");
                return;
            }

            if (!supported.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            result.CandidateArtifacts.Add(new DolphinArtifactCandidate
            {
                Path = filePath,
                FileName = Path.GetFileName(filePath) ?? string.Empty,
                Extension = extension
            });
        }

        private static void AnalyzeDirectory(
            string rootPath,
            HashSet<string> supported,
            bool recursive,
            DolphinArtifactInspectionResult result,
            IPlatformLogger? logger)
        {
            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var candidates = Directory
                .EnumerateFiles(rootPath, "*", search)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new
                {
                    Path = path,
                    Extension = NormalizeExtension(Path.GetExtension(path))
                })
                .Where(item => supported.Contains(item.Extension))
                .Select(item => new DolphinArtifactCandidate
                {
                    Path = item.Path,
                    FileName = Path.GetFileName(item.Path) ?? string.Empty,
                    Extension = item.Extension
                })
                .ToList();

            foreach (var candidate in candidates)
            {
                result.CandidateArtifacts.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "No supported Dolphin artifacts found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {candidates.Count} Dolphin artifact candidate(s) in staged content.");
        }

        private static void FinalizeSelection(
            DolphinArtifactInspectionResult result,
            IReadOnlyDictionary<string, int> preferredOrder,
            bool requireSingle,
            IPlatformLogger? logger)
        {
            var candidates = result.CandidateArtifacts
                .OrderBy(candidate => CandidateRank(candidate, preferredOrder))
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to resolve canonical Dolphin launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            result.IsAmbiguous = candidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple candidate artifacts detected ({candidates.Count}). Selected '{candidates[0].FileName}'.");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {candidates[0].Path}");
            }

            if (requireSingle && candidates.Count > 1)
            {
                result.ErrorMessage = "Multiple candidate artifacts detected and policy requires a single canonical artifact.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            result.IsValid = true;
            result.CanonicalArtifactPath = candidates[0].Path;
            result.CanonicalExtension = candidates[0].Extension;
        }

        private static int CandidateRank(DolphinArtifactCandidate candidate, IReadOnlyDictionary<string, int> preferredOrder)
        {
            if (preferredOrder.TryGetValue(candidate.Extension, out var rank))
            {
                return rank;
            }

            return 999;
        }

        private static HashSet<string> NormalizeSet(IReadOnlyCollection<string>? values)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
            {
                return set;
            }

            foreach (var value in values)
            {
                var normalized = NormalizeExtension(value);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    set.Add(normalized);
                }
            }

            return set;
        }

        private static IReadOnlyDictionary<string, int> NormalizePreferred(IReadOnlyCollection<string>? preferred, HashSet<string> supported)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (preferred == null)
            {
                return map;
            }

            var rank = 0;
            foreach (var value in preferred)
            {
                var normalized = NormalizeExtension(value);
                if (string.IsNullOrWhiteSpace(normalized) || !supported.Contains(normalized) || map.ContainsKey(normalized))
                {
                    continue;
                }

                map[normalized] = rank;
                rank++;
            }

            return map;
        }

        private static string NormalizeExtension(string? extension)
        {
            var normalized = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : "." + normalized;
        }
    }
}

