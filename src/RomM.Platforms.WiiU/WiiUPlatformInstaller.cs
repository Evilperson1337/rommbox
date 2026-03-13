using System;
using System.Collections.Generic;
using System.IO;
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
using RomM.Platforms.WiiU.Inspection;

namespace RomM.Platforms.WiiU
{
    public sealed class WiiUPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".wud",
            ".wux",
            ".wua",
            ".rpx"
        };

        public string PlatformKey => "wiiu";
        public string DisplayName => "Nintendo Wii U";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "wiiu", "wii-u" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "wiiu",
            "wii u",
            "nintendo wii u",
            "nintendowiiu"
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
                        Key = "CemuExecutablePath",
                        Label = "Cemu Executable",
                        Description = "Optional Cemu executable override. If omitted, LaunchBox emulator mapping is used.",
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

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            var isDirectLaunch = !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension) && File.Exists(installedPath);
            if (isDirectLaunch)
            {
                result.RecommendedExecutablePath = installedPath;
                result.CandidateExecutablePaths = new List<string> { installedPath };
                return Task.FromResult(result);
            }

            if (extension.Equals(".rpx", StringComparison.OrdinalIgnoreCase) && File.Exists(installedPath))
            {
                result.RecommendedExecutablePath = installedPath;
                result.CandidateExecutablePaths = new List<string> { installedPath };
                return Task.FromResult(result);
            }

            result.IsInstalled = false;
            result.Warnings = new List<DetectionWarning>
            {
                new() { Code = "installed_missing", Message = "Installed Nintendo Wii U artifact not found on disk." }
            };
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
                Message = valid ? "Nintendo Wii U install verified." : "Nintendo Wii U install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Nintendo Wii U install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Wii U install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new WiiUGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "Nintendo Wii U inspection failed." });
            }

            var targetDirectory = ResolveInstallDirectory(installRoot, ctx.GameName, inspection.TitleName);
            var targetPath = string.Empty;

            var installSourcePath = !string.IsNullOrWhiteSpace(inspection.InstallSourcePath)
                ? inspection.InstallSourcePath
                : inspection.LaunchArtifactPath;

            if (inspection.SelectedFormat == WiiUContentFormat.ExtractedLayout)
            {
                if (string.IsNullOrWhiteSpace(installSourcePath) || !Directory.Exists(installSourcePath))
                {
                    return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Wii U extracted layout missing on disk after inspection." });
                }

                var sourceFolderName = Path.GetFileName(installSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var folderName = !string.IsNullOrWhiteSpace(sourceFolderName)
                    ? sourceFolderName
                    : NormalizePathSegment(ctx.GameName ?? inspection.TitleName ?? "WiiU_Game");
                var targetRoot = Path.Combine(targetDirectory, folderName);

                ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Wii U install directory: {targetRoot}");
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact/layout: {installSourcePath}");

                try
                {
                    progress?.Report(new InstallProgress("Installing", "Installing Nintendo Wii U layout...", 50));
                    ReplaceDirectory(installSourcePath, targetRoot, ctx.Logger);
                }
                catch (Exception ex)
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Wii U install failed: {ex.Message}");
                    return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
                }

                var relativeLaunch = Path.GetRelativePath(installSourcePath, inspection.LaunchArtifactPath);
                targetPath = Path.Combine(targetRoot, relativeLaunch);
            }
            else
            {
                var sourcePath = inspection.LaunchArtifactPath;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo Wii U launch artifact missing on disk after inspection." });
                }

                targetPath = GameInstallPathHelper.ResolveTargetFilePath(
                    installRoot,
                    sourcePath,
                    ctx.GameName,
                    inspection.TitleName);

                ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Wii U install directory: {Path.GetDirectoryName(targetPath) ?? targetDirectory}");
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact/layout: {sourcePath}");

                try
                {
                    progress?.Report(new InstallProgress("Installing", "Installing Nintendo Wii U artifact...", 50));
                    MoveOrReplace(sourcePath, targetPath, ctx.Logger);
                }
                catch (Exception ex)
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Wii U install failed: {ex.Message}");
                    return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
                }
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: Cemu");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Nintendo Wii U install completed.",
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
            progress?.Report(new InstallProgress("Uninstall", "Nintendo Wii U uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Nintendo Wii U uninstall started");

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
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for Nintendo Wii U uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo Wii U uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Nintendo Wii U content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Nintendo Wii U content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Nintendo Wii U uninstall completed.",
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

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "Nintendo Wii U install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"Nintendo Wii U install failed write-check for '{directory}': {ex.Message}");
                return false;
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
                throw new FileNotFoundException("Nintendo Wii U source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Nintendo Wii U artifact at '{targetPath}'.");
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

        private static void ReplaceDirectory(string sourceDirectory, string targetDirectory, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("Source and target directories are required.");
            }

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Nintendo Wii U source directory not found: {sourceDirectory}");
            }

            var sourceFull = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetFull = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, $"Source and destination are identical; no move needed: '{targetDirectory}'.");
                return;
            }

            if (Directory.Exists(targetDirectory))
            {
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Nintendo Wii U layout at '{targetDirectory}'.");
                Directory.Delete(targetDirectory, true);
            }

            var sourceRoot = Path.GetPathRoot(sourceFull) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetFull) ?? string.Empty;
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory) ?? targetDirectory);
            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(sourceDirectory, targetDirectory);
                return;
            }

            CopyDirectoryRecursive(sourceDirectory, targetDirectory);
            Directory.Delete(sourceDirectory, true);
        }

        private static void CopyDirectoryRecursive(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDirectory, file);
                var destination = Path.Combine(targetDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetDirectory);
                File.Copy(file, destination, overwrite: true);
            }
        }
    }
}

