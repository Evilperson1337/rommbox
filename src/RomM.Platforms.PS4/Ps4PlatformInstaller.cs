using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.PS4.Inspection;

namespace RomM.Platforms.PS4
{
    public sealed class Ps4PlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        public string PlatformKey => "ps4";
        public string DisplayName => "PlayStation 4";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "ps4", "sony-playstation-4" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "ps4",
            "playstation4",
            "playstation 4",
            "sony playstation 4",
            "sonyplaystation4"
        };

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
            SupportsDlc = true,
            SupportsUpdates = true,
            SupportsRaps = false,
            RequiresEmulatorPath = false
        };

        public PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "ShadPs4ExecutablePath",
                        Label = "ShadPS4 Executable",
                        Description = "Optional ShadPS4 executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4GamesDirectory",
                        Label = "PS4 Games Directory",
                        Description = "Optional PS4 install directory override used for serial-based layout.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4ExternalPkgExtractorPath",
                        Label = "External PKG Extractor",
                        Description = "Optional external extractor used for direct .pkg downloads.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = true
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "Ps4FailIfDirectPkgExtractorMissing",
                        Label = "Fail if direct PKG extractor is missing",
                        Description = "If enabled, direct PKG download fails immediately when extractor path is missing.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
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

            if (Directory.Exists(installedPath))
            {
                result.RecommendedExecutablePath = installedPath;
                result.CandidateExecutablePaths = new List<string> { installedPath };
                return Task.FromResult(result);
            }

            if (File.Exists(installedPath))
            {
                var directory = Path.GetDirectoryName(installedPath) ?? string.Empty;
                result.RecommendedExecutablePath = !string.IsNullOrWhiteSpace(directory) ? directory : installedPath;
                result.CandidateExecutablePaths = new List<string> { result.RecommendedExecutablePath };
                return Task.FromResult(result);
            }

            result.IsInstalled = false;
            result.Warnings = new List<DetectionWarning> { new() { Code = "installed_missing", Message = "Installed PS4 content not found on disk." } };
            return Task.FromResult(result);
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var basePath = ResolveBasePath(ctx?.InstalledPath, ctx?.InstallRootPath);
            var valid = !string.IsNullOrWhiteSpace(basePath) && Directory.Exists(basePath);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PS4 install verified." : "PS4 install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation 4 install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.Settings?.Ps4GamesDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "PS4 install directory missing." });
            }

            Directory.CreateDirectory(installRoot);

            var extractorPath = ctx.Settings?.Ps4ExternalPkgExtractorPath ?? string.Empty;
            var extractorConfigured = !string.IsNullOrWhiteSpace(extractorPath) && File.Exists(extractorPath);
            var directPkgSource = IsDirectPkg(ctx.ArchivePath);
            var directPkgSupport = directPkgSource && extractorConfigured;
            var archiveExtractionEnabled = ctx.RomSettings?.ExtractArchives == true || !string.IsNullOrWhiteSpace(ctx.ExtractedPath);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS4 install started. InstallRoot='{installRoot}', ArchivePath='{ctx.ArchivePath ?? string.Empty}', ExtractedPath='{ctx.ExtractedPath ?? string.Empty}'.");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Archive extraction enabled: {archiveExtractionEnabled}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"PKG support enabled: {directPkgSupport}");

            var inspector = new Ps4GameInspector();
            var inspection = inspector.Inspect(
                ctx.ArchivePath,
                ctx.ExtractedPath,
                ctx.GameName,
                new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = archiveExtractionEnabled,
                    DirectPkgSupportEnabled = directPkgSupport,
                    AllowExtractedPkgInstall = false
                },
                ctx.Logger);

            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                if (directPkgSource && !extractorConfigured && ctx.Settings?.Ps4FailIfDirectPkgExtractorMissing == true)
                {
                    return Task.FromResult(new InstallResult
                    {
                        Success = false,
                        Message = "Direct PKG detected but external extractor path is not configured."
                    });
                }

                return Task.FromResult(new InstallResult
                {
                    Success = false,
                    Message = inspection.ErrorMessage ?? "PS4 inspection failed."
                });
            }

            var titleId = inspection.TitleId;
            if (string.IsNullOrWhiteSpace(titleId))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Unable to resolve PS4 Title ID." });
            }

            var baseTarget = Path.Combine(installRoot, titleId);
            var patchTarget = Path.Combine(installRoot, titleId + "-patch");
            var dlcTarget = Path.Combine(installRoot, titleId + "-dlc");

            progress?.Report(new InstallProgress("Installing", "Installing base game...", 25));
            var baseItem = inspection.BaseGameItems.First(item => item.IsSupportedForInstall);
            if (!InstallCandidate(baseItem, baseTarget, extractorPath, ctx.Logger, out var baseError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = baseError });
            }

            progress?.Report(new InstallProgress("Installing", "Installing updates...", 55));
            foreach (var update in inspection.UpdateItems.Where(item => item.IsSupportedForInstall).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!InstallCandidate(update, patchTarget, extractorPath, ctx.Logger, out var updateError))
                {
                    return Task.FromResult(new InstallResult { Success = false, Message = updateError });
                }
            }

            progress?.Report(new InstallProgress("Installing", "Installing DLC...", 75));
            foreach (var dlc in inspection.DlcItems.Where(item => item.IsSupportedForInstall).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!InstallCandidate(dlc, dlcTarget, extractorPath, ctx.Logger, out var dlcError))
                {
                    return Task.FromResult(new InstallResult { Success = false, Message = dlcError });
                }
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing extracted base game into {titleId}/");
            if (Directory.Exists(patchTarget))
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing extracted update into {titleId}-patch/");
            }
            if (Directory.Exists(dlcTarget))
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing extracted DLC into {titleId}-dlc/");
            }
            ctx.Logger?.Write(PlatformLogLevel.Info, "Application path resolved");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "PlayStation 4 install completed.",
                ExecutablePath = baseTarget,
                Arguments = Array.Empty<string>(),
                InstallType = InstallType.Portable,
                InstallRootPath = installRoot
            });
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "PS4 uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PS4 uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var basePath = ResolveBasePath(ctx.InstalledPath, ctx.InstallRootPath);
            var titleId = ExtractTitleId(Path.GetFileName(basePath ?? string.Empty));
            if (string.IsNullOrWhiteSpace(titleId))
            {
                notes.Add("Unable to determine PS4 title id for uninstall scope.");
            }
            else
            {
                var platformRoot = Path.GetDirectoryName(basePath ?? string.Empty) ?? ctx.InstallRootPath ?? string.Empty;
                var targets = new[]
                {
                    Path.Combine(platformRoot, titleId),
                    Path.Combine(platformRoot, titleId + "-patch"),
                    Path.Combine(platformRoot, titleId + "-dlc")
                };

                foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(target))
                    {
                        continue;
                    }

                    try
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Removing installed serial folder: {target}");
                        Directory.Delete(target, true);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        var message = $"Failed to remove '{target}': {ex.Message}";
                        notes.Add(message);
                        ctx.Logger?.Write(PlatformLogLevel.Warning, message);
                    }
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Cleaning install state");

            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PlayStation 4 uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static bool InstallCandidate(Ps4ContentCandidate candidate, string targetDirectory, string extractorPath, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            try
            {
                if (candidate.ContentFormat == Ps4ContentFormat.ExtractedFolder)
                {
                    DirectoryCopy(candidate.Path, targetDirectory, overwrite: true);
                    return true;
                }

                if (candidate.ContentFormat == Ps4ContentFormat.Pkg)
                {
                    if (string.IsNullOrWhiteSpace(extractorPath) || !File.Exists(extractorPath))
                    {
                        error = "External PKG extractor path is missing.";
                        return false;
                    }

                    Directory.CreateDirectory(targetDirectory);
                    logger?.Write(PlatformLogLevel.Info, "Invoking external extractor for direct PKG");
                    var psi = new ProcessStartInfo
                    {
                        FileName = extractorPath,
                        Arguments = $"\"{candidate.Path}\" \"{targetDirectory}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        error = "Failed to start PKG extractor process.";
                        return false;
                    }

                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        error = $"PKG extractor failed with exit code {process.ExitCode}.";
                        return false;
                    }

                    if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                    {
                        error = "PKG extractor completed but produced no output.";
                        return false;
                    }

                    logger?.Write(PlatformLogLevel.Info, $"Verified output at: {targetDirectory}");
                    return true;
                }

                error = $"Unsupported PS4 candidate format: {candidate.ContentFormat}.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ResolveInstallRoot(string? installDirectory, string? ps4GamesDirectory, string? romRootPath)
        {
            if (!string.IsNullOrWhiteSpace(ps4GamesDirectory))
            {
                return ps4GamesDirectory;
            }

            if (!string.IsNullOrWhiteSpace(romRootPath))
            {
                return romRootPath;
            }

            return installDirectory ?? string.Empty;
        }

        private static bool IsDirectPkg(string? archivePath)
        {
            return !string.IsNullOrWhiteSpace(archivePath)
                   && File.Exists(archivePath)
                   && string.Equals(Path.GetExtension(archivePath), ".pkg", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveBasePath(string? installedPath, string? installRootPath)
        {
            if (!string.IsNullOrWhiteSpace(installedPath) && Directory.Exists(installedPath))
            {
                return installedPath;
            }

            if (!string.IsNullOrWhiteSpace(installedPath) && File.Exists(installedPath))
            {
                return Path.GetDirectoryName(installedPath) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(installRootPath) && Directory.Exists(installRootPath))
            {
                return installRootPath;
            }

            return string.Empty;
        }

        private static string ExtractTitleId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var upper = text.ToUpperInvariant();
            var markers = new[] { "CUSA", "CUSB", "CUSC", "PCAS", "PCJS", "PPSA", "PPSH" };
            foreach (var marker in markers)
            {
                var index = upper.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0 || index + 9 > upper.Length)
                {
                    continue;
                }

                var candidate = upper.Substring(index, 9);
                if (candidate.Skip(4).All(char.IsDigit))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static void DirectoryCopy(string sourceDir, string destinationDir, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"Source directory '{sourceDir}' not found.");
            }

            Directory.CreateDirectory(destinationDir);

            foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, filePath);
                var target = Path.Combine(destinationDir, relative);
                var targetDir = Path.GetDirectoryName(target) ?? destinationDir;
                Directory.CreateDirectory(targetDir);
                File.Copy(filePath, target, overwrite);
            }
        }
    }
}

