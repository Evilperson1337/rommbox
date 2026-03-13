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
using RomM.Platforms.PS2.Inspection;

namespace RomM.Platforms.PS2
{
    public sealed class Ps2PlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso",
            ".chd",
            ".cso",
            ".zso",
            ".bin"
        };

        public string PlatformKey => "ps2";
        public string DisplayName => "PlayStation 2";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "ps2", "17" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "ps2",
            "playstation2",
            "playstation 2",
            "sonyplaystation2",
            "sony playstation 2"
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
                        Key = "Pcsx2ExecutablePath",
                        Label = "PCSX2 Executable",
                        Description = "Optional PCSX2 executable override. If omitted, LaunchBox emulator mapping is used.",
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
                    new() { Code = "installed_missing", Message = "Installed PS2 artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for PS2 detection." }
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
                Message = valid ? "PlayStation 2 install verified." : "PlayStation 2 install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation 2 install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "PlayStation 2 install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new Ps2GameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "PlayStation 2 inspection failed." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "PlayStation 2 launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved PS2 launch artifact has unsupported extension '{extension}'." });
            }

            var targetFileName = ResolveTargetFileName(sourcePath, ctx.GameName, inspection.TitleName);
            var targetDirectory = GameInstallPathHelper.ResolveGameDirectory(installRoot, ctx.GameName, inspection.TitleName);
            var targetPath = Path.Combine(targetDirectory, targetFileName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved PS2 install directory: {targetDirectory}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact: {sourcePath}");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing PlayStation 2 artifact...", 50));
                MoveOrReplace(sourcePath, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"PlayStation 2 install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: PCSX2");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "PlayStation 2 install completed.",
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
            progress?.Report(new InstallProgress("Uninstall", "PlayStation 2 uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PlayStation 2 uninstall started");

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
                        notes.Add($"Installed path '{installedPath}' is a directory; refusing to delete directory for PlayStation 2 uninstall.");
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"PlayStation 2 uninstall skipped directory path '{installedPath}' to preserve platform folder.");
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete installed PlayStation 2 content '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete installed PlayStation 2 content '{installedPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PlayStation 2 uninstall completed.",
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
                error = "PlayStation 2 install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"PlayStation 2 install failed write-check for '{directory}': {ex.Message}");
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
                throw new FileNotFoundException("PlayStation 2 source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing PlayStation 2 artifact at '{targetPath}'.");
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
            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            var sourceName = Path.GetFileName(sourcePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                return sourceName;
            }

            var baseName = !string.IsNullOrWhiteSpace(gameName) ? gameName : detectedTitleName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "PS2_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), extension);
        }
    }
}

