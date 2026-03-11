using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Arcade.Inspection
{
    public sealed class ArcadeGameInspector
    {
        public ArcadeGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new ArcadeGameInspectionResult
            {
                RequiresExtraction = false,
                SupportedEmulators = new List<string> { "MAME", "RetroArch+FinalBurnNeo" }
            };

            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                var ext = Path.GetExtension(archivePath) ?? string.Empty;
                if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    result.ErrorMessage = $"Unsupported Arcade source archive extension '{ext}'. Expected .zip ROM set archive.";
                    result.Warnings.Add(result.ErrorMessage);
                    return result;
                }

                result.IsValid = true;
                result.DetectedArchive = archivePath;
                result.LaunchArtifactPath = archivePath;
                result.RomSetName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
                result.CandidateRomSets.Add(Path.GetFileName(archivePath) ?? string.Empty);
                return result;
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && File.Exists(extractedPath))
            {
                var ext = Path.GetExtension(extractedPath) ?? string.Empty;
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = true;
                    result.DetectedArchive = extractedPath;
                    result.LaunchArtifactPath = extractedPath;
                    result.RomSetName = Path.GetFileNameWithoutExtension(extractedPath) ?? string.Empty;
                    result.CandidateRomSets.Add(Path.GetFileName(extractedPath) ?? string.Empty);
                    result.Warnings.Add("Arcade ROM set archive was provided as extracted file path; preserving archive as launch artifact.");
                    return result;
                }
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
            {
                var candidates = Directory
                    .EnumerateFiles(extractedPath, "*.zip", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    result.CandidateRomSets.Add(Path.GetFileName(candidate) ?? candidate);
                }

                if (candidates.Count == 1)
                {
                    var selected = candidates[0];
                    result.IsValid = true;
                    result.DetectedArchive = selected;
                    result.LaunchArtifactPath = selected;
                    result.RomSetName = Path.GetFileNameWithoutExtension(selected) ?? string.Empty;
                    result.Warnings.Add("Arcade extracted staging detected; selected nested ROM set archive and will preserve it.");
                    return result;
                }

                if (candidates.Count > 1)
                {
                    result.ErrorMessage = "Multiple Arcade ROM set archives detected in staged content. Provide a single ROM set archive.";
                    result.Warnings.Add(result.ErrorMessage);
                    logger?.Write(PlatformLogLevel.Warning, $"Arcade inspection found multiple ROM set candidates: {string.Join(", ", result.CandidateRomSets)}");
                    return result;
                }
            }

            var fallbackName = string.IsNullOrWhiteSpace(gameName) ? "unknown" : gameName;
            result.ErrorMessage = $"No valid Arcade ROM set archive detected for '{fallbackName}'.";
            result.Warnings.Add(result.ErrorMessage);
            return result;
        }
    }
}
