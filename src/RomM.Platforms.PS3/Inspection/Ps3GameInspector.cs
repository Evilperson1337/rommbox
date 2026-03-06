using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.PS3.Inspection
{
    public sealed class Ps3GameInspector
    {
        private static readonly Regex TitleIdRegex = new("[A-Z]{4}\\d{5}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Ps3GameInspectorResult Inspect(string rootPath, IPlatformLogger? logger)
        {
            var result = new Ps3GameInspectorResult();
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                result.Warnings.Add("PS3 inspection skipped: root path missing.");
                logger?.Write(PlatformLogLevel.Warning, "PS3 inspection skipped: root path missing.");
                return result;
            }

            if (File.Exists(rootPath) && Path.GetExtension(rootPath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                result.Format = Ps3GameFormat.DecryptedIso;
                result.IsoPath = rootPath;
                result.Warnings.Add("PS3 inspection used ISO path directly; PARAM.SFO metadata unavailable.");
                logger?.Write(PlatformLogLevel.Info, $"PS3 inspection detected ISO path '{rootPath}'.");
                return result;
            }

            if (!Directory.Exists(rootPath))
            {
                result.Warnings.Add($"PS3 inspection root missing on disk: '{rootPath}'.");
                logger?.Write(PlatformLogLevel.Warning, $"PS3 inspection root missing on disk: '{rootPath}'.");
                return result;
            }

            logger?.Write(PlatformLogLevel.Info, $"Scanning extracted content for PS3 formats at '{rootPath}'.");

            var ps3GameDirs = SafeEnumerateDirectories(rootPath, "PS3_GAME")
                .Concat(FindDirectoriesCaseInsensitive(rootPath, "PS3_GAME"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var isoFiles = SafeEnumerateFiles(rootPath, "*.iso").ToList();

            if (ps3GameDirs.Count > 0)
            {
                var selection = SelectBestPs3GameRoot(ps3GameDirs);
                result.Format = Ps3GameFormat.JbFolder;
                result.Ps3GamePath = selection.Ps3GamePath;
                result.GameRootPath = selection.GameRootPath;
                result.EbootPath = selection.EbootPath;
                result.ParamSfoPath = selection.ParamSfoPath;
                logger?.Write(PlatformLogLevel.Info, $"PS3_GAME folder detected at '{result.Ps3GamePath}'.");

                if (ps3GameDirs.Count > 1)
                {
                    result.Warnings.Add($"Multiple PS3_GAME folders detected. Using '{selection.Ps3GamePath}'.");
                }

                if (isoFiles.Count > 0)
                {
                    result.Warnings.Add("Both PS3_GAME folders and ISO files detected; defaulted to JB folder format.");
                }

                PopulateMetadataFromSfo(result, logger);
                NormalizePackageKinds(result);
            }
            else if (isoFiles.Count > 0)
            {
                result.Format = Ps3GameFormat.DecryptedIso;
                result.IsoPath = isoFiles.OrderBy(path => path.Length).FirstOrDefault() ?? string.Empty;
                if (isoFiles.Count > 1)
                {
                    result.Warnings.Add($"Multiple ISO files detected. Using '{result.IsoPath}'.");
                }
                logger?.Write(PlatformLogLevel.Info, $"Detected PS3 ISO '{result.IsoPath}'.");
            }
            else
            {
                result.Warnings.Add("PS3 inspection did not find PS3_GAME or ISO content.");
                logger?.Write(PlatformLogLevel.Warning, "WARNING: PS3_GAME folder not found.");
            }

            PopulatePackages(rootPath, result);
            if (result.Format != Ps3GameFormat.Unknown)
            {
                var summary = result.Format == Ps3GameFormat.JbFolder
                    ? $"JB folder detected. PS3_GAME='{result.Ps3GamePath}'."
                    : $"ISO detected at '{result.IsoPath}'.";
                logger?.Write(PlatformLogLevel.Info, summary);
            }
            return result;
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string root, string name)
        {
            try
            {
                return Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> FindDirectoriesCaseInsensitive(string root, string folderName)
        {
            try
            {
                return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetFileName(path), folderName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static (string GameRootPath, string Ps3GamePath, string EbootPath, string ParamSfoPath) SelectBestPs3GameRoot(IEnumerable<string> ps3GamePaths)
        {
            var candidates = ps3GamePaths
                .Select(path =>
                {
                    var root = Directory.GetParent(path)?.FullName ?? string.Empty;
                    var eboot = Path.Combine(path, "USRDIR", "EBOOT.BIN");
                    var sfo = Path.Combine(path, "PARAM.SFO");
                    var score = (File.Exists(sfo) ? 2 : 0) + (File.Exists(eboot) ? 1 : 0);
                    return new
                    {
                        Root = root,
                        Ps3Game = path,
                        Eboot = eboot,
                        Sfo = sfo,
                        Score = score
                    };
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Ps3Game.Length)
                .First();

            return (candidates.Root, candidates.Ps3Game, candidates.Eboot, candidates.Sfo);
        }

        private static void PopulateMetadataFromSfo(Ps3GameInspectorResult result, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(result.ParamSfoPath) || !File.Exists(result.ParamSfoPath))
            {
                result.Warnings.Add("PARAM.SFO missing; metadata extraction skipped.");
                return;
            }

            try
            {
                logger?.Write(PlatformLogLevel.Info, $"PARAM.SFO located at '{result.ParamSfoPath}'.");
                var parser = new ParamSfoParser();
                var data = parser.Parse(result.ParamSfoPath);
                result.TitleId = data.GetString("TITLE_ID");
                result.Title = data.GetString("TITLE");
                result.Version = data.GetString("APP_VER");
                if (string.IsNullOrWhiteSpace(result.Version))
                {
                    result.Version = data.GetString("VERSION");
                }

                result.Region = ResolveRegion(result.TitleId);
                logger?.Write(PlatformLogLevel.Info, $"PS3 PARAM.SFO parsed. TitleId='{result.TitleId}', Title='{result.Title}', Version='{result.Version}'.");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to parse PARAM.SFO: {ex.Message}");
            }
        }

        private static void PopulatePackages(string rootPath, Ps3GameInspectorResult result)
        {
            var dlcPackages = new List<Ps3PackageFile>();
            var updatePackages = new List<Ps3PackageFile>();
            var rapFiles = new List<Ps3RapFile>();
            var pkgFiles = SafeEnumerateFiles(rootPath, "*.pkg");
            foreach (var file in pkgFiles)
            {
                var kind = DetectPackageKind(file);
                var titleId = ExtractTitleId(file);
                var entry = new Ps3PackageFile
                {
                    Path = file,
                    TitleId = titleId,
                    Kind = kind,
                    Region = ResolveRegion(titleId)
                };
                if (kind == Ps3PackageKind.Update)
                {
                    updatePackages.Add(entry);
                }
                else
                {
                    dlcPackages.Add(entry);
                }
            }

            foreach (var rapFile in SafeEnumerateFiles(rootPath, "*.rap"))
            {
                var titleId = ExtractTitleId(rapFile);
                rapFiles.Add(new Ps3RapFile
                {
                    Path = rapFile,
                    TitleId = titleId,
                    Region = ResolveRegion(titleId)
                });
            }

            result.DlcPackages = dlcPackages;
            result.UpdatePackages = updatePackages;
            result.RapFiles = rapFiles;
        }

        private static void NormalizePackageKinds(Ps3GameInspectorResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.TitleId))
            {
                return;
            }

            foreach (var pkg in result.DlcPackages.ToList())
            {
                if (!string.IsNullOrWhiteSpace(pkg.TitleId)
                    && string.Equals(pkg.TitleId, result.TitleId, StringComparison.OrdinalIgnoreCase))
                {
                    pkg.Kind = Ps3PackageKind.Update;
                    result.DlcPackages.Remove(pkg);
                    result.UpdatePackages.Add(pkg);
                }
            }
        }

        private static Ps3PackageKind DetectPackageKind(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (directory.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Ps3PackageKind.Update;
            }

            if (directory.IndexOf("dlc", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Ps3PackageKind.Dlc;
            }

            return Ps3PackageKind.Unknown;
        }

        private static string ExtractTitleId(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(path) ?? string.Empty;
            var match = TitleIdRegex.Match(fileName);
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
        }

        public static Ps3GameRegion ResolveRegion(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId))
            {
                return Ps3GameRegion.Unknown;
            }

            var prefix = titleId.Substring(0, Math.Min(4, titleId.Length)).ToUpperInvariant();
            return prefix switch
            {
                "BLUS" or "NPUB" => Ps3GameRegion.Us,
                "BLES" or "NPEB" => Ps3GameRegion.Eu,
                "BLJM" or "BCJS" or "NPJB" => Ps3GameRegion.Jp,
                "BLAS" or "NPAS" => Ps3GameRegion.Asia,
                _ => Ps3GameRegion.Unknown
            };
        }
    }
}
