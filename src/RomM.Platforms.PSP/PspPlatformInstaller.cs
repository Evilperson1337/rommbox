using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using RomM.Platforms.PSP.Inspection;

namespace RomM.Platforms.PSP
{
    public sealed class PspPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".cso",
            ".chd"
        };

        public string PlatformKey => "psp";
        public string DisplayName => "PlayStation Portable";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "psp", "sony-playstation-portable" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "psp",
            "playstation portable",
            "sony playstation portable",
            "sony psp",
            "playstationportable"
        };

        public PlatformInstallerCapabilities Capabilities => new PlatformInstallerCapabilities
        {
            SupportsArchives = true,
            SupportsDirectFiles = true,
            RequiresStagingInspection = true,
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
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "PspEmulatorMode",
                        Label = "PSP Emulator Mode",
                        Description = "StandalonePPSSPP or RetroArchPPSSPP.",
                        Type = PlatformConfigFieldType.String,
                        Required = false,
                        Advanced = false,
                        DefaultValue = "StandalonePPSSPP"
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "PpssppExecutablePath",
                        Label = "PPSSPP Executable",
                        Description = "Optional standalone PPSSPP executable override.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "RetroArchExecutablePath",
                        Label = "RetroArch Executable",
                        Description = "Optional RetroArch executable override.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "RetroArchPpssppCorePath",
                        Label = "RetroArch PPSSPP Core",
                        Description = "Optional PPSSPP libretro core path or core identifier.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = true
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "ValidateRetroArchPpssppAssets",
                        Label = "Validate RetroArch PPSSPP Assets",
                        Description = "Validate RetroArch system/PPSSPP assets when RetroArch mode is selected.",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "true"
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "FailInstallIfEmulatorNotReady",
                        Label = "Fail If Emulator Not Ready",
                        Description = "Fail install when selected PSP emulator mode does not pass readiness validation.",
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
                result.Errors = new List<DetectionError>
                {
                    new() { Code = "context_missing", Message = "Platform context missing." }
                };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "missing_installed_path", Message = "Installed path missing." }
                };
                return Task.FromResult(result);
            }

            if (!File.Exists(installedPath))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "installed_missing", Message = "Installed PSP artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for PSP detection." }
                };
                return Task.FromResult(result);
            }

            result.RecommendedExecutablePath = installedPath;
            result.CandidateExecutablePaths = new List<string> { installedPath };
            return Task.FromResult(result);
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath)
                && File.Exists(ctx.InstalledPath)
                && SupportedExtensions.Contains(Path.GetExtension(ctx.InstalledPath) ?? string.Empty);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PlayStation Portable install verified." : "PlayStation Portable install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation Portable install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "PlayStation Portable install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new PspGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "PlayStation Portable inspection failed." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "PlayStation Portable launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved PSP launch artifact has unsupported extension '{extension}'." });
            }

            var mode = ResolveEmulatorMode(ctx.Settings, ctx.RomSettings);
            var readiness = ValidateEmulatorReadiness(mode, ctx.Settings, ctx.RomSettings, ctx.Logger);
            if (!readiness.IsReady && readiness.FailInstall)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = readiness.Message });
            }

            var targetFileName = ResolveTargetFileName(sourcePath, ctx.GameName, inspection.TitleName);
            var targetDirectory = GameInstallPathHelper.ResolveGameDirectory(installRoot, ctx.GameName, inspection.TitleName);
            var targetPath = Path.Combine(targetDirectory, targetFileName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved PSP install directory: {targetDirectory}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact: {sourcePath}");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing PlayStation Portable artifact...", 50));
                MoveOrReplace(sourcePath, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"PlayStation Portable install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.Settings, ctx.RomSettings, mode, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, $"PSP emulator mode selected: {mode}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "PlayStation Portable install completed.",
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = targetDirectory
            });
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "PlayStation Portable uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PlayStation Portable uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(installedPath))
            {
                try
                {
                    if (File.Exists(installedPath))
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting installed content: {installedPath}");
                        File.Delete(installedPath);
                        removed++;
                    }
                    else if (Directory.Exists(installedPath))
                    {
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for PlayStation Portable uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"PlayStation Portable uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed PlayStation Portable content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed PlayStation Portable content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PlayStation Portable uninstall completed.",
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
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "PlayStation Portable install directory missing.";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return false;
            }

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
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                logger?.Write(PlatformLogLevel.Warning, $"PlayStation Portable install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        private static string ResolveTargetFileName(string sourcePath, string? gameName, string? detectedTitleName)
        {
            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            var sourceName = Path.GetFileName(sourcePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                return sourceName;
            }

            var baseName = !string.IsNullOrWhiteSpace(gameName) ? gameName : detectedTitleName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "PSP_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), extension);
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PlayStation Portable source file not found.", sourcePath);
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDirectory);
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, $"Source and destination are identical; no move needed: '{targetPath}'.");
                return;
            }

            if (File.Exists(targetPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Removing existing PlayStation Portable artifact at '{targetPath}'.");
                File.Delete(targetPath);
            }

            var sourceRoot = Path.GetPathRoot(sourcePath.Trim()) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetPath.Trim()) ?? string.Empty;
            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                File.Delete(sourcePath);
            }
        }

        private static string BuildLaunchArguments(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings, PspEmulatorMode mode, string romPath)
        {
            var configuredTemplate = romSettings?.LaunchArguments;
            if (!string.IsNullOrWhiteSpace(configuredTemplate))
            {
                return configuredTemplate.Replace("{rom}", QuoteArgument(romPath));
            }

            if (mode == PspEmulatorMode.RetroArchPPSSPP)
            {
                var core = ResolveRetroArchCore(installSettings, romSettings);
                return $"-L {QuoteArgument(core)} {QuoteArgument(romPath)}";
            }

            return QuoteArgument(romPath);
        }

        private static PspEmulatorMode ResolveEmulatorMode(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings)
        {
            var configuredMode = installSettings?.PspEmulatorMode ?? string.Empty;
            if (configuredMode.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PspEmulatorMode.RetroArchPPSSPP;
            }

            if (configuredMode.IndexOf("standalone", StringComparison.OrdinalIgnoreCase) >= 0
                || configuredMode.IndexOf("ppsspp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PspEmulatorMode.StandalonePPSSPP;
            }

            var emulatorName = romSettings?.EmulatorName ?? string.Empty;
            var coreText = string.Join(" ", new[] { romSettings?.CoreName, romSettings?.CorePath }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (emulatorName.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0
                || coreText.IndexOf("ppsspp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PspEmulatorMode.RetroArchPPSSPP;
            }

            return PspEmulatorMode.StandalonePPSSPP;
        }

        private static EmulatorReadinessResult ValidateEmulatorReadiness(PspEmulatorMode mode, PlatformInstallSettings? installSettings, RomInstallSettings? romSettings, IPlatformLogger? logger)
        {
            var failInstall = installSettings?.FailInstallIfEmulatorNotReady ?? false;

            if (mode == PspEmulatorMode.StandalonePPSSPP)
            {
                var exePath = installSettings?.PpssppExecutablePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    logger?.Write(PlatformLogLevel.Info, "PSP emulator mode selected: StandalonePPSSPP");
                    logger?.Write(PlatformLogLevel.Info, "Standalone PPSSPP executable override not set; LaunchBox emulator mapping will be used.");
                    return EmulatorReadinessResult.Ready(failInstall, "");
                }

                if (!File.Exists(exePath))
                {
                    var message = $"Standalone PPSSPP executable not found at '{exePath}'.";
                    logger?.Write(PlatformLogLevel.Warning, message);
                    return EmulatorReadinessResult.NotReady(failInstall, message);
                }

                logger?.Write(PlatformLogLevel.Info, "PSP emulator mode selected: StandalonePPSSPP");
                logger?.Write(PlatformLogLevel.Info, $"Resolved standalone PPSSPP executable: {exePath}");
                return EmulatorReadinessResult.Ready(failInstall, "");
            }

            var retroArchExe = installSettings?.RetroArchExecutablePath ?? string.Empty;
            var core = ResolveRetroArchCore(installSettings, romSettings);
            if (!string.IsNullOrWhiteSpace(retroArchExe) && !File.Exists(retroArchExe))
            {
                var message = $"RetroArch executable not found at '{retroArchExe}'.";
                logger?.Write(PlatformLogLevel.Warning, message);
                return EmulatorReadinessResult.NotReady(failInstall, message);
            }

            logger?.Write(PlatformLogLevel.Info, "PSP emulator mode selected: RetroArchPPSSPP");
            logger?.Write(PlatformLogLevel.Info, $"Resolved RetroArch PPSSPP core: {core}");

            var validateAssets = installSettings?.ValidateRetroArchPpssppAssets ?? true;
            if (!validateAssets)
            {
                logger?.Write(PlatformLogLevel.Info, "RetroArch PPSSPP assets validation disabled.");
                return EmulatorReadinessResult.Ready(failInstall, "");
            }

            if (string.IsNullOrWhiteSpace(retroArchExe))
            {
                logger?.Write(PlatformLogLevel.Warning, "RetroArch executable override not set; cannot validate system/PPSSPP assets. LaunchBox emulator mapping will be used.");
                return EmulatorReadinessResult.Ready(failInstall, "");
            }

            var retroArchDirectory = Path.GetDirectoryName(retroArchExe) ?? string.Empty;
            var assetsDirectory = Path.Combine(retroArchDirectory, "system", "PPSSPP");
            var assetsReady = Directory.Exists(assetsDirectory)
                && Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories).Any();

            logger?.Write(PlatformLogLevel.Info, $"Validated RetroArch PPSSPP assets: {assetsReady}");
            if (assetsReady)
            {
                return EmulatorReadinessResult.Ready(failInstall, "");
            }

            var error = $"RetroArch PPSSPP assets missing. Expected files under '{assetsDirectory}'.";
            logger?.Write(PlatformLogLevel.Warning, error);
            return EmulatorReadinessResult.NotReady(failInstall, error);
        }

        private static string ResolveRetroArchCore(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings)
        {
            var configuredCore = installSettings?.RetroArchPpssppCorePath;
            if (!string.IsNullOrWhiteSpace(configuredCore))
            {
                return configuredCore;
            }

            if (!string.IsNullOrWhiteSpace(romSettings?.CorePath))
            {
                return romSettings.CorePath;
            }

            if (!string.IsNullOrWhiteSpace(romSettings?.CoreName))
            {
                return romSettings.CoreName;
            }

            return "ppsspp_libretro.dll";
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private enum PspEmulatorMode
        {
            StandalonePPSSPP,
            RetroArchPPSSPP
        }

        private readonly struct EmulatorReadinessResult
        {
            public bool IsReady { get; }
            public bool FailInstall { get; }
            public string Message { get; }

            private EmulatorReadinessResult(bool isReady, bool failInstall, string message)
            {
                IsReady = isReady;
                FailInstall = failInstall;
                Message = message;
            }

            public static EmulatorReadinessResult Ready(bool failInstall, string message) => new(true, failInstall, message);
            public static EmulatorReadinessResult NotReady(bool failInstall, string message) => new(false, failInstall, message);
        }
    }
}

