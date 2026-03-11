using System;
using System.Collections.Generic;
using System.IO;
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
using RomM.Platforms.DolphinInternal;
using RomM.Platforms.Wii.Inspection;

namespace RomM.Platforms.Wii
{
    public sealed class WiiPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".wbfs",
            ".gcz",
            ".ciso",
            ".wia",
            ".rvz"
        };

        public string PlatformKey => "wii";
        public string DisplayName => "Nintendo Wii";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "wii" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "wii",
            "nintendo wii",
            "nintendowii"
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
                        Key = "DolphinExecutablePath",
                        Label = "Dolphin Executable",
                        Description = "Optional Dolphin executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
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
                    new() { Code = "installed_missing", Message = "Installed Nintendo Wii artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for Nintendo Wii detection." }
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
                Message = valid ? "Nintendo Wii install verified." : "Nintendo Wii install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Nintendo Wii install...", 0));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Nintendo Wii install started");

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Wii install directory missing." });
            }

            if (!DolphinInstallHelpers.EnsureDirectoryWritable(installRoot, "Nintendo Wii", ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new WiiGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                var message = inspection.ErrorMessage;
                if (string.Equals(message, "Unable to resolve canonical Dolphin launch artifact.", StringComparison.Ordinal))
                {
                    message = "Unable to resolve Nintendo Wii launch artifact.";
                }

                return Task.FromResult(new InstallResult { Success = false, Message = message ?? "Nintendo Wii inspection failed." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Wii launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved Wii launch artifact has unsupported extension '{extension}'." });
            }

            var targetFileName = ResolveTargetFileName(sourcePath, ctx.GameName, inspection.TitleName);
            var targetDirectory = ResolveInstallDirectory(installRoot, ctx.GameName, inspection.TitleName);
            var targetPath = Path.Combine(targetDirectory, targetFileName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Wii install directory: {targetDirectory}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact: {sourcePath}");
            if (!string.IsNullOrWhiteSpace(inspection.TitleId))
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Inspection metadata TitleId={inspection.TitleId}, Region={inspection.Region}, Revision={inspection.Revision}");
            }

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Nintendo Wii artifact...", 50));
                DolphinInstallHelpers.MoveOrReplace(sourcePath, targetPath, "Nintendo Wii", ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Wii install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = DolphinInstallHelpers.BuildLaunchArguments(ctx.RomSettings?.LaunchArguments, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: Dolphin");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Nintendo Wii install completed.",
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
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
            progress?.Report(new InstallProgress("Uninstall", "Nintendo Wii uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Nintendo Wii uninstall started");

            var removed = 0;
            var notes = new List<string>();
            removed += DolphinInstallHelpers.DeleteInstalledArtifactOnly(ctx.InstalledPath, "Nintendo Wii", ctx.Logger, notes);

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Nintendo Wii uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static string ResolveInstallRoot(string? installDirectory, string? romRootPath)
        {
            return DolphinInstallHelpers.ResolveInstallRoot(installDirectory, romRootPath);
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
                baseName = "Wii_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), extension);
        }

        private static string ResolveInstallDirectory(string installRoot, string? gameName, string? detectedTitleName)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return installRoot;
            }

            var folderName = !string.IsNullOrWhiteSpace(gameName) ? gameName : detectedTitleName;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return installRoot;
            }

            var normalized = NormalizePathSegment(folderName);
            return string.IsNullOrWhiteSpace(normalized)
                ? installRoot
                : Path.Combine(installRoot, normalized);
        }

        private static string NormalizePathSegment(string value)
        {
            var normalized = value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalid, '_');
            }

            return normalized.Trim().TrimEnd('.', ' ');
        }
    }
}

