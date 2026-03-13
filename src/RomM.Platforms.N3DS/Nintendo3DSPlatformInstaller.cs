using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.N3DS.Inspection;
using RomM.Platforms.RomBase;

namespace RomM.Platforms.N3DS
{
    public sealed class Nintendo3DSPlatformInstaller : RomPlatformInstallerBase, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedInstalledExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".3ds",
            ".cci",
            ".cxi"
        };

        public override string PlatformKey => "3ds";
        public override string DisplayName => "Nintendo 3DS";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "3ds" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "nintendo3ds",
            "nintendo 3ds",
            "3ds"
        };

        public override PlatformInstallerCapabilities Capabilities => new PlatformInstallerCapabilities
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

        public override PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "AzaharExecutablePath",
                        Label = "Azahar Executable",
                        Description = "Optional Azahar executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new PlatformConfigFieldDescriptor
                    {
                        Key = "AzaharPlusExecutablePath",
                        Label = "AzaharPlus Executable",
                        Description = "Optional AzaharPlus executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = true
                    }
                }
            };
        }

        protected override bool IsInstalledArtifactSupported(string installedPath)
        {
            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            return SupportedInstalledExtensions.Contains(extension);
        }

        public override Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing Nintendo 3DS install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo 3DS install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new Nintendo3DSGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "Nintendo 3DS inspection failed." });
            }

            var sourcePath = inspection.LaunchArtifactPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Nintendo 3DS launch artifact missing on disk after inspection." });
            }

            var extension = Path.GetExtension(sourcePath) ?? string.Empty;
            if (!SupportedInstalledExtensions.Contains(extension))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = $"Resolved Nintendo 3DS launch artifact has unsupported extension '{extension}'." });
            }

            var fileName = Path.GetFileName(sourcePath);
            var targetDirectory = ResolveInstallDirectory(installRoot, ctx.GameName, inspection.TitleName);
            var targetPath = Path.Combine(targetDirectory, fileName);

            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Nintendo 3DS install directory: {targetDirectory}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing artifact: {fileName}");

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Nintendo 3DS artifact...", 50));
                MoveOrReplace(sourcePath, targetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Nintendo 3DS install failed: {ex.Message}");
                return Task.FromResult(new InstallResult { Success = false, Message = ex.Message });
            }

            progress?.Report(new InstallProgress("Installing", "Install completed.", 100));
            var launchArgs = BuildLaunchArguments(ctx.RomSettings, targetPath);
            var emulatorName = ResolveEmulatorName(ctx.RomSettings);
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Configured emulator: {emulatorName}");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Nintendo 3DS install completed.",
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = installRoot
            });
        }

        protected override RomInstallProfile BuildProfile()
        {
            return new RomInstallProfile
            {
                PlatformKey = PlatformKey,
                DisplayName = DisplayName,
                PlatformFolderName = "Nintendo 3DS",
                UsePlatformSubdirectory = false,
                UseGameSubdirectory = true,
                ArchivePolicy = RomArchivePolicy.AllowExtraction,
                RomExtensions = new List<string> { ".3ds", ".cci", ".cxi", ".cia" },
                Emulator = new RomEmulatorMetadata
                {
                    EmulatorName = "Azahar",
                    CoreName = string.Empty,
                    LaunchArguments = "{rom}"
                }
            };
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
                error = "Nintendo 3DS install directory missing.";
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
                logger?.Write(PlatformLogLevel.Warning, $"Nintendo 3DS install failed write-check for '{directory}': {ex.Message}");
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

        private static string ResolveEmulatorName(RomInstallSettings? settings)
        {
            var configured = settings?.EmulatorName ?? string.Empty;
            if (configured.IndexOf("azaharplus", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "AzaharPlus";
            }

            if (configured.IndexOf("azahar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Azahar";
            }

            return "Azahar";
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Nintendo 3DS source file not found.", sourcePath);
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
                logger?.Write(PlatformLogLevel.Info, $"Removing existing Nintendo 3DS artifact at '{targetPath}'.");
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

