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
                : Path.GetDirectoryName(sourcePath) ?? string.Empty;

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
            string? archivePath,
            Ps4InspectorOptions? options,
            Ps4GameInspectionResult result,
            IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            if (ArchiveExtensions.Contains(extension))
            {
                var warning = "Archive provided without extracted content; extracted-folder inspection is required for PS4 archive installs.";
                result.Warnings.Add(warning);
                result.ErrorMessage = warning;
                logger?.Write(PlatformLogLevel.Warning, warning);
                return;
            }

            if (string.Equals(extension, ".pkg", StringComparison.OrdinalIgnoreCase))
            {
                var isDirectPkg = !string.IsNullOrWhiteSpace(archivePath)
                    && string.Equals(Path.GetFullPath(archivePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeExtension(Path.GetExtension(archivePath)), ".pkg", StringComparison.OrdinalIgnoreCase);

                result.IsDirectPkgDownload = isDirectPkg;
                if (isDirectPkg)
                {
                    logger?.Write(PlatformLogLevel.Info, $"Detected direct PKG download: {filePath}");
                }

                var candidate = BuildPkgCandidate(filePath, Ps4ContentRole.BaseGame, result.DirectPkgSupportEnabled && isDirectPkg);
                AddCandidate(result, candidate, logger);
                if (!candidate.IsSupportedForInstall)
                {
                    var message = "Detected PKG content but PKG support is disabled because downloaded file is not a direct .pkg or extractor path is not configured.";
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
            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory) ?? string.Empty;
                var titleId = ExtractTitleId(name);
                if (string.IsNullOrWhiteSpace(titleId))
                {
                    continue;
                }

                var role = InferRoleFromPath(directory);
                var candidate = new Ps4ContentCandidate
                {
                    Path = directory,
                    ContentRole = role,
                    ContentFormat = Ps4ContentFormat.ExtractedFolder,
                    TitleId = titleId,
                    TitleName = InferTitleName(name),
                    Version = ExtractVersion(name),
                    IsSupportedForInstall = true
                };

                AddCandidate(result, candidate, logger);
            }

            var pkgFiles = files.Where(path => string.Equals(NormalizeExtension(Path.GetExtension(path)), ".pkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pkgFiles.Length > 0)
            {
                foreach (var pkgPath in pkgFiles)
                {
                    var role = InferRoleFromPath(pkgPath);
                    var candidate = BuildPkgCandidate(pkgPath, role, options?.AllowExtractedPkgInstall == true && result.DirectPkgSupportEnabled);
                    if (!candidate.IsSupportedForInstall)
                    {
                        candidate.Warnings.Add("Nested/extracted PKG detected. Direct PKG rule does not allow automatic support in archive workflow.");
                    }

                    AddCandidate(result, candidate, logger);
                }

                result.Warnings.Add("Detected PKG content inside extracted archive. PKG support for extracted PKG is disabled by default.");
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
                ContentRole = role,
                ContentFormat = isSupported ? Ps4ContentFormat.Pkg : Ps4ContentFormat.Unsupported,
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

            result.IsAmbiguous = supportedBase.Count > 1;
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

