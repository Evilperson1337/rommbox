using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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
        private static readonly Regex TitleIdRegex = new(@"(?:^|[^0-9A-Fa-f])([0-9A-Fa-f]{8})(?=[^0-9A-Fa-f]|$)", RegexOptions.Compiled);

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
            SupportsDlc = true,
            SupportsUpdates = true,
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
            var additionalApplications = new List<AdditionalApplicationLaunchInfo>();

            try
            {
                progress?.Report(new InstallProgress("Installing", "Installing Xbox 360 artifact...", 50));
                if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    var targetFolderName = ResolveInstallFolderName(ctx.GameName, inspection.TitleName, Path.GetDirectoryName(sourcePath) ?? installRoot);
                    var targetRoot = Path.Combine(installRoot, targetFolderName);
                    var discCandidates = BuildDiscCandidates(inspection);
                    var primaryDisc = discCandidates.FirstOrDefault(candidate => string.Equals(candidate.Path, sourcePath, StringComparison.OrdinalIgnoreCase))
                        ?? discCandidates.FirstOrDefault()
                        ?? new Xbox360ContentCandidate { Path = sourcePath, FileName = Path.GetFileName(sourcePath) ?? string.Empty };
                    var targetFileName = ResolveTargetFileName(primaryDisc, ctx.GameName, inspection.TitleName);
                    targetPath = Path.Combine(targetRoot, targetFileName);
                    MoveOrReplace(primaryDisc.Path, targetPath, ctx.Logger);
                    foreach (var disc in discCandidates.Where(candidate => !string.Equals(candidate.Path, primaryDisc.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        var additionalTargetFileName = ResolveTargetFileName(disc, ctx.GameName, inspection.TitleName);
                        var additionalTargetPath = Path.Combine(targetRoot, additionalTargetFileName);
                        MoveOrReplace(disc.Path, additionalTargetPath, ctx.Logger);
                        additionalApplications.Add(new AdditionalApplicationLaunchInfo
                        {
                            Id = $"disc-{disc.DiscNumber ?? additionalApplications.Count + 2}",
                            Name = $"Play {Path.GetFileNameWithoutExtension(additionalTargetFileName)}",
                            ApplicationPath = additionalTargetPath,
                            Arguments = string.IsNullOrWhiteSpace(BuildLaunchArguments(ctx.RomSettings, additionalTargetPath))
                                ? Array.Empty<string>()
                                : new[] { BuildLaunchArguments(ctx.RomSettings, additionalTargetPath) }
                        });
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Installed additional Xbox 360 disc to: {additionalTargetPath}");
                    }
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
            var platformContentId = ResolvePlatformContentId(inspection, ctx.Logger);
            ctx.Logger?.Write(PlatformLogLevel.Info, "Configured emulator: xenia");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {targetPath}");
            TryInstallOptionalPackages(ctx, inspection, targetPath);

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "Xbox 360 install completed.",
                ExecutablePath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                AdditionalApplications = additionalApplications,
                PlatformContentId = platformContentId,
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
            var platformContentId = ResolveUninstallTitleId(ctx, ctx.Logger);
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

            TryDeleteXeniaContent(platformContentId, ctx.EmulatorExecutablePath, ctx.Logger, notes, ref removed);

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

        private static List<Xbox360ContentCandidate> BuildDiscCandidates(Xbox360GameInspectionResult inspection)
        {
            var discs = new List<Xbox360ContentCandidate>();
            if (!string.IsNullOrWhiteSpace(inspection.LaunchArtifactPath))
            {
                discs.AddRange(inspection.CandidateArtifacts.Where(candidate => string.Equals(candidate.Path, inspection.LaunchArtifactPath, StringComparison.OrdinalIgnoreCase)));
            }

            discs.AddRange(inspection.AdditionalLaunchArtifacts);
            return discs
                .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.DiscNumber ?? 1)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryInstallOptionalPackages(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, Xbox360GameInspectionResult inspection, string targetPath)
        {
            if (inspection.PackageArchives.Count == 0)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No Xbox 360 update/DLC package archives detected during inspection.");
                return;
            }

            var xeniaRoot = ResolveXeniaRoot(ctx.RomSettings);
            if (string.IsNullOrWhiteSpace(xeniaRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, "Xbox 360 package installation skipped: unable to resolve Xenia root from emulator configuration.");
                return;
            }

            foreach (var package in inspection.PackageArchives)
            {
                try
                {
                    var resolvedTitleId = !string.IsNullOrWhiteSpace(package.TitleId)
                        ? package.TitleId
                        : inspection.TitleId;
                    if (string.IsNullOrWhiteSpace(resolvedTitleId))
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 package skipped because Title ID could not be determined: {package.Path}");
                        continue;
                    }

                    var contentSubdirectory = string.Equals(package.PackageType, "update", StringComparison.OrdinalIgnoreCase)
                        ? "000B0000"
                        : "00000002";
                    var installDirectory = Path.Combine(xeniaRoot, "content", "0000000000000000", resolvedTitleId, contentSubdirectory);
                    ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing Xbox 360 {package.PackageType} package '{package.Path}' to '{installDirectory}'.");
                    ExtractPackageArchive(package.Path, resolvedTitleId, contentSubdirectory, installDirectory, ctx.Logger);
                    ctx.Logger?.Write(PlatformLogLevel.Info, $"Xbox 360 {package.PackageType} package installed successfully: {package.Path}");
                }
                catch (Exception ex)
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 optional package install failed for '{package.Path}': {ex.Message}");
                }
            }
        }

        private static string ResolveXeniaRoot(RomInstallSettings? settings)
        {
            var emulatorExecutablePath = settings?.EmulatorExecutablePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emulatorExecutablePath))
            {
                return string.Empty;
            }

            return Path.GetDirectoryName(emulatorExecutablePath) ?? string.Empty;
        }

        private static string ResolveUninstallTitleId(UninstallContext ctx, IPlatformLogger? logger)
        {
            if (!string.IsNullOrWhiteSpace(ctx.PlatformContentId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall using recorded Title ID '{ctx.PlatformContentId}'.");
                return ctx.PlatformContentId.Trim().ToUpperInvariant();
            }

            var inspectedTitleId = TryResolveTitleIdFromInstalledContent(ctx, logger);
            if (!string.IsNullOrWhiteSpace(inspectedTitleId))
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall recovered Title ID '{inspectedTitleId}' by inspecting installed content.");
                return inspectedTitleId;
            }

            var candidates = new[]
            {
                ctx.InstalledPath,
                Path.GetFileName(ctx.InstalledPath ?? string.Empty),
                ctx.InstallRootPath,
                Path.GetFileName(ctx.InstallRootPath ?? string.Empty),
                ctx.ArchivePath,
                Path.GetFileName(ctx.ArchivePath ?? string.Empty),
                ctx.GameName
            };

            foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var resolved = TryExtractTitleId(candidate!);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall recovered Title ID '{resolved}' from '{candidate}'.");
                    return resolved;
                }
            }

            logger?.Write(PlatformLogLevel.Info, "Xbox 360 uninstall could not recover a Title ID from uninstall context.");
            return string.Empty;
        }

        public static string ResolvePlatformContentId(Xbox360GameInspectionResult inspection, IPlatformLogger? logger)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(inspection.TitleId))
            {
                candidates.Add(inspection.TitleId.Trim().ToUpperInvariant());
            }

            candidates.AddRange(inspection.CandidateArtifacts
                .Select(candidate => candidate?.TitleId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim().ToUpperInvariant()));

            candidates.AddRange(inspection.PackageArchives
                .Select(package => package?.TitleId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim().ToUpperInvariant()));

            var distinct = candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                logger?.Write(PlatformLogLevel.Warning, "Xbox 360 install could not determine a Title ID to persist for uninstall cleanup.");
                return string.Empty;
            }

            if (distinct.Count == 1)
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 install resolved persistent Title ID '{distinct[0]}' for uninstall cleanup.");
                return distinct[0];
            }

            logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 install found multiple Title ID candidates ({string.Join(", ", distinct)}); uninstall metadata will not be persisted to avoid mismatched cleanup.");
            return string.Empty;
        }

        public static string TryResolveTitleIdFromInstalledContent(UninstallContext ctx, IPlatformLogger? logger)
        {
            try
            {
                var inspector = new Xbox360GameInspector();

                if (!string.IsNullOrWhiteSpace(ctx.InstallRootPath)
                    && Directory.Exists(ctx.InstallRootPath))
                {
                    var inspection = inspector.Inspect(null, ctx.InstallRootPath, ctx.GameName, logger);
                    var resolved = ResolvePlatformContentId(inspection, logger);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                if (!string.IsNullOrWhiteSpace(ctx.InstalledPath)
                    && File.Exists(ctx.InstalledPath))
                {
                    var inspection = inspector.Inspect(ctx.InstalledPath, null, ctx.GameName, logger);
                    var resolved = ResolvePlatformContentId(inspection, logger);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                if (!string.IsNullOrWhiteSpace(ctx.ArchivePath)
                    && File.Exists(ctx.ArchivePath))
                {
                    var inspection = inspector.Inspect(ctx.ArchivePath, null, ctx.GameName, logger);
                    var resolved = ResolvePlatformContentId(inspection, logger);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Xbox 360 uninstall failed to inspect installed content for Title ID recovery: {ex.Message}");
            }

            return string.Empty;
        }

        private static void ExtractPackageArchive(string packagePath, string titleId, string contentSubdirectory, string installDirectory, IPlatformLogger? logger)
        {
            Directory.CreateDirectory(installDirectory);
            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                var relativePath = NormalizePackageRelativePath(entry.FullName, titleId, contentSubdirectory);
                var destinationPath = Path.Combine(installDirectory, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                logger?.Write(PlatformLogLevel.Info, $"Extracting Xbox 360 package entry '{entry.FullName}' to '{destinationPath}'.");
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        private static string NormalizePackageRelativePath(string entryName, string titleId, string contentSubdirectory)
        {
            var segments = entryName.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return string.Empty;
            }

            if (string.Equals(segments[0], titleId, StringComparison.OrdinalIgnoreCase)
                && segments.Length >= 3
                && string.Equals(segments[1], contentSubdirectory, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(segments.Skip(2).ToArray());
            }

            var titleIndex = Array.FindIndex(segments, segment => string.Equals(segment, titleId, StringComparison.OrdinalIgnoreCase));
            if (titleIndex >= 0)
            {
                var contentIndex = Array.FindIndex(segments, titleIndex + 1, segment => string.Equals(segment, contentSubdirectory, StringComparison.OrdinalIgnoreCase));
                if (contentIndex >= 0 && contentIndex + 1 < segments.Length)
                {
                    return Path.Combine(segments.Skip(contentIndex + 1).ToArray());
                }
            }

            var directContentIndex = Array.FindIndex(segments, segment => string.Equals(segment, contentSubdirectory, StringComparison.OrdinalIgnoreCase));
            if (directContentIndex >= 0 && directContentIndex + 1 < segments.Length)
            {
                return Path.Combine(segments.Skip(directContentIndex + 1).ToArray());
            }

            return Path.Combine(segments);
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

        private static string ResolveTargetFileName(Xbox360ContentCandidate candidate, string? gameName, string? detectedTitleName)
        {
            var sourcePath = candidate?.Path ?? string.Empty;
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

        private static void TryDeleteXeniaContent(string platformContentId, string? emulatorExecutablePath, IPlatformLogger? logger, List<string> notes, ref int removed)
        {
            if (string.IsNullOrWhiteSpace(platformContentId))
            {
                logger?.Write(PlatformLogLevel.Info, "Xbox 360 uninstall skipped Xenia content cleanup because no Title ID was recorded.");
                return;
            }

            var xeniaRoot = ResolveXeniaRoot(new RomInstallSettings
            {
                EmulatorExecutablePath = emulatorExecutablePath
            });
            if (string.IsNullOrWhiteSpace(xeniaRoot))
            {
                notes.Add("Skipped Xenia content cleanup because emulator root could not be resolved.");
                logger?.Write(PlatformLogLevel.Warning, "Xbox 360 uninstall skipped Xenia content cleanup because emulator root could not be resolved.");
                return;
            }

            logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall resolved Xenia root to '{xeniaRoot}'.");

            var candidateRoots = ResolveXeniaTitleContentRoots(xeniaRoot, platformContentId).ToList();
            var removedAny = false;
            foreach (var titleContentRoot in candidateRoots)
            {
                var removedRoot = TryDeleteXeniaContentRoot(titleContentRoot, logger, notes, ref removed);
                removedAny = removedAny || removedRoot;
            }

            if (!removedAny)
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall found no Xenia DLC or title update content for Title ID '{platformContentId}'.");
            }
        }

        private static IEnumerable<string> ResolveXeniaTitleContentRoots(string xeniaRoot, string platformContentId)
        {
            yield return Path.Combine(xeniaRoot, "localstate", "Content", platformContentId);
            yield return Path.Combine(xeniaRoot, "content", "0000000000000000", platformContentId);
        }

        private static bool TryDeleteXeniaContentRoot(string titleContentRoot, IPlatformLogger? logger, List<string> notes, ref int removed)
        {
            if (!Directory.Exists(titleContentRoot))
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall found no Xenia title content root at '{titleContentRoot}'.");
                return false;
            }

            var removedAny = false;
            removedAny |= TryDeleteXeniaContentSubdirectory(titleContentRoot, "00000002", "DLC", logger, notes, ref removed);
            removedAny |= TryDeleteXeniaContentSubdirectory(titleContentRoot, "000B0000", "title update", logger, notes, ref removed);

            TryDeleteEmptyDirectory(titleContentRoot, "title content root", logger, notes, ref removed);
            return removedAny;
        }

        private static bool TryDeleteXeniaContentSubdirectory(string titleContentRoot, string subdirectoryName, string contentLabel, IPlatformLogger? logger, List<string> notes, ref int removed)
        {
            var subdirectoryPath = Path.Combine(titleContentRoot, subdirectoryName);
            if (!Directory.Exists(subdirectoryPath))
            {
                logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall found no Xenia {contentLabel} directory at '{subdirectoryPath}'.");
                return false;
            }

            try
            {
                logger?.Write(PlatformLogLevel.Info, $"Deleting Xbox 360 Xenia {contentLabel} directory: {subdirectoryPath}");
                Directory.Delete(subdirectoryPath, recursive: true);
                removed++;
                return true;
            }
            catch (Exception ex)
            {
                notes.Add($"Failed to delete Xbox 360 Xenia {contentLabel} directory '{subdirectoryPath}': {ex.Message}");
                logger?.Write(PlatformLogLevel.Warning, $"Failed to delete Xbox 360 Xenia {contentLabel} directory '{subdirectoryPath}': {ex.Message}");
                return false;
            }
        }

        private static void TryDeleteEmptyDirectory(string directoryPath, string label, IPlatformLogger? logger, List<string> notes, ref int removed)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    logger?.Write(PlatformLogLevel.Info, $"Xbox 360 uninstall left non-empty Xenia {label} '{directoryPath}' in place.");
                    return;
                }

                logger?.Write(PlatformLogLevel.Info, $"Deleting empty Xbox 360 Xenia {label}: {directoryPath}");
                Directory.Delete(directoryPath, recursive: false);
                removed++;
            }
            catch (Exception ex)
            {
                notes.Add($"Failed to delete empty Xbox 360 Xenia {label} '{directoryPath}': {ex.Message}");
                logger?.Write(PlatformLogLevel.Warning, $"Failed to delete empty Xbox 360 Xenia {label} '{directoryPath}': {ex.Message}");
            }
        }

        private static string TryExtractTitleId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var match = TitleIdRegex.Match(value);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
        }
    }
}

