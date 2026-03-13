using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.N3DS.Inspection
{
    public sealed class Nintendo3DSGameInspector
    {
        private static readonly HashSet<string> DirectFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".3ds",
            ".cci",
            ".cxi"
        };

        private static readonly HashSet<string> ImportFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cia",
            ".app"
        };

        private static readonly HashSet<string> ArchiveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".7z",
            ".rar"
        };

        private static readonly Regex TitleIdRegex = new("[0-9A-Fa-f]{16}", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new(@"(?:(?:v|ver|version)[\s_\.-]*)(\d+(?:\.\d+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Nintendo3DSGameInspectionResult Inspect(string? archivePath, string? extractedPath, string? gameName, IPlatformLogger? logger)
        {
            var result = new Nintendo3DSGameInspectionResult();
            var sourcePath = ResolveSourcePath(archivePath, extractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                result.ErrorMessage = "No Nintendo 3DS source content available for inspection.";
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
                result.ErrorMessage = $"Nintendo 3DS source path does not exist: '{sourcePath}'.";
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

        private static void AnalyzeFile(string filePath, Nintendo3DSGameInspectionResult result, IPlatformLogger? logger)
        {
            var extension = NormalizeExtension(Path.GetExtension(filePath));
            result.DetectedFormat = MapFormat(extension);
            result.RequiresExtraction = ArchiveFormats.Contains(extension);

            if (ArchiveFormats.Contains(extension))
            {
                result.NormalizedFormat = Nintendo3DSContentFormat.Archive;
                result.Warnings.Add("Archive detected. Extraction is required before Nintendo 3DS launch artifact selection.");
                logger?.Write(PlatformLogLevel.Info, "Archive detected");
                logger?.Write(PlatformLogLevel.Info, "Extracting archive for inspection");
                return;
            }

            if (!IsSupported3DsExtension(extension))
            {
                result.ErrorMessage = $"Unsupported Nintendo 3DS content extension '{extension}'.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            var candidate = BuildCandidate(filePath, extension);
            result.CandidateArtifacts.Add(candidate);
            result.NormalizedFormat = candidate.NormalizedFormat;
            result.RequiresEmulatorImport = candidate.RequiresEmulatorImport;

            if (candidate.Format == Nintendo3DSContentFormat.Cci)
            {
                logger?.Write(PlatformLogLevel.Info, "Detected Nintendo 3DS content format: CCI");
            }
            else if (candidate.Format == Nintendo3DSContentFormat.ThreeDs)
            {
                logger?.Write(PlatformLogLevel.Info, "Detected Nintendo 3DS content format: 3DS");
            }
            else if (candidate.Format == Nintendo3DSContentFormat.Cxi)
            {
                logger?.Write(PlatformLogLevel.Info, "Detected Nintendo 3DS content format: CXI");
            }
            else if (candidate.Format == Nintendo3DSContentFormat.Cia)
            {
                logger?.Write(PlatformLogLevel.Info, "Detected Nintendo 3DS content format: CIA");
            }
        }

        private static void AnalyzeDirectory(string rootPath, Nintendo3DSGameInspectionResult result, IPlatformLogger? logger)
        {
            result.DetectedFormat = Nintendo3DSContentFormat.Directory;
            result.RequiresExtraction = false;

            var candidates = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => (Path: path, Extension: NormalizeExtension(Path.GetExtension(path))))
                .Where(item => IsSupported3DsExtension(item.Extension))
                .Select(item => BuildCandidate(item.Path, item.Extension))
                .ToList();

            foreach (var candidate in candidates)
            {
                result.CandidateArtifacts.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "No Nintendo 3DS artifacts (.3ds/.cci/.cxi/.cia/.app) found in staged content.";
                result.Warnings.Add(result.ErrorMessage);
                return;
            }

            if (candidates.Any(candidate => candidate.RequiresEmulatorImport))
            {
                result.RequiresEmulatorImport = true;
            }

            if (candidates.Any(candidate => candidate.Format == Nintendo3DSContentFormat.Cci)
                || candidates.Any(candidate => candidate.Format == Nintendo3DSContentFormat.ThreeDs))
            {
                logger?.Write(PlatformLogLevel.Info, "Detected 3DS cartridge image inside archive");
            }
        }

        private static void FinalizeSelection(Nintendo3DSGameInspectionResult result, string? gameName, IPlatformLogger? logger)
        {
            var directCandidates = result.CandidateArtifacts
                .Where(candidate => candidate.IsDirectLaunchArtifact)
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var importCandidates = result.CandidateArtifacts
                .Where(candidate => candidate.RequiresEmulatorImport)
                .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directCandidates.Count == 0)
            {
                if (importCandidates.Count > 0)
                {
                    var selectedImport = importCandidates[0];
                    result.IsValid = false;
                    result.RequiresEmulatorImport = true;
                    result.NormalizedFormat = selectedImport.NormalizedFormat;
                    result.TitleId = selectedImport.TitleId;
                    result.TitleName = !string.IsNullOrWhiteSpace(selectedImport.TitleName) ? selectedImport.TitleName : (gameName ?? string.Empty);
                    result.Version = selectedImport.Version;
                    result.ErrorMessage = "CIA/APP package detected. Automatic Azahar/AzaharPlus import is not implemented yet.";
                    result.Warnings.Add(result.ErrorMessage);
                    logger?.Write(PlatformLogLevel.Warning, "Nintendo 3DS package requires emulator import. Automation is deferred until Azahar/AzaharPlus import CLI/API integration is available.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    result.ErrorMessage = "Unable to resolve Nintendo 3DS launch artifact.";
                    result.Warnings.Add(result.ErrorMessage);
                }

                return;
            }

            var selected = directCandidates[0];
            result.IsAmbiguous = directCandidates.Count > 1;
            if (result.IsAmbiguous)
            {
                result.Warnings.Add($"Multiple direct launch artifacts detected ({directCandidates.Count}). Selected '{selected.FileName}'.");
            }

            result.IsValid = true;
            result.LaunchArtifactPath = selected.Path;
            result.NormalizedFormat = selected.NormalizedFormat;
            result.RequiresEmulatorImport = false;
            result.TitleId = selected.TitleId;
            result.TitleName = !string.IsNullOrWhiteSpace(selected.TitleName) ? selected.TitleName : (gameName ?? string.Empty);
            result.Version = selected.Version;

            if (selected.Format == Nintendo3DSContentFormat.Cci)
            {
                logger?.Write(PlatformLogLevel.Info, $"Detected cartridge image: {selected.FileName}");
            }
            else if (selected.Format == Nintendo3DSContentFormat.ThreeDs)
            {
                logger?.Write(PlatformLogLevel.Info, $"Detected cartridge image: {selected.FileName}");
            }
            else if (selected.Format == Nintendo3DSContentFormat.Cxi)
            {
                logger?.Write(PlatformLogLevel.Info, $"Detected executable image: {selected.FileName}");
            }
        }

        private static int CandidatePriority(Nintendo3DSContentCandidate candidate)
        {
            return candidate.Format switch
            {
                Nintendo3DSContentFormat.Cci => 0,
                Nintendo3DSContentFormat.ThreeDs => 1,
                Nintendo3DSContentFormat.Cxi => 2,
                _ => 9
            };
        }

        private static Nintendo3DSContentCandidate BuildCandidate(string path, string extension)
        {
            var format = MapFormat(extension);
            var normalizedFormat = format == Nintendo3DSContentFormat.Cci || format == Nintendo3DSContentFormat.ThreeDs
                ? Nintendo3DSContentFormat.CartridgeImage
                : format;

            var isDirectLaunch = DirectFormats.Contains(extension);
            var requiresImport = ImportFormats.Contains(extension);
            var fileName = Path.GetFileName(path) ?? string.Empty;

            return new Nintendo3DSContentCandidate
            {
                Path = path,
                FileName = fileName,
                Format = format,
                NormalizedFormat = normalizedFormat,
                IsDirectLaunchArtifact = isDirectLaunch,
                RequiresEmulatorImport = requiresImport,
                TitleId = ExtractTitleId(fileName),
                TitleName = InferTitleName(fileName),
                Version = ExtractVersion(fileName)
            };
        }

        private static bool IsSupported3DsExtension(string extension)
        {
            return DirectFormats.Contains(extension)
                || ImportFormats.Contains(extension)
                || ArchiveFormats.Contains(extension);
        }

        private static Nintendo3DSContentFormat MapFormat(string extension)
        {
            return extension switch
            {
                ".3ds" => Nintendo3DSContentFormat.ThreeDs,
                ".cci" => Nintendo3DSContentFormat.Cci,
                ".cxi" => Nintendo3DSContentFormat.Cxi,
                ".cia" => Nintendo3DSContentFormat.Cia,
                ".app" => Nintendo3DSContentFormat.App,
                ".zip" or ".7z" or ".rar" => Nintendo3DSContentFormat.Archive,
                _ => Nintendo3DSContentFormat.Unknown
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

        private static string InferTitleName(string fileName)
        {
            var withoutExt = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            return withoutExt.Replace('_', ' ').Trim();
        }
    }
}

