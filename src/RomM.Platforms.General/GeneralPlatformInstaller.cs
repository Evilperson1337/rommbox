using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Install;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;

namespace RomM.Platforms.General
{
    public sealed class GeneralPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformConfigurationProvider
    {
        private static readonly string[] DefaultSupportedExtensions =
        {
            ".zip", ".rom", ".bin", ".iso", ".cue", ".chd", ".sfc", ".smc", ".z64", ".n64", ".v64", ".m3u", ".ccd", ".pbp"
        };

        private static readonly string[] DefaultPreferredLaunchExtensions =
        {
            ".m3u", ".cue", ".ccd", ".chd", ".iso", ".pbp", ".z64", ".n64", ".v64", ".sfc", ".smc", ".rom", ".bin", ".zip"
        };

        public string PlatformKey => "general";
        public string PluginKey => PlatformKey;
        public string DisplayName => "General Platform";

        public PlatformInstallerCapabilities Capabilities => new PlatformInstallerCapabilities
        {
            SupportsArchives = true,
            SupportsDirectFiles = true,
            RequiresStagingInspection = false,
            SupportsAutoFormatDetection = true,
            SupportsInstaller = false,
            SupportsSilentInstaller = false,
            SupportsUninstall = true,
            SupportsInstallStateDetection = true,
            SupportsApplicationPathDiscovery = true,
            SupportsDlc = false,
            SupportsUpdates = false,
            SupportsRaps = false,
            RequiresEmulatorPath = false
        };

        public PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new()
                    {
                        Key = "ExtractArchives",
                        Label = "Extract Archives",
                        Description = "Archive handling mode for general ROM installs.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "false"
                    },
                    new()
                    {
                        Key = "ArchiveHandlingMode",
                        Label = "Archive Handling",
                        Description = "NeverExtract, ExtractForInspection, or ExtractAlways.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "NeverExtract"
                    },
                    new()
                    {
                        Key = "SupportedFileTypes",
                        Label = "Supported File Types",
                        Description = "Comma-separated launch candidate extensions, e.g. .cue,.bin,.chd,.iso,.zip.",
                        Type = PlatformConfigFieldType.String,
                        Required = true,
                        Advanced = false,
                        DefaultValue = ".zip,.rom,.bin,.iso,.cue,.chd,.sfc,.smc,.z64,.n64,.v64"
                    },
                    new()
                    {
                        Key = "PreferredLaunchExtensions",
                        Label = "Preferred Launch Extensions",
                        Description = "Optional extension priority for launch artifact selection.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = true,
                        DefaultValue = ".m3u,.cue,.chd,.iso,.zip"
                    },
                    new()
                    {
                        Key = "InstallFromArchiveDirectly",
                        Label = "Install From Archive Directly",
                        Description = "If enabled, supported archives can be installed and launched directly.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "false"
                    },
                    new()
                    {
                        Key = "InstallLayoutMode",
                        Label = "Install Layout",
                        Description = "UsePlatformRoot, CreatePerGameSubfolder, or PreserveArchiveStructure.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "CreatePerGameSubfolder"
                    },
                    new()
                    {
                        Key = "ArtifactSelectionMode",
                        Label = "Artifact Selection",
                        Description = "FirstSupportedFile, LargestSupportedFile, or ExtensionPriority.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "ExtensionPriority"
                    },
                    new()
                    {
                        Key = "UseGameSubdirectory",
                        Label = "Use Game Subdirectory",
                        Description = "Legacy compatibility toggle; superseded by InstallLayoutMode.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "false"
                    },
                    new()
                    {
                        Key = "InstallAllMatchingFiles",
                        Label = "Install All Matching Files",
                        Description = "Install all discovered candidate files. If disabled, install launch artifact + required companions.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "true"
                    },
                    new()
                    {
                        Key = "UseGeneralFallbackInstaller",
                        Label = "Enable General Fallback Installer",
                        Description = "Allows this platform mapping to use GeneralPlatformInstaller when no dedicated installer exists.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "false"
                    }
                }
            };
        }

        public Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError> { new() { Code = "context_missing", Message = "Platform context missing." } };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "missing_installed_path", Message = "Installed path missing." } };
                return Task.FromResult(result);
            }

            if (!File.Exists(installedPath))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning> { new() { Code = "installed_missing", Message = "Installed artifact not found on disk." } };
                return Task.FromResult(result);
            }

            result.RecommendedExecutablePath = installedPath;
            result.CandidateExecutablePaths = new List<string> { installedPath };
            return Task.FromResult(result);
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath) && File.Exists(ctx.InstalledPath);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "General install verified." : "General install missing on disk."
            });
        }

        public async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return new InstallResult { Success = false, Message = "Install context missing." };
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing general install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return new InstallResult { Success = false, Message = "Install directory missing." };
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return new InstallResult { Success = false, Message = writableError };
            }

            var options = ResolveOptions(ctx);
            LogOptions(options, ctx.Logger);

            var source = ResolveSourceArtifact(ctx, ctx.Logger, out var sourceError);
            if (string.IsNullOrWhiteSpace(source))
            {
                return new InstallResult { Success = false, Message = sourceError };
            }

            var sourceExtension = NormalizeExtension(Path.GetExtension(source));
            if (!options.SupportedExtensions.Contains(sourceExtension))
            {
                return new InstallResult
                {
                    Success = false,
                    Message = $"Downloaded artifact extension '{sourceExtension}' is not allowed by SupportedFileTypes."
                };
            }

            if (!string.IsNullOrWhiteSpace(ctx.ArchivePath) && File.Exists(ctx.ArchivePath))
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing ROM artifact: '{Path.GetFileName(ctx.ArchivePath)}'.");
            }

            var installFolder = ResolveInstallFolder(installRoot, ctx.GameName, options);

            Directory.CreateDirectory(installFolder);
            progress?.Report(new InstallProgress("Installing", "Installing artifact to final location...", 60));

            ct.ThrowIfCancellationRequested();
            var executablePath = Path.Combine(installFolder, Path.GetFileName(source));
            MoveOrReplace(source, executablePath, ctx.Logger);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: '{executablePath}'.");
            progress?.Report(new InstallProgress("Installing", "General install completed.", 100));

            return new InstallResult
            {
                Success = true,
                Message = "General install completed.",
                ExecutablePath = executablePath,
                Arguments = BuildLaunchArguments(ctx.RomSettings, executablePath),
                InstallType = InstallType.Portable,
                InstallRootPath = installFolder
            };
        }

        public PlatformConfigDescriptor GetConfigurationSchema() => GetConfigDescriptor() ?? new PlatformConfigDescriptor();

        public IReadOnlyDictionary<string, string> GetDefaultValues()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExtractArchives"] = "false",
                ["ArchiveHandlingMode"] = "NeverExtract",
                ["SupportedFileTypes"] = ".zip,.rom,.bin,.iso,.cue,.chd,.sfc,.smc,.z64,.n64,.v64",
                ["PreferredLaunchExtensions"] = ".m3u,.cue,.chd,.iso,.zip",
                ["InstallLayoutMode"] = "CreatePerGameSubfolder",
                ["ArtifactSelectionMode"] = "ExtensionPriority",
                ["UseGameSubdirectory"] = "false",
                ["InstallAllMatchingFiles"] = "false",
                ["InstallFromArchiveDirectly"] = "true",
                ["UseGeneralFallbackInstaller"] = "true"
            };
        }

        public IReadOnlyDictionary<string, string> GetUiMetadata()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["category"] = "GenericRom",
                ["displayName"] = "General ROM Plugin"
            };
        }

        public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string> settings)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                return errors;
            }

            if (settings.TryGetValue("SupportedFileTypes", out var types)
                && string.IsNullOrWhiteSpace(types))
            {
                errors.Add("SupportedFileTypes cannot be empty for general plugin mappings.");
            }

            return errors;
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "General uninstall started.", 0, true));

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            var installRootPath = ctx.InstallRootPath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(installedPath) && File.Exists(installedPath))
            {
                try
                {
                    File.Delete(installedPath);
                    removed++;
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed artifact '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed artifact '{installedPath}': {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(installRootPath)
                && Directory.Exists(installRootPath)
                && IsLikelyGameOwnedInstallRoot(installRootPath, ctx.GameName))
            {
                try
                {
                    ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved uninstall root: '{installRootPath}'.");
                    Directory.Delete(installRootPath, recursive: true);
                    removed++;
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete game install root '{installRootPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete game install root '{installRootPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "General uninstall completed.", 100, false));
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "General uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static string ResolveInstallRoot(string? installDirectory, string? romRootPath)
        {
            if (!string.IsNullOrWhiteSpace(romRootPath))
            {
                return romRootPath;
            }

            return installDirectory ?? string.Empty;
        }

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            try
            {
                Directory.CreateDirectory(directory);
                var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
                using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Install directory not writable: {ex.Message}";
                logger?.Write(PlatformLogLevel.Warning, error);
                return false;
            }
        }

        private static GeneralOptions ResolveOptions(InstallContext ctx)
        {
            var settings = ctx.RomSettings;
            var supported = ParseExtensions(settings?.SupportedFileTypes, DefaultSupportedExtensions);
            var preferred = ParseExtensions(settings?.PreferredLaunchExtensions, DefaultPreferredLaunchExtensions);
            var archiveHandlingMode = ParseArchiveHandlingMode(settings);
            var installLayoutMode = ParseInstallLayoutMode(settings);
            var artifactSelectionMode = ParseArtifactSelectionMode(settings);

            return new GeneralOptions(
                supported,
                preferred,
                settings?.ExtractArchives ?? false,
                settings?.InstallFromArchiveDirectly ?? false,
                settings?.UseGameSubdirectory ?? false,
                settings?.InstallAllMatchingFiles ?? false,
                archiveHandlingMode,
                installLayoutMode,
                artifactSelectionMode);
        }

        private static void LogOptions(GeneralOptions options, IPlatformLogger? logger)
        {
            logger?.Write(PlatformLogLevel.Info, $"General ROM plugin selected. ArchiveHandling={options.ArchiveHandlingMode}, InstallLayout={options.InstallLayoutMode}, ArtifactSelection={options.ArtifactSelectionMode}.");
            logger?.Write(PlatformLogLevel.Info, $"Legacy flags: ExtractArchives={options.ExtractArchives}, InstallFromArchiveDirectly={options.InstallFromArchiveDirectly}, UseGameSubdirectory={options.UseGameSubdirectory}, InstallAllMatchingFiles={options.InstallAllMatchingFiles}.");
            logger?.Write(PlatformLogLevel.Info, $"Supported extensions: {string.Join(",", options.SupportedExtensions)}");
            if (options.PreferredLaunchExtensions.Count > 0)
            {
                logger?.Write(PlatformLogLevel.Info, $"Preferred launch extensions: {string.Join(",", options.PreferredLaunchExtensions)}");
            }
        }

        private static string ResolveSourceArtifact(InstallContext ctx, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (!string.IsNullOrWhiteSpace(ctx.ArchivePath) && File.Exists(ctx.ArchivePath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Using downloaded artifact: '{ctx.ArchivePath}'.");
                return ctx.ArchivePath;
            }

            if (!string.IsNullOrWhiteSpace(ctx.ExtractedPath) && File.Exists(ctx.ExtractedPath))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Downloaded artifact missing; using extracted file as fallback: '{ctx.ExtractedPath}'.");
                return ctx.ExtractedPath;
            }

            error = "No downloaded content available for install.";
            return string.Empty;
        }

        private static string ResolveInstallFolder(string installRoot, string? gameName, GeneralOptions options)
        {
            return GameInstallPathHelper.ResolveGameDirectory(installRoot, gameName);
        }

        private static ArchiveHandlingMode ParseArchiveHandlingMode(RomInstallSettings? settings)
        {
            if (settings == null)
            {
                return ArchiveHandlingMode.NeverExtract;
            }

            if (Enum.TryParse(settings.ArchiveHandlingMode ?? string.Empty, ignoreCase: true, out ArchiveHandlingMode mode))
            {
                return mode;
            }

            return settings.ExtractArchives == true
                ? ArchiveHandlingMode.ExtractAlways
                : ArchiveHandlingMode.NeverExtract;
        }

        private static InstallLayoutMode ParseInstallLayoutMode(RomInstallSettings? settings)
        {
            if (settings == null)
            {
                return InstallLayoutMode.CreatePerGameSubfolder;
            }

            if (Enum.TryParse(settings.InstallLayoutMode ?? string.Empty, ignoreCase: true, out InstallLayoutMode mode))
            {
                return mode;
            }

            return settings.UseGameSubdirectory == true
                ? InstallLayoutMode.CreatePerGameSubfolder
                : InstallLayoutMode.CreatePerGameSubfolder;
        }

        private static ArtifactSelectionMode ParseArtifactSelectionMode(RomInstallSettings? settings)
        {
            if (settings == null)
            {
                return ArtifactSelectionMode.ExtensionPriority;
            }

            if (Enum.TryParse(settings.ArtifactSelectionMode ?? string.Empty, ignoreCase: true, out ArtifactSelectionMode mode))
            {
                return mode;
            }

            return ArtifactSelectionMode.ExtensionPriority;
        }

        private static InspectionResult InspectSource(string sourcePath, GeneralOptions options, IPlatformLogger? logger)
        {
            var candidates = new List<CandidateFile>();
            if (File.Exists(sourcePath))
            {
                var ext = NormalizeExtension(Path.GetExtension(sourcePath));
                if (options.SupportedExtensions.Contains(ext))
                {
                    candidates.Add(new CandidateFile(sourcePath, ext, 0, SafeGetLength(sourcePath)));
                }
                return new InspectionResult(Path.GetDirectoryName(sourcePath) ?? string.Empty, candidates);
            }

            var root = sourcePath;
            var rootNormalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var ext = NormalizeExtension(Path.GetExtension(file));
                if (!options.SupportedExtensions.Contains(ext))
                {
                    continue;
                }

                var depth = ComputeDepth(rootNormalized, Path.GetFullPath(file));
                candidates.Add(new CandidateFile(file, ext, depth, SafeGetLength(file)));
            }

            logger?.Write(PlatformLogLevel.Info, $"Candidate files discovered: {candidates.Count}.");
            foreach (var candidate in candidates)
            {
                logger?.Write(PlatformLogLevel.Info, $"Candidate: '{candidate.Path}'");
            }

            return new InspectionResult(root, candidates);
        }

        private static CandidateFile? SelectLaunchCandidate(IReadOnlyList<CandidateFile> candidates, GeneralOptions options, IPlatformLogger? logger)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var preference = options.PreferredLaunchExtensions
                .Select((ext, index) => new { ext, index })
                .ToDictionary(item => item.ext, item => item.index, StringComparer.OrdinalIgnoreCase);

            var selected = candidates
                .OrderBy(candidate => preference.TryGetValue(candidate.Extension, out var rank) ? rank : int.MaxValue)
                .ThenBy(candidate => candidate.Depth)
                .ThenByDescending(candidate => candidate.Length)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (selected != null)
            {
                logger?.Write(PlatformLogLevel.Info, $"Selected launch artifact: '{selected.Path}'.");
            }

            return selected;
        }

        private static IReadOnlyList<string> ResolveOwnedFiles(
            string sourceRoot,
            CandidateFile selected,
            IReadOnlyList<CandidateFile> candidates,
            GeneralOptions options,
            IPlatformLogger? logger)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (options.InstallAllMatchingFiles)
            {
                foreach (var candidate in candidates)
                {
                    files.Add(candidate.Path);
                }
            }
            else
            {
                files.Add(selected.Path);
            }

            foreach (var companion in ResolveCompanionFiles(selected.Path, sourceRoot))
            {
                files.Add(companion);
            }

            logger?.Write(PlatformLogLevel.Info, $"Installing {files.Count} file(s) to final location.");
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IEnumerable<string> ResolveCompanionFiles(string selectedPath, string sourceRoot)
        {
            var ext = NormalizeExtension(Path.GetExtension(selectedPath));
            if (string.Equals(ext, ".cue", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var referenced in ParseCueReferences(selectedPath))
                {
                    if (File.Exists(referenced))
                    {
                        yield return referenced;
                    }
                }

                yield break;
            }

            if (string.Equals(ext, ".ccd", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.Combine(Path.GetDirectoryName(selectedPath) ?? string.Empty, Path.GetFileNameWithoutExtension(selectedPath));
                var img = baseName + ".img";
                var sub = baseName + ".sub";
                if (File.Exists(img))
                {
                    yield return img;
                }
                if (File.Exists(sub))
                {
                    yield return sub;
                }

                yield break;
            }

            if (string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadAllLines(selectedPath))
                {
                    var value = line?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var resolved = Path.IsPathRooted(value)
                        ? value
                        : Path.Combine(Path.GetDirectoryName(selectedPath) ?? sourceRoot, value);
                    if (File.Exists(resolved))
                    {
                        yield return resolved;
                        foreach (var nestedCompanion in ResolveCompanionFiles(resolved, sourceRoot))
                        {
                            yield return nestedCompanion;
                        }
                    }
                }
            }
        }

        private static IEnumerable<string> ParseCueReferences(string cuePath)
        {
            if (string.IsNullOrWhiteSpace(cuePath) || !File.Exists(cuePath))
            {
                yield break;
            }

            var folder = Path.GetDirectoryName(cuePath) ?? string.Empty;
            var regex = new Regex("^\\s*FILE\\s+\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (var line in File.ReadAllLines(cuePath))
            {
                var match = regex.Match(line ?? string.Empty);
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var resolved = Path.IsPathRooted(name) ? name : Path.Combine(folder, name);
                yield return resolved;
            }
        }

        private static string ResolveTargetPath(string sourceFile, string sourceRoot, string installRoot)
        {
            if (!Directory.Exists(sourceRoot))
            {
                return Path.Combine(installRoot, Path.GetFileName(sourceFile));
            }

            var fullSourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullSourceFile = Path.GetFullPath(sourceFile);
            if (fullSourceFile.StartsWith(fullSourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = fullSourceFile.Substring(fullSourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.Combine(installRoot, relative);
            }

            return Path.Combine(installRoot, Path.GetFileName(sourceFile));
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDirectory);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            var sourceRoot = Path.GetPathRoot(sourcePath) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetPath) ?? string.Empty;
            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                File.Delete(sourcePath);
            }

            logger?.Write(PlatformLogLevel.Info, $"Installed '{sourcePath}' -> '{targetPath}'.");
        }

        private static IReadOnlyList<string> BuildLaunchArguments(RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings? settings, string executablePath)
        {
            var template = settings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "{rom}";
            }

            var args = template.Replace("{rom}", QuoteArgument(executablePath));
            return string.IsNullOrWhiteSpace(args) ? Array.Empty<string>() : new[] { args };
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static bool IsLikelyGameOwnedInstallRoot(string installRootPath, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(installRootPath) || !Directory.Exists(installRootPath))
            {
                return false;
            }

            var expected = NormalizePathSegment(gameName);
            var folder = Path.GetFileName(installRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.Equals(expected, folder, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePathSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
        }

        private static HashSet<string> ParseExtensions(string? raw, IEnumerable<string> defaults)
        {
            var values = string.IsNullOrWhiteSpace(raw)
                ? defaults
                : raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            return values
                .Select(NormalizeExtension)
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeExtension(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (!trimmed.StartsWith(".", StringComparison.Ordinal))
            {
                trimmed = "." + trimmed;
            }

            return trimmed.ToLowerInvariant();
        }

        private static int ComputeDepth(string rootPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(filePath))
            {
                return 0;
            }

            if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var relative = filePath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
            {
                return 0;
            }

            return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
        }

        private static long SafeGetLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private sealed record GeneralOptions(
            HashSet<string> SupportedExtensions,
            HashSet<string> PreferredLaunchExtensions,
            bool ExtractArchives,
            bool InstallFromArchiveDirectly,
            bool UseGameSubdirectory,
            bool InstallAllMatchingFiles,
            ArchiveHandlingMode ArchiveHandlingMode,
            InstallLayoutMode InstallLayoutMode,
            ArtifactSelectionMode ArtifactSelectionMode);

        private enum ArchiveHandlingMode
        {
            NeverExtract = 0,
            ExtractForInspection = 1,
            ExtractAlways = 2
        }

        private enum InstallLayoutMode
        {
            UsePlatformRoot = 0,
            CreatePerGameSubfolder = 1,
            PreserveArchiveStructure = 2
        }

        private enum ArtifactSelectionMode
        {
            FirstSupportedFile = 0,
            LargestSupportedFile = 1,
            ExtensionPriority = 2
        }

        private sealed record CandidateFile(string Path, string Extension, int Depth, long Length);

        private sealed record InspectionResult(string SourceRootPath, IReadOnlyList<CandidateFile> Candidates);
    }
}
