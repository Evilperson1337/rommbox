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
using RomM.Platforms.Xbox360.Inspection;

namespace RomM.Platforms.Xbox360
{
    public sealed class Xbox360PlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".xex"
        };

        public string PlatformKey => "xbox360";
        public string DisplayName => "Microsoft Xbox 360";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "xbox360", "x360" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "xbox 360",
            "xbox360",
            "microsoft xbox 360",
            "x360"
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
                    new() { Code = "installed_missing", Message = "Installed Xbox 360 artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for Xbox 360 detection." }
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
                Message = valid ? "Xbox 360 install verified." : "Xbox 360 install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Xbox 360 install...", 0));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Xbox 360 install started.");

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Xbox 360 install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new Xbox360GameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "Xbox 360 inspection failed." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Xbox 360 launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved Xbox 360 launch artifact has unsupported extension '{extension}'." });
            }

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Xbox 360 install directory: {installRoot}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact/layout: {sourcePath}");

            string targetPath;
            string installRootPath;

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Xbox 360 artifact...", 50));
                if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    var targetFolderName = ResolveInstallFolderName(ctx.GameName, inspection.TitleName, Path.GetDirectoryName(sourcePath) ?? installRoot);
                    var targetRoot = Path.Combine(installRoot, targetFolderName);
                    var targetFileName = ResolveTargetFileName(sourcePath, ctx.GameName, inspection.TitleName);
                    targetPath = Path.Combine(targetRoot, targetFileName);
                    MoveOrReplace(sourcePath, targetPath, ctx.Logger);
                    installRootPath = targetRoot;
                }
                else
                {
                    var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                    {
                        return Task.FromResult(new InstallResult { Success = false, Message = "Xbox 360 XEX source directory missing on disk." });
                    }

                    var targetFolderName = ResolveInstallFolderName(ctx.GameName, inspection.TitleName, sourceDirectory);
                    var targetRoot = Path.Combine(installRoot, targetFolderName);
                    EnsureDirectoryPlaced(sourceDirectory, targetRoot, ctx.Logger);
                    targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
                    installRootPath = targetRoot;
                }
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: xenia");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Xbox 360 install completed.",
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = installRootPath
            });
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "Xbox 360 uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Xbox 360 uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var installRootPath = ctx.InstallRootPath ?? string.Empty;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            var deletedInstalledPath = false;

            if (!string.IsNullOrWhiteSpace(installRootPath) && Directory.Exists(installRootPath))
            {
                try
                {
                    var canDeleteRoot = string.IsNullOrWhiteSpace(installedPath)
                        || IsChildPath(installedPath, installRootPath)
                        || string.Equals(installedPath, installRootPath, StringComparison.OrdinalIgnoreCase);
                    if (canDeleteRoot)
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting installed content root: {installRootPath}");
                        Directory.Delete(installRootPath, recursive: true);
                        removed++;
                        if (!string.IsNullOrWhiteSpace(installedPath))
                        {
                            deletedInstalledPath = true;
                        }
                    }
                    else
                    {
                        notes.Add($"Install root '{installRootPath}' does not safely match installed path '{installedPath}'; skipping root delete.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 uninstall skipped install root '{installRootPath}' due to ownership mismatch.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Xbox 360 install root '{installRootPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Xbox 360 install root '{installRootPath}': {ex.Message}");
                }
            }

            if (!deletedInstalledPath && !string.IsNullOrWhiteSpace(installedPath))
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
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for Xbox 360 uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Xbox 360 content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Xbox 360 content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Xbox 360 uninstall completed.",
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
                error = "Xbox 360 install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 install failed write-check for '{directory}': {ex.Message}");
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
                throw new FileNotFoundException("Xbox 360 source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Xbox 360 artifact at '{targetPath}'.");
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

        private static string ResolveTargetFileName(string sourcePath, string? gameName, string? detectedTitleName)
        {
            var sourceName = Path.GetFileName(sourcePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                return sourceName;
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            var baseName = !string.IsNullOrWhiteSpace(gameName) ? gameName : detectedTitleName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Xbox360_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), extension);
        }

        private static string ResolveInstallFolderName(string? gameName, string? titleName, string sourceDirectory)
        {
            var preferred = !string.IsNullOrWhiteSpace(gameName)
                ? gameName
                : titleName;
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = Path.GetFileName(sourceDirectory);
            }

            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = "Xbox360_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                preferred = preferred.Replace(invalid, '_');
            }

            return preferred.Trim();
        }

        private static void EnsureDirectoryPlaced(string sourceDirectory, string targetDirectory, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("Source and target directories are required.");
            }

            var normalizedSource = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(targetDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, "Source and destination directories are identical; no copy needed.");
                return;
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            DirectoryCopy(sourceDirectory, targetDirectory);
        }

        private static void DirectoryCopy(string sourceDir, string destinationDir)
        {
            var source = new DirectoryInfo(sourceDir);
            if (!source.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: '{sourceDir}'.");
            }

            Directory.CreateDirectory(destinationDir);
            foreach (var file in source.GetFiles())
            {
                var target = Path.Combine(destinationDir, file.Name);
                file.CopyTo(target, overwrite: true);
            }

            foreach (var dir in source.GetDirectories())
            {
                DirectoryCopy(dir.FullName, Path.Combine(destinationDir, dir.Name));
            }
        }

        private static bool IsChildPath(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var candidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}

