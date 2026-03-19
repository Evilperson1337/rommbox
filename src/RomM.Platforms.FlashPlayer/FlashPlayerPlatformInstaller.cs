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

namespace RomM.Platforms.FlashPlayer
{
    public sealed class FlashPlayerPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".swf"
        };

        public string PlatformKey => "flashplayer";
        public string DisplayName => "Flash Player";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "4", "flashplayer", "flash", "swf" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "flash",
            "flash player",
            "flashplayer",
            "adobe flash",
            "adobe flash player",
            "browser (flash/html5)",
            "browser flash html5",
            "flash html5",
            "macromedia flash",
            "swf"
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
            SupportsDlc = false,
            SupportsUpdates = false,
            SupportsRaps = false,
            RequiresEmulatorPath = true,
            SupportsRomMWebPlay = true,
            RomMWebPlayPathSuffix = "ruffle"
        };

        public PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "RuffleExecutablePath",
                        Label = "Ruffle Executable",
                        Description = "Required Ruffle executable path used to launch installed .swf files.",
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
                    new() { Code = "installed_missing", Message = "Installed Flash Player artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for Flash Player detection." }
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
                Message = valid ? "Flash Player install verified." : "Flash Player install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Flash Player install...", 0));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Flash Player install started");

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Flash Player install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var sourcePath = ResolveSourcePath(ctx, ctx.Logger);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Flash Player launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Flash Player detection rejected unsupported extension '{extension}'.");
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved Flash Player launch artifact has unsupported extension '{extension}'." });
            }

            var ruffleExecutable = ResolveRuffleExecutable(ctx.Settings, ctx.RomSettings, ctx.Logger);
            if (string.IsNullOrWhiteSpace(ruffleExecutable))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Ruffle executable could not be resolved from platform settings or LaunchBox emulator mapping." });
            }

            if (!File.Exists(ruffleExecutable))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Resolved Ruffle executable does not exist: {ruffleExecutable}");
                return Task.FromResult(new InstallResult { Success = false, Message = "Configured Ruffle executable was not found on disk." });
            }

            var targetFileName = ResolveTargetFileName(sourcePath, ctx.GameName);
            var targetDirectory = ResolveInstallDirectory(installRoot, ctx.GameName);
            var targetPath = Path.Combine(targetDirectory, targetFileName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Matched platform plugin: {DisplayName} ({PlatformKey})");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Flash Player install directory: {targetDirectory}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact: {sourcePath}");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Flash artifact...", 50));
                MoveOrReplace(sourcePath, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Flash Player install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: Ruffle");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Ruffle executable: {ruffleExecutable}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Generated launch command: {Path.GetFileName(ruffleExecutable)} {launchArgs}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Flash Player install completed.",
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
            progress?.Report(new InstallProgress("Uninstall", "Flash Player uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Flash Player uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(installedPath))
            {
                try
                {
                    if (File.Exists(installedPath))
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting installed Flash content: {installedPath}");
                        File.Delete(installedPath);
                        removed++;
                    }
                    else if (Directory.Exists(installedPath))
                    {
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for Flash Player uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Flash Player uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed Flash content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed Flash content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Flash Player uninstall completed.",
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

        private static string ResolveSourcePath(InstallContext ctx, IPlatformLogger? logger)
        {
            var extractedPath = ctx.ExtractedPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(extractedPath))
            {
                if (File.Exists(extractedPath))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Flash Player staging resolved direct extracted artifact: {extractedPath}");
                    return extractedPath;
                }

                if (Directory.Exists(extractedPath))
                {
                    foreach (var file in Directory.EnumerateFiles(extractedPath, "*", SearchOption.AllDirectories))
                    {
                        var extension = Path.GetExtension(file) ?? string.Empty;
                        if (SupportedExtensions.Contains(extension))
                        {
                            logger?.Write(PlatformLogLevel.Info, $"Flash Player staging selected extracted artifact: {file}");
                            return file;
                        }
                    }
                }
            }

            var archivePath = ctx.ArchivePath ?? string.Empty;
            if (File.Exists(archivePath) && SupportedExtensions.Contains(Path.GetExtension(archivePath) ?? string.Empty))
            {
                logger?.Write(PlatformLogLevel.Info, $"Flash Player staging selected downloaded artifact: {archivePath}");
                return archivePath;
            }

            logger?.Write(PlatformLogLevel.Warning, "Flash Player staging did not find a supported .swf artifact.");
            return string.Empty;
        }

        private static string ResolveRuffleExecutable(PlatformInstallSettings? settings, RomInstallSettings? romSettings, IPlatformLogger? logger)
        {
            var configured = settings?.RuffleExecutablePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                logger?.Write(PlatformLogLevel.Info, $"Resolved Ruffle executable from platform settings: {configured}");
                return configured;
            }

            var fallback = romSettings?.EmulatorExecutablePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                logger?.Write(PlatformLogLevel.Info, $"Resolved Ruffle executable from LaunchBox emulator mapping: {fallback}");
                return fallback;
            }

            logger?.Write(PlatformLogLevel.Warning, "Ruffle executable override not set and LaunchBox emulator mapping did not provide an executable.");
            return string.Empty;
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

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".romm_flash_write_test.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Install directory not writable: {ex.Message}";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                logger?.Write(PlatformLogLevel.Warning, $"Flash Player install failed write-check for '{directory}': {ex.Message}");
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
                throw new FileNotFoundException("Flash Player source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Flash artifact at '{targetPath}'.");
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

        private static string ResolveTargetFileName(string sourcePath, string? gameName)
        {
            var sourceName = Path.GetFileName(sourcePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                return sourceName;
            }

            var baseName = string.IsNullOrWhiteSpace(gameName) ? "Flash_Game" : gameName;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), ".swf");
        }

        private static string ResolveInstallDirectory(string installRoot, string? gameName)
        {
            var safeName = string.IsNullOrWhiteSpace(gameName) ? "Flash Game" : gameName;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalid, '_');
            }

            return Path.Combine(installRoot, safeName.Trim());
        }
    }
}
