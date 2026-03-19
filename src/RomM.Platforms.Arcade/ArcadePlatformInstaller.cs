using System;
using System.Collections.Generic;
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
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.Arcade.Inspection;

namespace RomM.Platforms.Arcade
{
    public sealed class ArcadePlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip"
        };

        public string PlatformKey => "arcade";
        public string DisplayName => "Arcade";
        public IReadOnlyCollection<string>? SupportedPlatformIds => Array.Empty<string>();
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "arcade",
            "arcadegames",
            "mame",
            "finalburnneo",
            "final burn neo",
            "fbneo",
            "fba"
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
            return null;
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
                    new() { Code = "installed_missing", Message = "Installed Arcade ROM set archive not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for Arcade detection." }
                };
                return Task.FromResult(result);
            }

            result.RecommendedExecutablePath = installedPath;
            result.CandidateExecutablePaths = new List<string> { installedPath };
            return Task.FromResult(result);
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Arcade install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Arcade install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new ArcadeGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid || string.IsNullOrWhiteSpace(inspection.LaunchArtifactPath) || !File.Exists(inspection.LaunchArtifactPath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "Unable to determine Arcade ROM set archive." });
            }

            if (!string.Equals(Path.GetExtension(inspection.LaunchArtifactPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Arcade source content must be a .zip ROM set archive." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            var targetPath = ResolveTargetPath(installRoot, sourcePath, ctx.GameName);
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Detected arcade ROM set: {Path.GetFileName(sourcePath)}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Arcade install directory: {installRoot}");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Moving ROM archive to install location...", 50));
                ctx.Logger?.Write(PlatformLogLevel.Info, "Arcade install started");
                ctx.Logger?.Write(PlatformLogLevel.Info, "Moving ROM archive to install location");
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Source: {sourcePath}");
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Destination: {targetPath}");
                MoveOrReplace(sourcePath, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Arcade install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Configured emulator: {ResolveEmulatorMode(ctx.RomSettings)}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Launch artifact: {Path.GetFileName(targetPath)}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");
            ctx.Logger?.Write(PlatformLogLevel.Info, "Install completed.");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Arcade install completed.",
                ExecutablePath = targetPath,
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
            progress?.Report(new InstallProgress("Uninstall", "Arcade uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Arcade uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(installedPath))
            {
                try
                {
                    if (File.Exists(installedPath))
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting ROM archive: {Path.GetFileName(installedPath)}");
                        File.Delete(installedPath);
                        removed++;
                    }
                    else if (Directory.Exists(installedPath))
                    {
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for Arcade uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Arcade uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Arcade ROM '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Arcade ROM '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Arcade uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath)
                && File.Exists(ctx.InstalledPath)
                && string.Equals(Path.GetExtension(ctx.InstalledPath), ".zip", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "Arcade install verified." : "Arcade install missing on disk."
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

        private static string ResolveTargetPath(string installRoot, string sourcePath, string? gameName)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return Path.GetFullPath(sourcePath);
            }

            var fileName = Path.GetFileName(sourcePath);
            var sourceDirectory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory)
                && IsPathUnderRoot(sourceDirectory, installRoot)
                && !IsSamePath(sourceDirectory, installRoot))
            {
                return Path.Combine(sourceDirectory, fileName);
            }

            var folderName = NormalizePathSegment(gameName);
            return Path.Combine(installRoot, folderName, fileName);
        }

        private static string NormalizePathSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned.Trim();
        }

        private static bool IsSamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPathUnderRoot(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "Arcade install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"Arcade install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Arcade source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing ROM archive at '{targetPath}'.");
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

        private static string BuildLaunchArguments(RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings? settings, string romArchivePath)
        {
            var mode = ResolveEmulatorMode(settings);
            var defaultTemplate = mode == ArcadeEmulatorMode.Mame
                ? "{romset}"
                : "-L finalburnneo_libretro.dll {rom}";

            var template = string.IsNullOrWhiteSpace(settings?.LaunchArguments)
                ? defaultTemplate
                : settings!.LaunchArguments!;

            var romSet = Path.GetFileNameWithoutExtension(romArchivePath) ?? string.Empty;
            var resolved = template
                .Replace("{rom}", QuoteArgument(romArchivePath))
                .Replace("{romset}", QuoteArgument(romSet));

            if (mode == ArcadeEmulatorMode.RetroArch
                && !resolved.Contains("-L", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings?.CorePath ?? settings?.CoreName))
            {
                var core = settings?.CorePath ?? settings?.CoreName ?? "finalburnneo_libretro.dll";
                resolved = $"-L {QuoteArgument(core)} {resolved}";
            }

            return resolved;
        }

        private static ArcadeEmulatorMode ResolveEmulatorMode(RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings? settings)
        {
            var emulatorName = settings?.EmulatorName ?? string.Empty;
            if (emulatorName.IndexOf("mame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ArcadeEmulatorMode.Mame;
            }

            var coreText = string.Join(" ", new[] { settings?.CoreName, settings?.CorePath }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (coreText.IndexOf("fbneo", StringComparison.OrdinalIgnoreCase) >= 0
                || coreText.IndexOf("finalburn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ArcadeEmulatorMode.RetroArch;
            }

            if (emulatorName.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ArcadeEmulatorMode.RetroArch;
            }

            return ArcadeEmulatorMode.Mame;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private enum ArcadeEmulatorMode
        {
            Mame,
            RetroArch
        }
    }
}
