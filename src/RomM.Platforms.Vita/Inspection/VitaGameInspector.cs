using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Vita.Inspection
{
    public sealed class VitaGameInspector
    {
        private static readonly HashSet<string> PackageFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vpk"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar",
            ".gz"
        };

        private static readonly Regex TitleIdRegex = new(@"\b([A-Za-z]{4}\d{5})\b", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"\b(?:v|ver(?:sion)?|patch|update)\s*([0-9]+(?:\.[0-9]+)*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParamTitleIdRegex = new(@"TITLE_ID[\x00-\x20:=]+([A-Za-z]{4}\d{5})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParamTitleRegex = new(@"(?:^|[\x00-\x20])TITLE(?!_ID)[\x00-\x20:=]+([^\x00\r\n]{2,120})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParamVersionRegex = new(@"APP_VER[\x00-\x20:=]+([0-9]+(?:\.[0-9]+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public VitaGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new VitaGameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No PlayStation Vita source content available for inspection.";
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
                result.ErrorMessage = $"PlayStation Vita source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, VitaGameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = VitaContentFormat.Archive;
                result.Warnings.Add("PlayStation Vita archive detected. Extraction is required before install artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Detected archive containing Vita content");
                logger?.Write(PlatformLogLevel.Info, "Extraction enabled: True");
                return;
            }

            if (!PackageFormats.Contains(extension))
            {
                result.ErrorMessage = $"Unsupported PlayStation Vita content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildPackageCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;

            logger?.Write(PlatformLogLevel.Info, $"Detected PlayStation Vita content format: {candidate.Format}");
            logger?.Write(PlatformLogLevel.Info, $"Detected content role: {candidate.Role}");
            logger?.Write(PlatformLogLevel.Info, $"Detected candidate install artifact: {candidate.Path}");
        }

        private static void AnalyzeDirectory(string rootPath, VitaGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = VitaContentFormat.Directory;
            result.RequiresExtraction = false;

            var files = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var extension = NormalizeExtension(Path.GetExtension(file));
                if (!PackageFormats.Contains(extension))
                {
                    continue;
                }

                result.CandidateArtifacts.Add(BuildPackageCandidate(file, extension));
            }

            var layoutCandidates = DetectExtractedLayoutCandidates(rootPath, files);
            foreach (var layout in layoutCandidates)
            {
                result.CandidateArtifacts.Add(layout);
            }

            if (result.CandidateArtifacts.Count == 0)
            {
                result.ErrorMessage = "No Vita install artifacts (.vpk or extracted layout with PARAM.SFO) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {result.CandidateArtifacts.Count} Vita candidate artifact(s) in staged content.");
        }

        private static List<VitaContentCandidate> DetectExtractedLayoutCandidates(string rootPath, IReadOnlyCollection<string> allFiles)
        {
            var results = new List<VitaContentCandidate>();
            var paramFiles = allFiles
                .Where(path => string.Equals(Path.GetFileName(path), "param.sfo", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var paramPath in paramFiles)
            {
                var sceSys = Path.GetDirectoryName(paramPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sceSys))
                {
                    continue;
                }

                var appRoot = Directory.GetParent(sceSys)?.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(appRoot) || !Directory.Exists(appRoot))
                {
                    continue;
                }

                var metadata = ParseParamSfo(paramPath);
                var titleId = !string.IsNullOrWhiteSpace(metadata.TitleId)
                    ? metadata.TitleId
                    : ExtractTitleId(appRoot);

                results.Add(new VitaContentCandidate
                {
                    Path = appRoot,
                    FileName = Path.GetFileName(appRoot) ?? string.Empty,
                    Format = VitaContentFormat.ExtractedLayout,
                    NormalizedFormat = VitaContentFormat.Package,
                    Role = InferRoleFromPath(appRoot, titleId),
                    IsInstallableArtifact = true,
                    TitleId = titleId,
                    TitleName = metadata.Title,
                    Version = metadata.Version
                });
            }

            return results
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void FinalizeSelection(VitaGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var ordered = result.CandidateArtifacts
                .Where(candidate => candidate.IsInstallableArtifact)
                .OrderBy(RolePriority)
                .ThenBy(FormatPriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in ordered)
            {
                result.OrderedInstallCandidates.Add(candidate);
            }

            var baseCandidates = ordered
                .Where(candidate => candidate.Role == VitaContentRole.BaseGame || candidate.Role == VitaContentRole.Unknown)
                .ToList();

            if (baseCandidates.Count == 0)
            {
                result.ErrorMessage = "No Vita base game candidate detected. Update/DLC content requires a base game candidate in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                logger?.Write(PlatformLogLevel.Warning, result.ErrorMessage);
                return;
            }

            var selectedBase = baseCandidates
                .OrderBy(FormatPriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .First();

            var roleScoped = ordered.Where(candidate => candidate.Role == selectedBase.Role).ToList();
            result.IsAmbiguous = roleScoped.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple Vita base-game candidates detected ({roleScoped.Count}). Selected '{selectedBase.FileName}'.");
                logger?.Write(PlatformLogLevel.Warning, "Multiple Vita game candidates detected");
                logger?.Write(PlatformLogLevel.Info, $"Selected candidate by priority rule: {selectedBase.Path}");
            }

            result.IsValid = true;
            result.InstallArtifactPath = selectedBase.Path;
            result.ContentRole = selectedBase.Role;
            result.NormalizedFormat = selectedBase.NormalizedFormat;
            result.TitleId = selectedBase.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selectedBase.TitleName)
                ? selectedBase.TitleName
                : (gameName ?? string.Empty);
            result.Version = selectedBase.Version;

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
        }

        private static VitaContentCandidate BuildPackageCandidate(string path, string extension)
        {
            var titleId = ExtractTitleId(path);
            return new VitaContentCandidate
            {
                Path = path,
                FileName = Path.GetFileName(path) ?? string.Empty,
                Format = MapFormat(extension),
                NormalizedFormat = VitaContentFormat.Package,
                Role = InferRoleFromPath(path, titleId),
                IsInstallableArtifact = true,
                TitleId = titleId,
                TitleName = InferTitle(path),
                Version = ExtractVersion(path)
            };
        }

        private static VitaContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".vpk" => VitaContentFormat.Vpk,
                ".zip" or ".7z" or ".rar" or ".gz" => VitaContentFormat.Archive,
                _ => VitaContentFormat.Unknown
            };
        }

        private static VitaContentRole InferRoleFromPath(string path, string titleId)
        {
            var normalized = (path ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("dlc") || normalized.Contains("addcont") || normalized.Contains("add-on") || normalized.Contains("addon"))
            {
                return VitaContentRole.Dlc;
            }

            if (normalized.Contains("update") || normalized.Contains("patch") || normalized.Contains("repatch"))
            {
                return VitaContentRole.Update;
            }

            if (!string.IsNullOrWhiteSpace(titleId)
                && (normalized.Contains($"patch{Path.DirectorySeparatorChar}{titleId.ToLowerInvariant()}")
                    || normalized.Contains($"repatch{Path.DirectorySeparatorChar}{titleId.ToLowerInvariant()}")))
            {
                return VitaContentRole.Update;
            }

            return VitaContentRole.BaseGame;
        }

        private static int RolePriority(VitaContentCandidate candidate)
        {
            return candidate.Role switch
            {
                VitaContentRole.BaseGame => 0,
                VitaContentRole.Update => 1,
                VitaContentRole.Dlc => 2,
                _ => 9
            };
        }

        private static int FormatPriority(VitaContentCandidate candidate)
        {
            return candidate.Format switch
            {
                VitaContentFormat.ExtractedLayout => 0,
                VitaContentFormat.Vpk => 1,
                _ => 9
            };
        }

        private static string NormalizeExtension(string extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ExtractTitleId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = TitleIdRegex.Match(text);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
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

        private static string InferTitle(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return fileName.Replace('_', ' ').Trim();
        }

        private static (string TitleId, string Title, string Version) ParseParamSfo(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                {
                    return (string.Empty, string.Empty, string.Empty);
                }

                var text = Encoding.UTF8.GetString(bytes);
                var titleIdMatch = ParamTitleIdRegex.Match(text);
                var titleMatch = ParamTitleRegex.Match(text);
                var versionMatch = ParamVersionRegex.Match(text);

                var titleId = titleIdMatch.Success ? titleIdMatch.Groups[1].Value.ToUpperInvariant() : string.Empty;
                var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : string.Empty;
                var version = versionMatch.Success ? versionMatch.Groups[1].Value : string.Empty;
                return (titleId, title, version);
            }
            catch
            {
                return (string.Empty, string.Empty, string.Empty);
            }
        }
    }
}

