using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PS4.Inspection
{
    public sealed class Ps4GameInspector
    {
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar"
        };

        private static readonly Regex TitleIdRegex = new("(?<![A-Z0-9])(CUSA|CUSB|CUSC|PCAS|PCJS|PPSA|PPSH)[0-9]{5}(?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Ps4GameInspectionResult Inspect(
            string? archivePath,
            string? extractedPath,
            string? gameName,
            Ps4InspectorOptions options,
            IPlatformLogger? logger)
        {
            var result = new Ps4GameInspectionResult
            {
                ArchiveExtractionEnabled = options?.ArchiveExtractionEnabled == true,
                DirectPkgSupportEnabled = options?.DirectPkgSupportEnabled == true
            };

            var sourcePath = ResolveSourcePath(archivePath, extractedPath, options);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No PS4 source content available for inspection.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            result.SourcePath = sourcePath;
            result.StagedContentRoot = Directory.Exists(sourcePath)
                ? sourcePath
                : (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath)
                    ? extractedPath
                    : Path.GetDirectoryName(sourcePath) ?? string.Empty);

            logger?.Write(PlatformLogLevel.Info, $"Detected PS4 input layout. Archive extraction enabled: {result.ArchiveExtractionEnabled}.");

            if (File.Exists(sourcePath))
            {
                AnalyzeFile(sourcePath, archivePath, options, result, logger);
            }
            else if (Directory.Exists(sourcePath))
            {
                AnalyzeDirectory(sourcePath, options, result, logger);
            }
            else
            {
                result.ErrorMessage = $"PS4 source path does not exist: '{sourcePath}'.";
                result.Warnings.Add(result.ErrorMessage);
                return result;
            }

            FinalizeSelection(result, gameName, logger);
            return result;
        }

        private static string ResolveSourcePath(string? archivePath, string? extractedPath, Ps4InspectorOptions? options)
        {
            if (options?.ArchiveExtractionEnabled == true
                && !string.IsNullOrWhiteSpace(extractedPath)
                && (Directory.Exists(extractedPath) || File.Exists(extractedPath)))
            {
                return extractedPath;
            }

            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                return archivePath;
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && (Directory.Exists(extractedPath) || File.Exists(extractedPath)))
            {
                return extractedPath;
            }

            return string.Empty;
        }

        private static void AnalyzeFile(
            string filePath,
            string? extractedPath,
            Ps4InspectorOptions? options,
            Ps4GameInspectionResult result,
            IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            if (ArchiveExtensions.Contains(extension))
            {
                result.SourceContentFormat = Ps4ContentFormat.Archive;
                logger?.Write(PlatformLogLevel.Info, $"Detected PS4 archive source: {filePath}");

                if (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Inspecting extracted PS4 archive staging at: {extractedPath}");
                    AnalyzeDirectory(extractedPath, options, result, logger);
                    return;
                }

                var warning = "Archive provided without extracted content; extracted staging is required for PS4 archive installs.";
                result.Warnings.Add(warning);
                result.ErrorMessage = warning;
                logger?.Write(PlatformLogLevel.Warning, warning);
                return;
            }

            if (string.Equals(extension, ".pkg", StringComparison.OrdinalIgnoreCase))
            {
                result.SourceContentFormat = Ps4ContentFormat.Pkg;
                result.IsDirectPkgDownload = true;
                logger?.Write(PlatformLogLevel.Info, $"Detected direct PKG download: {filePath}");

                var candidate = BuildPkgCandidate(filePath, Ps4ContentRole.BaseGame, result.DirectPkgSupportEnabled);
                AddCandidate(result, candidate, logger);
                if (!candidate.IsSupportedForInstall)
                {
                    var message = "Detected PKG content but PKG support is disabled because extractor support is not configured.";
                    result.Warnings.Add(message);
                    logger?.Write(PlatformLogLevel.Warning, message);
                }

                return;
            }

            result.ErrorMessage = $"Unsupported PS4 content extension '{extension}'.";
            result.Warnings.Add(result.ErrorMessage);
            logger?.Write(PlatformLogLevel.Warning, result.ErrorMessage);
        }

        private static void AnalyzeDirectory(string rootPath, Ps4InspectorOptions? options, Ps4GameInspectionResult result, IPlatformLogger? logger)
        {
            result.SourceContentFormat = Ps4ContentFormat.Folder;

            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var folderCandidate in DiscoverFolderCandidates(rootPath, files, directories))
            {
                AddCandidate(result, folderCandidate, logger);
            }

            var pkgFiles = files.Where(path => string.Equals(NormalizeExtension(Path.GetExtension(path)), ".pkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pkgFiles.Length > 0)
            {
                foreach (var pkgPath in pkgFiles)
                {
                    var role = InferRoleFromPath(pkgPath);
                    var candidate = BuildPkgCandidate(pkgPath, role, options?.AllowExtractedPkgInstall == true && result.DirectPkgSupportEnabled);

                    AddCandidate(result, candidate, logger);
                }
            }

            if (result.DetectedItems.Count == 0)
            {
                result.ErrorMessage = "No PS4 installable content discovered in staged directory.";
                result.Warnings.Add(result.ErrorMessage);
            }
        }

        private static Ps4ContentCandidate BuildPkgCandidate(string path, Ps4ContentRole role, bool isSupported)
        {
            var fileName = Path.GetFileName(path) ?? string.Empty;
            return new Ps4ContentCandidate
            {
                Path = path,
                RelativePath = Path.GetFileName(path) ?? string.Empty,
                ContentRole = role,
                ContentFormat = Ps4ContentFormat.Pkg,
                TitleId = ExtractTitleId(fileName),
                TitleName = InferTitleName(fileName),
                Version = ExtractVersion(fileName),
                RequiresExternalExtractor = true,
                IsSupportedForInstall = isSupported
            };
        }

        private static void AddCandidate(Ps4GameInspectionResult result, Ps4ContentCandidate candidate, IPlatformLogger? logger)
        {
            result.DetectedItems.Add(candidate);
            switch (candidate.ContentRole)
            {
                case Ps4ContentRole.Update:
                    result.UpdateItems.Add(candidate);
                    logger?.Write(PlatformLogLevel.Info, $"Detected update item: {candidate.ContentFormat} path={candidate.Path}");
                    break;
                case Ps4ContentRole.Dlc:
                    result.DlcItems.Add(candidate);
                    logger?.Write(PlatformLogLevel.Info, $"Detected DLC item: {candidate.ContentFormat} path={candidate.Path}");
                    break;
                case Ps4ContentRole.Bonus:
                    result.BonusItems.Add(candidate);
                    logger?.Write(PlatformLogLevel.Info, $"Detected bonus item: {candidate.ContentFormat} path={candidate.Path}");
                    break;
                case Ps4ContentRole.Unknown:
                    result.UnknownItems.Add(candidate);
                    logger?.Write(PlatformLogLevel.Warning, $"Detected unknown PS4 item: {candidate.ContentFormat} path={candidate.Path}");
                    break;
                default:
                    result.BaseGameItems.Add(candidate);
                    logger?.Write(PlatformLogLevel.Info, $"Detected base game item: {candidate.ContentFormat} path={candidate.Path}");
                    break;
            }
        }

        private static void FinalizeSelection(Ps4GameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var supportedBase = result.BaseGameItems
                .Where(item => item.IsSupportedForInstall)
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (supportedBase.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to determine canonical PS4 base game install target safely.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            if (supportedBase.Count > 1)
            {
                supportedBase = supportedBase
                    .OrderByDescending(item => item.HasPlayableBinary)
                    .ThenBy(item => item.Path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                    .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            result.IsAmbiguous = supportedBase.Skip(1).Any(item => item.HasPlayableBinary == supportedBase[0].HasPlayableBinary);
            if (result.IsAmbiguous)
            {
                var warning = $"Multiple base game candidates detected ({supportedBase.Count}). Unable to determine canonical install target safely.";
                result.Warnings.Add(warning);
                logger?.Write(PlatformLogLevel.Warning, warning);
                result.IsValid = false;
                result.ErrorMessage = warning;
                return;
            }

            var selected = supportedBase[0];
            result.IsValid = true;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName)
                ? selected.TitleName
                : (gameName ?? string.Empty);
            result.Version = selected.Version;

            if (!string.IsNullOrWhiteSpace(result.TitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Parsed Title ID: {result.TitleId}");
            }
        }

        private static Ps4ContentRole InferRoleFromPath(string path)
        {
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
            if (normalized.Contains("\\bonus\\") || normalized.EndsWith("\\bonus", StringComparison.Ordinal))
            {
                return Ps4ContentRole.Bonus;
            }

            if (normalized.Contains("\\update\\") || normalized.EndsWith("\\update", StringComparison.Ordinal))
            {
                return Ps4ContentRole.Update;
            }

            if (normalized.Contains("\\dlc\\") || normalized.EndsWith("\\dlc", StringComparison.Ordinal))
            {
                return Ps4ContentRole.Dlc;
            }

            if (normalized.Contains("patch", StringComparison.Ordinal))
            {
                return Ps4ContentRole.Update;
            }

            return Ps4ContentRole.BaseGame;
        }

        private static IEnumerable<Ps4ContentCandidate> DiscoverFolderCandidates(
            string rootPath,
            IReadOnlyCollection<string> files,
            IReadOnlyCollection<string> directories)
        {
            var map = new Dictionary<string, Ps4ContentCandidate>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                if (!TryResolveDirectoryCandidateRoot(rootPath, directory, out var candidateRoot, out var role))
                {
                    continue;
                }

                EnsureFolderCandidate(map, rootPath, candidateRoot, role);
            }

            foreach (var file in files)
            {
                if (string.Equals(NormalizeExtension(Path.GetExtension(file)), ".pkg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryResolveFolderCandidateRoot(rootPath, file, out var candidateRoot, out var role))
                {
                    continue;
                }

                var candidate = EnsureFolderCandidate(map, rootPath, candidateRoot, role);

                if (string.Equals(Path.GetFileName(file), "eboot.bin", StringComparison.OrdinalIgnoreCase))
                {
                    candidate.HasPlayableBinary = true;
                }
            }

            return map.Values
                .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static Ps4ContentCandidate EnsureFolderCandidate(
            IDictionary<string, Ps4ContentCandidate> map,
            string rootPath,
            string candidateRoot,
            Ps4ContentRole role)
        {
            if (!map.TryGetValue(candidateRoot, out var candidate))
            {
                var name = Path.GetFileName(candidateRoot) ?? string.Empty;
                candidate = new Ps4ContentCandidate
                {
                    Path = candidateRoot,
                    RelativePath = Path.GetRelativePath(rootPath, candidateRoot),
                    ContentRole = role,
                    ContentFormat = Ps4ContentFormat.Folder,
                    TitleId = ExtractTitleId(name),
                    TitleName = InferTitleName(name),
                    Version = ExtractVersion(name),
                    IsSupportedForInstall = true
                };
                map[candidateRoot] = candidate;
            }

            return candidate;
        }

        private static bool TryResolveDirectoryCandidateRoot(string rootPath, string directoryPath, out string candidateRoot, out Ps4ContentRole role)
        {
            candidateRoot = string.Empty;
            role = Ps4ContentRole.Unknown;

            var relative = Path.GetRelativePath(rootPath, directoryPath);
            var segments = relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            if (segments.Length == 0)
            {
                return false;
            }

            var normalizedSegments = segments.Select(segment => segment.ToLowerInvariant()).ToArray();

            var bonusIndex = Array.FindIndex(normalizedSegments, segment => segment == "bonus");
            if (bonusIndex >= 0 && bonusIndex + 1 < segments.Length)
            {
                role = Ps4ContentRole.Bonus;
                candidateRoot = Path.Combine(rootPath, Path.Combine(segments.Take(bonusIndex + 2).ToArray()));
                return true;
            }

            var dlcIndex = Array.FindIndex(normalizedSegments, segment => segment == "dlc");
            if (dlcIndex >= 0 && dlcIndex + 1 < segments.Length)
            {
                role = Ps4ContentRole.Dlc;
                candidateRoot = Path.Combine(rootPath, Path.Combine(segments.Take(dlcIndex + 2).ToArray()));
                return true;
            }

            var updateIndex = Array.FindIndex(normalizedSegments, segment => segment == "update");
            if (updateIndex >= 0 && updateIndex + 1 < segments.Length)
            {
                role = Ps4ContentRole.Update;
                candidateRoot = Path.Combine(rootPath, Path.Combine(segments.Take(updateIndex + 2).ToArray()));
                return true;
            }

            var patchIndex = Array.FindIndex(segments, segment => segment.EndsWith("-patch", StringComparison.OrdinalIgnoreCase));
            if (patchIndex >= 0)
            {
                role = Ps4ContentRole.Update;
                candidateRoot = Path.Combine(rootPath, Path.Combine(segments.Take(patchIndex + 1).ToArray()));
                return true;
            }

            var titleIndex = Array.FindIndex(segments, segment => !string.IsNullOrWhiteSpace(ExtractTitleId(segment)));
            if (titleIndex >= 0)
            {
                role = InferRoleFromPath(directoryPath);
                candidateRoot = Path.Combine(rootPath, Path.Combine(segments.Take(titleIndex + 1).ToArray()));
                return true;
            }

            return false;
        }

        private static bool TryResolveFolderCandidateRoot(string rootPath, string filePath, out string candidateRoot, out Ps4ContentRole role)
        {
            candidateRoot = string.Empty;
            role = Ps4ContentRole.Unknown;

            var relative = Path.GetRelativePath(rootPath, filePath);
            var segments = relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            if (segments.Length < 2)
            {
                return false;
            }

            var directorySegments = segments.Take(segments.Length - 1).ToArray();
            var normalizedSegments = directorySegments.Select(segment => segment.ToLowerInvariant()).ToArray();

            var bonusIndex = Array.FindIndex(normalizedSegments, segment => segment == "bonus");
            if (bonusIndex >= 0)
            {
                role = Ps4ContentRole.Bonus;
                var rootIndex = Math.Min(bonusIndex + 1, directorySegments.Length - 1);
                candidateRoot = Path.Combine(rootPath, Path.Combine(directorySegments.Take(rootIndex + 1).ToArray()));
                return true;
            }

            var dlcIndex = Array.FindIndex(normalizedSegments, segment => segment == "dlc");
            if (dlcIndex >= 0)
            {
                role = Ps4ContentRole.Dlc;
                var rootIndex = Math.Min(dlcIndex + 1, directorySegments.Length - 1);
                candidateRoot = Path.Combine(rootPath, Path.Combine(directorySegments.Take(rootIndex + 1).ToArray()));
                return true;
            }

            var updateIndex = Array.FindIndex(normalizedSegments, segment => segment == "update");
            if (updateIndex >= 0)
            {
                role = Ps4ContentRole.Update;
                var rootIndex = Math.Min(updateIndex + 1, directorySegments.Length - 1);
                candidateRoot = Path.Combine(rootPath, Path.Combine(directorySegments.Take(rootIndex + 1).ToArray()));
                return true;
            }

            var patchIndex = Array.FindIndex(directorySegments, segment => segment.EndsWith("-patch", StringComparison.OrdinalIgnoreCase));
            if (patchIndex >= 0)
            {
                role = Ps4ContentRole.Update;
                candidateRoot = Path.Combine(rootPath, Path.Combine(directorySegments.Take(patchIndex + 1).ToArray()));
                return true;
            }

            var titleIndex = Array.FindIndex(directorySegments, segment => !string.IsNullOrWhiteSpace(ExtractTitleId(segment)));
            if (titleIndex >= 0)
            {
                role = Ps4ContentRole.BaseGame;
                candidateRoot = Path.Combine(rootPath, Path.Combine(directorySegments.Take(titleIndex + 1).ToArray()));
                return true;
            }

            return false;
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

        private static string InferTitleName(string fileOrFolderName)
        {
            var value = Path.GetFileNameWithoutExtension(fileOrFolderName) ?? string.Empty;
            return value.Replace('_', ' ').Trim();
        }

        private static string NormalizeExtension(string? extension)
        {
            return (extension ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}

