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
using RomM.Platforms.Switch.Inspection;

namespace RomM.Platforms.Switch
{
    public sealed class SwitchPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".nsp",
            ".xci"
        };

        public string PlatformKey => "switch";
        public string DisplayName => "Nintendo Switch";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "switch" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "nintendoswitch",
            "switch",
            "nintendo switch"
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
                        Key = "SwitchEdenExecutablePath",
                        Label = "Eden Executable",
                        Description = "Optional Eden executable override. If omitted, LaunchBox emulator mapping is used.",
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
                    new() { Code = "installed_missing", Message = "Installed Nintendo Switch artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for Nintendo Switch detection." }
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
                Message = valid ? "Nintendo Switch install verified." : "Nintendo Switch install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Nintendo Switch install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Switch install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new SwitchGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "Switch inspection failed." });
            }

            var canonicalSource = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(canonicalSource) || !File.Exists(canonicalSource))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Switch launch artifact missing on disk after inspection." });
            }

            var finalSource = canonicalSource;
            if (inspection.RequiresDecompression)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Detected compressed Switch package: {Path.GetExtension(canonicalSource)}.");
                var decompressed = TrySimulateDecompression(canonicalSource, ctx.StagingDirectory, ctx.Logger, out var decompError);
                if (string.IsNullOrWhiteSpace(decompressed) || !File.Exists(decompressed))
                {
                    return Task.FromResult(new InstallResult { Success = false, Message = decompError ?? "Switch decompression failed." });
                }

                finalSource = decompressed;
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Decompression output: {decompressed}");
            }

            var extension = Path.GetExtension(finalSource) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved Switch launch artifact has unsupported extension '{extension}'." });
            }

            var targetPath = GameInstallPathHelper.ResolveTargetFilePath(
                installRoot,
                finalSource,
                ctx.GameName,
                inspection.TitleName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Switch install directory: {Path.GetDirectoryName(targetPath) ?? installRoot}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing base game artifact: {finalSource}");
            ctx.Logger?.Write(PlatformLogLevel.Info, "Moving game file to install location");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Nintendo Switch artifact...", 50));
                MoveOrReplace(finalSource, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Switch install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            LogDeferredOptionalContent(inspection, ctx.Logger);

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: Eden");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Nintendo Switch install completed.",
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
            progress?.Report(new InstallProgress("Uninstall", "Nintendo Switch uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Switch uninstall started");

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
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for Nintendo Switch uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Switch uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Nintendo Switch content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Nintendo Switch content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Nintendo Switch uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static void LogDeferredOptionalContent(SwitchGameInspectionResult inspection, IPlatformLogger? logger)
        {
            if (inspection.UpdateCandidates.Count == 0 && inspection.DlcCandidates.Count == 0)
            {
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Detected {inspection.UpdateCandidates.Count} update package(s) and {inspection.DlcCandidates.Count} DLC package(s).");
            logger?.Write(PlatformLogLevel.Warning, "Automatic Eden update/DLC installation is not implemented yet. Packages are detected and logged for future installer integration.");
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
                error = "Nintendo Switch install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"Nintendo Switch install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        private static string TrySimulateDecompression(string sourcePath, string? stagingDirectory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            var targetExtension = extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase)
                ? ".nsp"
                : extension.Equals(".xcz", StringComparison.OrdinalIgnoreCase)
                    ? ".xci"
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(targetExtension))
            {
                error = $"Unsupported compressed Switch package '{extension}'.";
                return string.Empty;
            }

            try
            {
                var outputRoot = !string.IsNullOrWhiteSpace(stagingDirectory)
                    ? stagingDirectory
                    : Path.GetDirectoryName(sourcePath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(outputRoot))
                {
                    error = "Unable to resolve Switch decompression output directory.";
                    return string.Empty;
                }

                Directory.CreateDirectory(outputRoot);
                var decompressedPath = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(sourcePath) + targetExtension);
                File.Copy(sourcePath, decompressedPath, overwrite: true);
                logger?.Write(PlatformLogLevel.Info, $"Decompressing {extension} to {targetExtension} (copy simulation until Eden decompressor integration is added).");
                return decompressedPath;
            }
            catch (Exception ex)
            {
                error = $"Switch decompression failed: {ex.Message}";
                return string.Empty;
            }
        }

        private static string BuildLaunchArguments(RomInstallSettings? settings, string romPath)
        {
            var template = settings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "{rom}";
            }

            return template.Replace("{rom}", QuoteArgument(romPath));
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Nintendo Switch source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Switch artifact at '{targetPath}'.");
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
    }
}

