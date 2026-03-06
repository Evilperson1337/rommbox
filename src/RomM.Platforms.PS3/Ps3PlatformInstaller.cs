using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.PS3.Inspection;
using RomM.Platforms.PS3.Packages;

namespace RomM.Platforms.PS3
{
    public sealed class Ps3PlatformInstaller : IPlatformInstaller
    {
        public string PlatformKey => "ps3";
        public string DisplayName => "PlayStation 3";

        public Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError> { new() { Code = "context_missing", Message = "Platform context missing." } };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "missing_installed_path", Message = "Installed path missing." } };
                return Task.FromResult(result);
            }

            if (File.Exists(installedPath))
            {
                result.RecommendedExecutablePath = installedPath;
                result.CandidateExecutablePaths = new List<string> { installedPath };
                return Task.FromResult(result);
            }

            var root = !string.IsNullOrWhiteSpace(ctx.InstallRootPath) ? ctx.InstallRootPath : installedPath;
            if (!Directory.Exists(root))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "installed_missing", Message = "Installed content not found on disk." } };
                result.IsInstalled = false;
                return Task.FromResult(result);
            }

            var inspector = new Ps3GameInspector();
            var inspection = inspector.Inspect(root, ctx.Logger);
            if (inspection.Format == Ps3GameFormat.JbFolder && !string.IsNullOrWhiteSpace(inspection.EbootPath) && File.Exists(inspection.EbootPath))
            {
                result.RecommendedExecutablePath = inspection.EbootPath;
                result.CandidateExecutablePaths = new List<string> { inspection.EbootPath };
            }
            else
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "ps3_no_executable", Message = "Unable to resolve PS3 EBOOT or ISO path." } };
            }

            return Task.FromResult(result);
        }

        public async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return new InstallResult { Success = false, Message = "Install context missing." };
            }

            var settings = ctx.Settings;
            var installRoot = ResolvePs3InstallRoot(ctx.InstallDirectory, settings?.Ps3GameDirectory);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return new InstallResult { Success = false, Message = "PS3 install directory missing." };
            }

            Directory.CreateDirectory(installRoot);
            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Inspecting PS3 content...", 5));
            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS3 install starting. InstallRoot='{installRoot}', ExtractedPath='{ctx.ExtractedPath ?? string.Empty}', ArchivePath='{ctx.ArchivePath ?? string.Empty}'.");

            var inspection = InspectContent(ctx, installRoot);
            if (inspection == null)
            {
                return new InstallResult { Success = false, Message = "PS3 inspection failed." };
            }

            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS3GameInspector result: Format={inspection.Format}, GameRoot='{inspection.GameRootPath}', Eboot='{inspection.EbootPath}', TitleId='{inspection.TitleId}'.");

            string finalExecutable = string.Empty;
            string installRootPath = string.Empty;
            if (inspection.Format == Ps3GameFormat.JbFolder)
            {
                progress?.Report(new InstallProgress("Installing", "Copying JB folder...", 15));
                var targetFolderName = ResolveGameFolderName(ctx.GameName, inspection.Title, inspection.GameRootPath);
                var targetRoot = Path.Combine(installRoot, targetFolderName);
                var sourceRoot = inspection.GameRootPath;
                var normalizedSource = Path.GetFullPath(sourceRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedTarget = Path.GetFullPath(targetRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase)
                    || normalizedSource.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || normalizedTarget.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Info, "PS3 install source already in target directory; skipping copy.");
                    installRootPath = sourceRoot;
                }
                else
                {
                    if (Directory.Exists(targetRoot))
                    {
                        Directory.Delete(targetRoot, true);
                    }
                    DirectoryCopy(sourceRoot, targetRoot);
                    installRootPath = targetRoot;
                }

                if (!string.IsNullOrWhiteSpace(inspection.Ps3GamePath))
                {
                    var relativePs3Game = Path.GetRelativePath(inspection.GameRootPath, inspection.Ps3GamePath);
                    var finalBase = string.Equals(installRootPath, targetRoot, StringComparison.OrdinalIgnoreCase)
                        ? targetRoot
                        : installRootPath;
                    var finalPs3GamePath = Path.Combine(finalBase, relativePs3Game);
                    inspection.Ps3GamePath = finalPs3GamePath;
                    inspection.ParamSfoPath = Path.Combine(finalPs3GamePath, "PARAM.SFO");
                    inspection.EbootPath = Path.Combine(finalPs3GamePath, "USRDIR", "EBOOT.BIN");
                }

                if (!string.IsNullOrWhiteSpace(inspection.EbootPath))
                {
                    finalExecutable = inspection.EbootPath;
                }
            }
            else if (inspection.Format == Ps3GameFormat.DecryptedIso)
            {
                progress?.Report(new InstallProgress("Installing", "Copying ISO...", 15));
                var isoSource = inspection.IsoPath;
                var isoName = ResolveIsoFileName(ctx.GameName, inspection.IsoPath);
                var isoTarget = Path.Combine(installRoot, isoName);
                File.Copy(isoSource, isoTarget, true);
                finalExecutable = isoTarget;
                installRootPath = installRoot;
            }
            else
            {
                return new InstallResult { Success = false, Message = "Unsupported PS3 content format." };
            }

            progress?.Report(new InstallProgress("Installing", "Installing DLC/updates...", 50));
            await InstallOptionalContentAsync(ctx, inspection, installRootPath, ct).ConfigureAwait(false);

            progress?.Report(new InstallProgress("Installing", "PS3 install completed.", 100));
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path resolved: '{finalExecutable}'.");
            return new InstallResult
            {
                Success = true,
                Message = "PS3 install completed.",
                ExecutablePath = finalExecutable,
                Arguments = Array.Empty<string>(),
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
            progress?.Report(new InstallProgress("Uninstall", "Removing PS3 content...", 0, true));

            var removed = 0;
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(ctx.InstalledPath))
            {
                try
                {
                    if (File.Exists(ctx.InstalledPath))
                    {
                        File.Delete(ctx.InstalledPath);
                        removed++;
                    }
                    else if (Directory.Exists(ctx.InstalledPath))
                    {
                        Directory.Delete(ctx.InstalledPath, true);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete '{ctx.InstalledPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PS3 uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath)
                && (File.Exists(ctx.InstalledPath) || Directory.Exists(ctx.InstalledPath));
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PS3 install verified." : "PS3 install missing on disk."
            });
        }

        private static string ResolvePs3InstallRoot(string? installDirectory, string? overrideDirectory)
        {
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                return overrideDirectory;
            }

            return installDirectory ?? string.Empty;
        }

        private Ps3GameInspectorResult? InspectContent(InstallContext ctx, string installRoot)
        {
            var inspector = new Ps3GameInspector();
            if (!string.IsNullOrWhiteSpace(ctx.ExtractedPath) && Directory.Exists(ctx.ExtractedPath))
            {
                return inspector.Inspect(ctx.ExtractedPath, ctx.Logger);
            }

            if (!string.IsNullOrWhiteSpace(ctx.ArchivePath) && File.Exists(ctx.ArchivePath)
                && Path.GetExtension(ctx.ArchivePath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                return inspector.Inspect(ctx.ArchivePath, ctx.Logger);
            }

            if (!string.IsNullOrWhiteSpace(ctx.ArchivePath) && File.Exists(ctx.ArchivePath))
            {
                var extension = Path.GetExtension(ctx.ArchivePath);
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"PS3 install failed: extracted content missing for archive '{extension}'.");
            }

            ctx.Logger?.Write(PlatformLogLevel.Warning, "PS3 install failed: extracted content missing.");
            return null;
        }

        private async Task InstallOptionalContentAsync(InstallContext ctx, Ps3GameInspectorResult inspection, string installRootPath, CancellationToken ct)
        {
            var settings = ctx.Settings;
            var baseTitleId = inspection.TitleId;
            var preferMetadata = settings?.PreferMetadataBasedPackageMatching ?? false;
            var skipRegionMismatch = settings?.SkipRegionMismatchedDlc ?? false;
            var skipUnmatchedRap = settings?.SkipUnmatchedRapFiles ?? false;
            var rpcs3Exe = ResolveRpcs3ExecutablePath(settings, ctx.Logger, ctx.RomSettings?.EmulatorId);
            if (string.IsNullOrWhiteSpace(rpcs3Exe) && !string.IsNullOrWhiteSpace(ctx.RomSettings?.EmulatorId))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"RPCS3 executable not resolved for EmulatorId='{ctx.RomSettings.EmulatorId}'.");
            }

            var hasDlc = inspection.DlcPackages.Count > 0;
            var hasUpdates = inspection.UpdatePackages.Count > 0;
            var hasRaps = inspection.RapFiles.Count > 0;
            ctx.Logger?.Write(PlatformLogLevel.Info, $"PS3 optional content detected: DLC={inspection.DlcPackages.Count}, Updates={inspection.UpdatePackages.Count}, RAPs={inspection.RapFiles.Count}.");
            if (!hasDlc)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No PKG DLC packages detected; skipping DLC installation.");
            }
            if (!hasUpdates)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No PKG update packages detected; skipping update installation.");
            }
            if (!hasRaps)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No RAP files detected; skipping license installation.");
            }

            var installer = new Rpcs3PackageInstaller(ctx.Logger);
            var hasPackages = hasDlc || hasUpdates;
            var shouldInstallPackages = (hasPackages || hasRaps) && EnsureRpcs3Available(ctx.Logger, rpcs3Exe);
            if ((hasPackages || hasRaps) && !shouldInstallPackages)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, "PKG/RAP content detected but RPCS3 executable unavailable; skipping package installation.");
            }
            if (hasUpdates)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Detected {inspection.UpdatePackages.Count} update packages.");
            }
            if (hasDlc)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Detected {inspection.DlcPackages.Count} DLC packages.");
            }
            if (hasRaps)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Detected {inspection.RapFiles.Count} RAP licenses.");
            }

            if (!shouldInstallPackages)
            {
                CopyRapFiles(ctx, inspection, skipUnmatchedRap, rpcs3Exe);
                return;
            }

            var sharedInstallRoot = ResolveSharedInstallRoot(installRootPath, ctx.Logger);
            if (string.IsNullOrWhiteSpace(sharedInstallRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, "PS3 shared install directory unavailable; skipping PKG/RAP installation.");
                CopyRapFiles(ctx, inspection, skipUnmatchedRap, rpcs3Exe);
                return;
            }

            ctx.Logger?.Write(PlatformLogLevel.Info, $"PKG install directory resolved: '{sharedInstallRoot}'. Exists={Directory.Exists(sharedInstallRoot)}.");

            var stagedItems = new List<(string SourcePath, string FileType, string? OriginalPath)>();
            var orderedUpdates = inspection.UpdatePackages
                .Select(pkg => (Package: pkg, Version: ResolveUpdateVersion(pkg, ctx.Logger)))
                .OrderBy(entry => entry.Version ?? new Version(int.MaxValue, 0))
                .ThenBy(entry => entry.Package.Path, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Package)
                .ToList();

            foreach (var pkg in orderedUpdates)
            {
                if (!ShouldInstallPackage(baseTitleId, pkg, skipRegionMismatch, preferMetadata, ctx.Logger))
                {
                    continue;
                }

                StagePackage(sharedInstallRoot, pkg.Path, "PKG", ctx.Logger, stagedItems);
            }

            foreach (var pkg in inspection.DlcPackages.OrderBy(pkg => pkg.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldInstallPackage(baseTitleId, pkg, skipRegionMismatch, preferMetadata, ctx.Logger))
                {
                    continue;
                }

                StagePackage(sharedInstallRoot, pkg.Path, "PKG", ctx.Logger, stagedItems);
            }

            var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(inspection.TitleId))
            {
                allowedIds.Add(inspection.TitleId);
            }

            foreach (var pkg in inspection.DlcPackages.Concat(inspection.UpdatePackages))
            {
                if (!string.IsNullOrWhiteSpace(pkg.TitleId))
                {
                    allowedIds.Add(pkg.TitleId);
                }
            }

            foreach (var rap in inspection.RapFiles)
            {
                if (skipUnmatchedRap && !string.IsNullOrWhiteSpace(rap.TitleId) && !allowedIds.Contains(rap.TitleId))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Skipping RAP '{rap.Path}' - no matching title id.");
                    continue;
                }

                if (skipUnmatchedRap && string.IsNullOrWhiteSpace(rap.TitleId))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Skipping RAP '{rap.Path}' - title id missing.");
                    continue;
                }

                StagePackage(sharedInstallRoot, rap.Path, "RAP", ctx.Logger, stagedItems);
            }

            if (stagedItems.Count == 0)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No eligible PKG/RAP files staged for RPCS3 install; skipping installpkg.");
                return;
            }

            ctx.Logger?.Write(PlatformLogLevel.Info, $"RPCS3 batch install: staging {stagedItems.Count} files in '{sharedInstallRoot}'.");
            var batchResult = await installer.InstallPackagesFromDirectoryAsync(rpcs3Exe, sharedInstallRoot, ct).ConfigureAwait(false);
            LogPackageResult(ctx.Logger, sharedInstallRoot, batchResult);
            if (!batchResult.Success)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, "RPCS3 batch install failed; staged files left in shared install directory for manual review.");
                return;
            }

            foreach (var item in stagedItems)
            {
                TryDeleteFile(ctx.Logger, item.SourcePath, true, item.FileType);
                if (!string.IsNullOrWhiteSpace(item.OriginalPath))
                {
                    TryDeleteEmptyParentDirectories(ctx.Logger, item.OriginalPath, item.FileType);
                }
            }

            TryDeleteSharedInstallDirectory(ctx.Logger, sharedInstallRoot);
        }

        private static void LogPackageResult(IPlatformLogger? logger, string packagePath, PackageInstallResult result)
        {
            logger?.Write(PlatformLogLevel.Info, $"RPCS3 installpkg '{packagePath}' exited {result.ExitCode}. Duration={result.Duration.TotalSeconds:0.0}s");
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                logger?.Write(PlatformLogLevel.Debug, result.StdOut);
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                logger?.Write(result.Success ? PlatformLogLevel.Debug : PlatformLogLevel.Warning, result.StdErr);
            }
            if (!result.Success)
            {
                logger?.Write(PlatformLogLevel.Warning, result.Message);
            }
        }

        private static bool ShouldInstallPackage(string baseTitleId, Ps3PackageFile pkg, bool skipRegionMismatch, bool preferMetadata, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(baseTitleId))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Base Title ID missing. Installing package '{pkg.Path}' without match validation.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(pkg.TitleId))
            {
                if (preferMetadata)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Package '{pkg.Path}' missing Title ID; skipped due to metadata preference.");
                    return false;
                }

                logger?.Write(PlatformLogLevel.Warning, $"Package '{pkg.Path}' missing Title ID; installing with filename fallback.");
                return true;
            }

            if (!string.Equals(baseTitleId, pkg.TitleId, StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Package Title ID '{pkg.TitleId}' does not match base '{baseTitleId}'. Skipping.");
                return false;
            }

            if (skipRegionMismatch && pkg.Region != Ps3GameRegion.Unknown)
            {
                var baseRegion = Ps3GameInspector.ResolveRegion(baseTitleId);
                if (baseRegion != Ps3GameRegion.Unknown && baseRegion != pkg.Region)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Package region mismatch ({pkg.Region}) for base '{baseTitleId}'. Skipping.");
                    return false;
                }
            }

            return true;
        }

        private static bool EnsureRpcs3Available(IPlatformLogger? logger, string rpcs3ExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(rpcs3ExecutablePath) || !File.Exists(rpcs3ExecutablePath))
            {
                logger?.Write(PlatformLogLevel.Error, "ERROR: RPCS3 executable not found; unable to install PKG packages.");
                return false;
            }

            return true;
        }

        private static string ResolveRpcs3ExecutablePath(
            RomM.Platforms.Abstractions.Models.Install.PlatformInstallSettings? settings,
            IPlatformLogger? logger,
            string? emulatorId)
        {
            var configured = settings?.Rpcs3ExecutablePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                logger?.Write(PlatformLogLevel.Info, $"RPCS3 executable resolved from settings: '{configured}'.");
                return NormalizePath(configured);
            }

            var fallback = ResolveRpcs3FromLaunchBox(emulatorId);
            if (!string.IsNullOrWhiteSpace(fallback) && File.Exists(fallback))
            {
                logger?.Write(PlatformLogLevel.Info, $"RPCS3 executable resolved from LaunchBox: '{fallback}'.");
                return NormalizePath(fallback);
            }

            return string.Empty;
        }

        private static string ResolveRpcs3FromLaunchBox(string? emulatorId)
        {
            try
            {
                var pluginHelperType = Type.GetType("Unbroken.LaunchBox.Plugins.PluginHelper, Unbroken.LaunchBox.Plugins");
                if (pluginHelperType == null)
                {
                    return string.Empty;
                }

                var dataManager = pluginHelperType
                    .GetProperty("DataManager", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (dataManager == null)
                {
                    return string.Empty;
                }

                var dataManagerType = dataManager.GetType();
                if (!string.IsNullOrWhiteSpace(emulatorId))
                {
                    var emulator = dataManagerType.GetMethod("GetEmulatorById")?.Invoke(dataManager, new object[] { emulatorId });
                    var appPath = emulator?.GetType().GetProperty("ApplicationPath")?.GetValue(emulator) as string;
                    if (!string.IsNullOrWhiteSpace(appPath))
                    {
                        return appPath;
                    }
                }

                var emulators = dataManagerType.GetMethod("GetAllEmulators")?.Invoke(dataManager, Array.Empty<object>()) as IEnumerable;
                if (emulators == null)
                {
                    return string.Empty;
                }

                foreach (var emulator in emulators)
                {
                    if (emulator == null)
                    {
                        continue;
                    }

                    var appPath = emulator.GetType().GetProperty("ApplicationPath")?.GetValue(emulator) as string;
                    if (!string.IsNullOrWhiteSpace(appPath)
                        && appPath.IndexOf("rpcs3", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return appPath;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static Version ParseVersion(string version)
        {
            if (Version.TryParse(version, out var parsed))
            {
                return parsed;
            }

            return new Version(0, 0);
        }

        private static Version? ResolveUpdateVersion(Ps3PackageFile pkg, IPlatformLogger? logger)
        {
            if (!string.IsNullOrWhiteSpace(pkg.Version) && Version.TryParse(pkg.Version, out var parsed))
            {
                return parsed;
            }

            var fileName = Path.GetFileNameWithoutExtension(pkg.Path) ?? string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(fileName, "A(?<major>\\d{2})(?<minor>\\d{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Update package version not found in filename '{fileName}'.");
                return null;
            }

            if (int.TryParse(match.Groups["major"].Value, out var major)
                && int.TryParse(match.Groups["minor"].Value, out var minor))
            {
                return new Version(major, minor);
            }

            logger?.Write(PlatformLogLevel.Warning, $"Update package version parse failed for '{fileName}'.");
            return null;
        }

        private static void TryDeleteFile(IPlatformLogger? logger, string path, bool shouldDelete, string fileType)
        {
            if (!shouldDelete || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    logger?.Write(PlatformLogLevel.Info, $"Removed processed {fileType} file '{path}'.");
                    TryDeleteEmptyParentDirectories(logger, path, fileType);
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to remove {fileType} file '{path}': {ex.Message}");
            }
        }

        private static void TryDeleteEmptyParentDirectories(IPlatformLogger? logger, string path, string fileType)
        {
            try
            {
                var current = Path.GetDirectoryName(path);
                while (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                {
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        break;
                    }

                    Directory.Delete(current);
                    logger?.Write(PlatformLogLevel.Info, $"Removed empty {fileType} directory '{current}'.");
                    current = Path.GetDirectoryName(current);
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to remove empty {fileType} directories for '{path}': {ex.Message}");
            }
        }

        private static string ResolveSharedInstallRoot(string installRootPath, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(installRootPath))
            {
                return string.Empty;
            }

            try
            {
                var sharedRoot = Path.Combine(installRootPath, ".rpcs3-install");
                Directory.CreateDirectory(sharedRoot);
                return sharedRoot;
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to create shared RPCS3 install directory: {ex.Message}");
                return string.Empty;
            }
        }

        private static void StagePackage(
            string sharedInstallRoot,
            string sourcePath,
            string fileType,
            IPlatformLogger? logger,
            List<(string SourcePath, string FileType, string? OriginalPath)> stagedItems)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                logger?.Write(PlatformLogLevel.Warning, $"{fileType} path missing or invalid: '{sourcePath}'.");
                return;
            }

            var fileName = Path.GetFileName(sourcePath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"{Guid.NewGuid():N}.{fileType.ToLowerInvariant()}";
            }

            var targetPath = ResolveUniqueTargetPath(sharedInstallRoot, fileName);
            var originalPath = sourcePath;

            try
            {
                File.Move(sourcePath, targetPath);
                stagedItems.Add((targetPath, fileType, originalPath));
                logger?.Write(PlatformLogLevel.Info, $"Staged {fileType} '{sourcePath}' -> '{targetPath}'.");
            }
            catch (Exception)
            {
                try
                {
                    File.Copy(sourcePath, targetPath, true);
                    File.Delete(sourcePath);
                    stagedItems.Add((targetPath, fileType, originalPath));
                    logger?.Write(PlatformLogLevel.Info, $"Staged {fileType} '{sourcePath}' -> '{targetPath}' (copy/delete).");
                }
                catch (Exception ex)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Failed to stage {fileType} '{sourcePath}': {ex.Message}");
                }
            }
        }

        private static string ResolveUniqueTargetPath(string root, string fileName)
        {
            var candidate = Path.Combine(root, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName) ?? "file";
            var extension = Path.GetExtension(fileName);
            var index = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(root, $"{baseName}_{index}{extension}");
                index++;
            }

            return candidate;
        }

        private static void TryDeleteSharedInstallDirectory(IPlatformLogger? logger, string sharedInstallRoot)
        {
            if (string.IsNullOrWhiteSpace(sharedInstallRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(sharedInstallRoot))
                {
                    Directory.Delete(sharedInstallRoot, true);
                    logger?.Write(PlatformLogLevel.Info, $"Removed shared RPCS3 install directory '{sharedInstallRoot}'.");
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to remove shared RPCS3 install directory '{sharedInstallRoot}': {ex.Message}");
            }
        }

        private static void CopyRapFiles(InstallContext ctx, Ps3GameInspectorResult inspection, bool skipUnmatched, string rpcs3ExecutablePath)
        {
            if (inspection.RapFiles.Count == 0)
            {
                ctx.Logger?.Write(PlatformLogLevel.Info, "No RAP files detected; skipping license installation.");
                return;
            }

            var settings = ctx.Settings;
            var defaultLicensePath = ResolveDefaultLicenseDirectory(rpcs3ExecutablePath);
            var licenseDir = string.IsNullOrWhiteSpace(settings?.Rpcs3LicenseDirectory)
                ? defaultLicensePath
                : settings.Rpcs3LicenseDirectory;

            if (string.IsNullOrWhiteSpace(licenseDir))
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, "RPCS3 license directory not configured and RPCS3 executable unavailable; skipping RAP files.");
                return;
            }

            Directory.CreateDirectory(licenseDir);
            ctx.Logger?.Write(PlatformLogLevel.Info, $"RPCS3 license directory resolved: '{licenseDir}'.");
            var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(inspection.TitleId))
            {
                allowedIds.Add(inspection.TitleId);
            }

            foreach (var pkg in inspection.DlcPackages.Concat(inspection.UpdatePackages))
            {
                if (!string.IsNullOrWhiteSpace(pkg.TitleId))
                {
                    allowedIds.Add(pkg.TitleId);
                }
            }

            foreach (var rap in inspection.RapFiles)
            {
                if (skipUnmatched && !string.IsNullOrWhiteSpace(rap.TitleId) && !allowedIds.Contains(rap.TitleId))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Skipping RAP '{rap.Path}' - no matching title id.");
                    continue;
                }

                if (skipUnmatched && string.IsNullOrWhiteSpace(rap.TitleId))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Skipping RAP '{rap.Path}' - title id missing.");
                    continue;
                }

                var target = Path.Combine(licenseDir, Path.GetFileName(rap.Path) ?? string.Empty);
                if (File.Exists(target))
                {
                    continue;
                }

                File.Copy(rap.Path, target, true);
                ctx.Logger?.Write(PlatformLogLevel.Info, $"Installing RAP license '{rap.Path}'.");
                TryDeleteFile(ctx.Logger, rap.Path, true, "RAP");
            }
        }

        private static string ResolveDefaultLicenseDirectory(string rpcs3ExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(rpcs3ExecutablePath))
            {
                return string.Empty;
            }

            var normalized = NormalizePath(rpcs3ExecutablePath);
            var root = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            return Path.Combine(root, "dev_hdd0", "home", "00000001", "exdata");
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var root = ResolveLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return path;
            }

            try
            {
                if (!Path.IsPathRooted(path))
                {
                    return Path.GetFullPath(Path.Combine(root, path));
                }
            }
            catch
            {
            }

            return path;
        }

        private static string ResolveLaunchBoxRootDirectory()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var fullBase = Path.GetFullPath(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var candidate = fullBase;
            if (string.Equals(Path.GetFileName(candidate), "Core", StringComparison.OrdinalIgnoreCase))
            {
                candidate = Directory.GetParent(candidate)?.FullName ?? candidate;
            }

            for (var i = 0; i < 6; i++)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    break;
                }

                if (File.Exists(Path.Combine(candidate, "LaunchBox.exe"))
                    || (Directory.Exists(Path.Combine(candidate, "Plugins"))
                        && Directory.Exists(Path.Combine(candidate, "Games"))))
                {
                    return candidate;
                }

                if (string.Equals(Path.GetFileName(candidate), "Core", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(candidate)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parent)
                        && (File.Exists(Path.Combine(parent, "LaunchBox.exe"))
                            || (Directory.Exists(Path.Combine(parent, "Plugins"))
                                && Directory.Exists(Path.Combine(parent, "Games")))))
                    {
                        return parent;
                    }
                }

                candidate = Directory.GetParent(candidate)?.FullName;
            }

            return fullBase;
        }

        private static string ResolveGameFolderName(string? gameName, string? title, string rootPath)
        {
            var name = !string.IsNullOrWhiteSpace(gameName) ? gameName : title;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "PS3_Game";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "PS3_Game" : name.Trim();
        }

        private static string ResolveIsoFileName(string? gameName, string isoSource)
        {
            var baseName = !string.IsNullOrWhiteSpace(gameName)
                ? gameName
                : Path.GetFileNameWithoutExtension(isoSource) ?? "PS3_Game";
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }

            return string.Concat(baseName.Trim(), ".iso");
        }

        private static void DirectoryCopy(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var target = Path.Combine(destinationDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destinationDir);
                File.Copy(file, target, true);
            }
        }
    }
}
