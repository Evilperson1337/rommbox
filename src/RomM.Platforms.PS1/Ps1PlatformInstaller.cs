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
using RomM.Platforms.PS1.Inspection;

namespace RomM.Platforms.PS1
{
    public sealed class Ps1PlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedLaunchExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".chd",
            ".iso",
            ".pbp",
            ".cue",
            ".ccd",
            ".m3u"
        };

        private static readonly HashSet<string> SingleFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".chd",
            ".iso",
            ".pbp"
        };

        public string PlatformKey => "ps1";
        public string DisplayName => "PlayStation";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "22" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[] { "playstation", "sonyplaystation", "psx", "ps1" };

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
                    new() { Code = "installed_missing", Message = "Installed PS1 artifact not found on disk." }
                };
                return Task.FromResult(result);
            }

            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            if (!SupportedLaunchExtensions.Contains(extension))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = $"Installed file extension '{extension}' is not supported for PS1 detection." }
                };
                return Task.FromResult(result);
            }

            if (extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
            {
                var m3uWarnings = ValidateM3u(installedPath);
                if (m3uWarnings.Count > 0)
                {
                    result.IsInstalled = false;
                    result.Warnings = m3uWarnings
                        .Select(message => new DetectionWarning { Code = "m3u_missing_disc", Message = message })
                        .ToList();
                    return Task.FromResult(result);
                }
            }

            if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                var cueRefs = Ps1GameInspector.ParseCueReferencedFiles(installedPath);
                if (cueRefs.Count == 0 || cueRefs.Any(path => !File.Exists(path)))
                {
                    result.IsInstalled = false;
                    result.Warnings = new List<DetectionWarning>
                    {
                        new() { Code = "cue_missing_companion", Message = "CUE companion disc file(s) missing." }
                    };
                    return Task.FromResult(result);
                }
            }

            if (extension.Equals(".ccd", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.Combine(Path.GetDirectoryName(installedPath) ?? string.Empty, Path.GetFileNameWithoutExtension(installedPath));
                if (!File.Exists(baseName + ".img"))
                {
                    result.IsInstalled = false;
                    result.Warnings = new List<DetectionWarning>
                    {
                        new() { Code = "ccd_missing_companion", Message = "CCD companion IMG file missing." }
                    };
                    return Task.FromResult(result);
                }
            }

            result.RecommendedExecutablePath = installedPath;
            result.CandidateExecutablePaths = new List<string> { installedPath };
            return Task.FromResult(result);
        }

        public async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return new InstallResult { Success = false, Message = "Install context missing." };
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return new InstallResult { Success = false, Message = "PS1 install directory missing." };
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return new InstallResult { Success = false, Message = writableError };
            }

            var sourcePath = ResolveSourcePath(ctx.ArchivePath, ctx.ExtractedPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return new InstallResult { Success = false, Message = "PS1 source content missing." };
            }

            if (File.Exists(sourcePath))
            {
                var extension = Path.GetExtension(sourcePath) ?? string.Empty;
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, "PS1 archive provided without extracted content; extraction is required before install.");
                    return new InstallResult { Success = false, Message = "PS1 archives must be extracted before install." };
                }
            }

            var inspector = new Ps1GameInspector();
            var inspection = inspector.Inspect(sourcePath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (inspection.Format == Ps1ContentFormat.Unknown || string.IsNullOrWhiteSpace(inspection.LaunchFilePath))
            {
                return new InstallResult { Success = false, Message = "Unable to determine PS1 launch artifact." };
            }

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved canonical launch artifact: '{inspection.LaunchFilePath}'.");
            var launchTarget = string.Empty;
            var installRootPath = installRoot;
            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing PS1 content...", 50));

                if (inspection.Format == Ps1ContentFormat.Chd
                    || inspection.Format == Ps1ContentFormat.Iso
                    || inspection.Format == Ps1ContentFormat.Pbp)
                {
                    var gameFolder = GameInstallPathHelper.ResolveGameDirectory(installRoot, ctx.GameName, inspection.InstallFolderName);
                    var fileName = Path.GetFileName(inspection.LaunchFilePath);
                    launchTarget = Path.Combine(gameFolder, fileName);
                    MoveOrReplace(inspection.LaunchFilePath, launchTarget, ctx.Logger);
                    installRootPath = gameFolder;
                }
                else
                {
                    var gameFolder = Path.Combine(installRoot, inspection.InstallFolderName);
                    Directory.CreateDirectory(gameFolder);
                    PlaceOwnedFiles(inspection.SourceRootPath, inspection.OwnedFiles, gameFolder, ctx.Logger);

                    if (inspection.IsMultiDisc)
                    {
                        var playlistPath = Path.Combine(gameFolder, inspection.InstallFolderName + ".m3u");
                        var lines = inspection.DiscFiles
                            .Select(Path.GetFileName)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Select(name => name ?? string.Empty)
                            .ToArray();
                        File.WriteAllLines(playlistPath, lines);
                        launchTarget = playlistPath;
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Generated PS1 playlist for multi-disc content: '{playlistPath}'.");
                    }
                    else
                    {
                        launchTarget = Path.Combine(gameFolder, Path.GetFileName(inspection.LaunchFilePath));
                    }

                    installRootPath = gameFolder;
                }
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"PS1 install failed: {ex.Message}");
                return new InstallResult { Success = false, Message = ex.Message };
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, launchTarget);
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path resolved: '{launchTarget}'.");
            ctx.Logger?.Write(PlatformLogLevel.Info, "Install completed.");

            return new InstallResult
            {
                Success = true,
                Message = "PS1 install completed.",
                ExecutablePath = launchTarget,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = installRootPath
            };
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "PS1 uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PS1 uninstall started.");

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            var installRoot = ctx.InstallRootPath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(installedPath) && File.Exists(installedPath))
            {
                try
                {
                    ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting launch artifact: '{installedPath}'.");
                    File.Delete(installedPath);
                    removed++;
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete launch artifact '{installedPath}': {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
            {
                var installedExtension = Path.GetExtension(installedPath ?? string.Empty) ?? string.Empty;
                var isSingleFileInstall = SingleFileExtensions.Contains(installedExtension)
                    && string.Equals(Path.GetDirectoryName(installedPath) ?? string.Empty, installRoot, StringComparison.OrdinalIgnoreCase);

                if (!isSingleFileInstall)
                {
                    try
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting game-owned install directory: '{installRoot}'.");
                        Directory.Delete(installRoot, recursive: true);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"Failed to delete install root '{installRoot}': {ex.Message}");
                    }
                }
                else
                {
                    ctx.Logger?.Write(PlatformLogLevel.Info, "Single-file PS1 install detected; preserving platform directory.");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed.");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PS1 uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath) && File.Exists(ctx.InstalledPath);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PS1 install verified." : "PS1 install missing on disk."
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

        private static string ResolveSourcePath(string? archivePath, string? extractedPath)
        {
            if (!string.IsNullOrWhiteSpace(extractedPath) && (Directory.Exists(extractedPath) || File.Exists(extractedPath)))
            {
                return extractedPath;
            }

            if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
            {
                return archivePath;
            }

            return string.Empty;
        }

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "PS1 install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"PS1 install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        private static string BuildLaunchArguments(RomInstallSettings? settings, string romPath)
        {
            var arguments = settings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = "{rom}";
            }

            return arguments.Replace("{rom}", QuoteArgument(romPath));
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static void PlaceOwnedFiles(string sourceRootPath, IReadOnlyList<string> ownedFiles, string destinationRoot, IPlatformLogger? logger)
        {
            if (ownedFiles == null || ownedFiles.Count == 0)
            {
                throw new InvalidOperationException("No PS1 files available for install.");
            }

            var normalizedSourceRoot = (Directory.Exists(sourceRootPath) ? Path.GetFullPath(sourceRootPath) : string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var file in ownedFiles.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
            {
                var target = ResolveOwnedFileTarget(file, normalizedSourceRoot, destinationRoot);
                MoveOrReplace(file, target, logger);
            }
        }

        private static string ResolveOwnedFileTarget(string sourcePath, string normalizedSourceRoot, string destinationRoot)
        {
            if (!string.IsNullOrWhiteSpace(normalizedSourceRoot))
            {
                var fullSource = Path.GetFullPath(sourcePath);
                if (fullSource.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = fullSource.Substring(normalizedSourceRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return Path.Combine(destinationRoot, relative);
                }
            }

            return Path.Combine(destinationRoot, Path.GetFileName(sourcePath));
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PS1 source file not found.", sourcePath);
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDirectory);
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(targetPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Removing existing file at '{targetPath}'.");
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

        private static List<string> ValidateM3u(string m3uPath)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(m3uPath) || !File.Exists(m3uPath))
            {
                warnings.Add("Playlist file missing.");
                return warnings;
            }

            var folder = Path.GetDirectoryName(m3uPath) ?? string.Empty;
            foreach (var line in File.ReadAllLines(m3uPath))
            {
                var value = line?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var resolved = Path.IsPathRooted(value) ? value : Path.Combine(folder, value);
                if (!File.Exists(resolved))
                {
                    warnings.Add($"Playlist entry missing: '{resolved}'.");
                }
            }

            return warnings;
        }
    }
}
