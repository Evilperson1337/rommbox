using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Install;
using RomMbox.Models.Install;
using PlatformInstallProgress = RomM.Platforms.Abstractions.Models.Install.InstallProgress;
using PlatformInstallType = RomM.Platforms.Abstractions.Models.Install.InstallType;
using RomMbox.Services.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.PlatformInstallers;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class InstallContentStep : IInstallStep
    {
        private readonly PlatformInstallerRegistry _platformInstallers;
        private readonly PlatformLoggerAdapter _platformLogger;
        private readonly ArchiveService _archiveService;

        public InstallContentStep(PlatformInstallerRegistry platformInstallers, PlatformLoggerAdapter platformLogger, ArchiveService archiveService)
        {
            _platformInstallers = platformInstallers;
            _platformLogger = platformLogger;
            _archiveService = archiveService;
        }

        public InstallPhase Phase => InstallPhase.Installing;

        public async Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.RommDetails == null)
            {
                return InstallResult.Failed(Phase, "RomM details missing.");
            }

            var platform = context.DataManager.GetPlatformByName(context.Game.Platform);
            if (platform == null)
            {
                return InstallResult.Failed(Phase, "LaunchBox platform not found.");
            }

            var installScenario = context.PlatformMapping?.InstallScenario ?? InstallScenario.Basic;
            var targetImportFile = context.PlatformMapping?.TargetImportFile ?? string.Empty;
            var installerSilentArgs = context.PlatformMapping?.InstallerSilentArgs ?? string.Empty;

            var finalPath = !string.IsNullOrWhiteSpace(context.ExtractedPath)
                ? context.ExtractedPath
                : context.ArchivePath;
            if (string.IsNullOrWhiteSpace(finalPath))
            {
                return InstallResult.Failed(Phase, "Download completed but no output file was produced.");
            }

            if (InstallDestinationService.IsWindowsPlatform(platform.Name))
            {
                context.Logger?.Info($"Windows install input: ArchivePath='{context.ArchivePath ?? string.Empty}', ExtractedPath='{context.ExtractedPath ?? string.Empty}', InstallDir='{context.InstallDirectory}'.");
                if (string.IsNullOrWhiteSpace(context.ExtractedPath) && !string.IsNullOrWhiteSpace(context.ArchivePath))
                {
                    try
                    {
                        var extractionRoot = Path.Combine(context.InstallDirectory ?? string.Empty, ".staging", context.OperationId ?? Guid.NewGuid().ToString("N"), "extracted");
                        context.Logger?.Info($"Extraction missing for Windows install; extracting archive '{context.ArchivePath}' to '{extractionRoot}'.");
                        context.ExtractedPath = await _archiveService
                            .ExtractAsync(context.ArchivePath, extractionRoot, ExtractionBehavior.Subfolder, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        context.Logger?.Error("Extraction failed before Windows install.", ex);
                        return InstallResult.Failed(Phase, $"Extraction failed: {ex.Message}");
                    }

                    if (string.IsNullOrWhiteSpace(context.ExtractedPath))
                    {
                        return InstallResult.Failed(Phase, "Extraction failed.");
                    }
                }

                if (_platformInstallers == null || !_platformInstallers.TryGetInstaller("windows", out var installer))
                {
                    return InstallResult.Failed(Phase, "Windows platform installer not available.");
                }

                progress?.Report(new InstallProgressEvent(Phase, $"Installing {context.Game.Title}...", 0));
                context.Logger?.Info($"InstallStarted. Game='{context.Game.Title}', InstallDir='{context.InstallDirectory}'.");
                if (!context.InstallStateSnapshot?.LastAttemptUtc.HasValue ?? false)
                {
                    context.InstallStateSnapshot.LastAttemptUtc = DateTimeOffset.UtcNow;
                }
                IProgress<double> installProgress = new Progress<double>(value =>
                {
                    progress?.Report(new InstallProgressEvent(Phase, $"Installing {context.Game.Title}...", value));
                });

                var stagingRoot = Path.Combine(context.InstallDirectory, ".staging", context.OperationId ?? Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingRoot);

                var platformContext = new RomM.Platforms.Abstractions.Models.Install.InstallContext
                {
                    GameName = context.Game.Title,
                    InstallDirectory = context.InstallDirectory,
                    StagingDirectory = stagingRoot,
                    ArchivePath = context.ArchivePath,
                    ExtractedPath = context.ExtractedPath,
                    Settings = PlatformInstallSettingsMapper.Map(context.PlatformMapping),
                    SelectExecutableAsync = PlatformInstallerUi.SelectExecutableAsync,
                    ConfirmAsync = PlatformInstallerUi.ConfirmAsync,
                    Logger = _platformLogger
                };

                var result = await installer
                    .InstallAsync(platformContext, new Progress<PlatformInstallProgress>(update =>
                    {
                        var percent = update.Percent.HasValue
                            ? Math.Clamp(update.Percent.Value, 0, 100)
                            : (double?)null;
                        progress?.Report(new InstallProgressEvent(Phase, update.Message ?? "Installing...", percent));
                    }), cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    TryCleanupStaging(stagingRoot, context.Logger);
                    return InstallResult.Failed(Phase, result.Message ?? "Windows install failed.");
                }

                var usedStaging = result.InstallType.HasValue
                    && result.InstallType.Value != PlatformInstallType.Installer;
                var finalInstallRoot = context.InstallDirectory;
                var stagingRewriteRoot = stagingRoot;
                if (usedStaging)
                {
                    var safeGameName = NormalizeGameFolderName(context.Game.Title, "Game");
                    var stagedGameRoot = Path.Combine(stagingRoot, safeGameName);
                    if (Directory.Exists(stagedGameRoot))
                    {
                        stagingRewriteRoot = stagedGameRoot;
                    }

                    var swapResult = TryCommitStaging(stagingRoot, context.InstallDirectory, context.Game.Title, context.Logger);
                    if (!swapResult.Success)
                    {
                        return InstallResult.Failed(Phase, swapResult.Message);
                    }
                    finalInstallRoot = swapResult.FinalInstallRoot;
                }
                else
                {
                    TryCleanupStaging(stagingRoot, context.Logger);
                }

                installProgress.Report(100);

                if (!string.IsNullOrWhiteSpace(result.ExecutablePath))
                {
                    context.InstalledExecutablePath = usedStaging
                        ? RewriteStagedPath(result.ExecutablePath, stagingRewriteRoot, finalInstallRoot)
                        : result.ExecutablePath;
                    context.InstallerArguments = result.Arguments == null
                        ? Array.Empty<string>()
                        : result.Arguments.ToArray();
                }

                context.InstallStateSnapshot.WindowsInstallType = result.InstallType?.ToString();
                context.InstallStateSnapshot.InstallRootPath = usedStaging
                    ? finalInstallRoot
                    : result.InstallRootPath ?? context.InstallDirectory;
                return InstallResult.Successful();
            }

            if (_platformInstallers != null && context.RommDetails != null)
            {
                var platformKey = context.RommDetails.PlatformId ?? string.Empty;
                var platformDisplayName = context.RommDetails.PlatformDisplayName ?? string.Empty;
                var launchBoxPlatformName = context.Game?.Platform ?? string.Empty;
                context.Logger?.Info($"Detected platform: LaunchBox='{launchBoxPlatformName}', RomMId='{platformKey}', RomMName='{platformDisplayName}'.");

                var resolvedKey = ResolveInstallerKey(platformKey, platformDisplayName, launchBoxPlatformName, _platformInstallers, context.Logger);
                if (!string.IsNullOrWhiteSpace(resolvedKey) && !string.Equals(resolvedKey, platformKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (ShouldWarnOnResolvedKeyDifference(platformKey, resolvedKey, _platformInstallers))
                    {
                        context.Logger?.Warning($"Platform key mismatch. Provided='{platformKey}', Resolved='{resolvedKey}'.");
                    }
                    else
                    {
                        context.Logger?.Info($"Mapped RomM platform identifier '{platformKey}' to installer key '{resolvedKey}'.");
                    }
                }

                var keyToUse = string.IsNullOrWhiteSpace(resolvedKey) ? platformKey : resolvedKey;
                var foundInstaller = _platformInstallers.TryGetInstallerOrFallback(keyToUse, out var romInstaller, out var usedFallbackInstaller);

                if (!foundInstaller || romInstaller == null)
                {
                    context.Logger?.Warning($"Platform installer not found for RomM platform '{platformKey}'.");
                }
                else
                {
                    if (usedFallbackInstaller)
                    {
                        var mappingFallbackEnabled = context.PlatformMapping?.UseGeneralFallbackInstaller == true;
                        var supportedExtensions = context.PlatformMapping?.SupportedFileTypes ?? string.Empty;
                        var archiveHandling = context.PlatformMapping?.ArchiveHandlingMode ?? string.Empty;
                        context.Logger?.Warning($"No dedicated plugin found for platform '{platformDisplayName}'. Falling back to GeneralPlatformInstaller. MappingFallbackEnabled={mappingFallbackEnabled}.");
                        context.Logger?.Info($"Using General ROM Plugin settings. SupportedExtensions='{supportedExtensions}', ArchiveHandling='{archiveHandling}', ExtractAfterDownload={context.PlatformMapping?.ExtractAfterDownload == true}.");
                    }

                    context.Logger?.Info($"Matched platform plugin: {romInstaller.DisplayName} ({romInstaller.PlatformKey}).");
                }

                if (romInstaller != null)
                {
                    progress?.Report(new InstallProgressEvent(Phase, $"Installing {context.Game.Title}...", 0));
                    context.Logger?.Info($"ROM install input: ArchivePath='{context.ArchivePath ?? string.Empty}', ExtractedPath='{context.ExtractedPath ?? string.Empty}', InstallDir='{context.InstallDirectory}'.");

                    var usingGeneralRomPlugin = string.Equals(romInstaller.PlatformKey, "general", StringComparison.OrdinalIgnoreCase);
                    var archiveHandlingMode = context.PlatformMapping?.ArchiveHandlingMode ?? string.Empty;
                    var generalPluginAllowsPreExtraction = usingGeneralRomPlugin
                        && (string.Equals(archiveHandlingMode, "ExtractAlways", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(archiveHandlingMode, "ExtractForInspection", StringComparison.OrdinalIgnoreCase));

                    if (context.PlatformMapping?.ExtractAfterDownload == false
                        && !string.IsNullOrWhiteSpace(context.ArchivePath)
                        && _archiveService.IsSupportedArchive(context.ArchivePath)
                        && (!usingGeneralRomPlugin || generalPluginAllowsPreExtraction))
                    {
                        var archivePolicy = context.PlatformMapping?.RomArchivePolicy ?? string.Empty;
                        var preserveArchivePlatform = string.Equals(context.RommDetails?.PlatformId, "snes", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(context.RommDetails?.PlatformId, "n64", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(context.RommDetails?.PlatformId, "arcade", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(romInstaller.PlatformKey, "snes", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(romInstaller.PlatformKey, "n64", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(romInstaller.PlatformKey, "arcade", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(keyToUse, "snes", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(keyToUse, "n64", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(keyToUse, "arcade", StringComparison.OrdinalIgnoreCase);
                        if (preserveArchivePlatform)
                        {
                            context.Logger?.Info("Archive-preserve ROM platform detected; skipping extraction before ROM install.");
                        }
                        else if (!string.Equals(archivePolicy, "Preserve", StringComparison.OrdinalIgnoreCase))
                        {
                            progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extracting ROM archive...", 0));
                            try
                            {
                                var extractionRoot = Path.Combine(context.DownloadDirectory ?? context.InstallDirectory ?? string.Empty, ".staging", context.OperationId ?? Guid.NewGuid().ToString("N"), "extracted");
                                context.Logger?.Info($"ROM install extraction requested; extracting archive '{context.ArchivePath}' to '{extractionRoot}'.");
                                context.ExtractedPath = await _archiveService
                                    .ExtractAsync(context.ArchivePath, extractionRoot, ExtractionBehavior.Subfolder, cancellationToken)
                                    .ConfigureAwait(false);
                                progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "ROM archive extracted.", 100));
                            }
                            catch (Exception ex)
                            {
                                context.Logger?.Error("ROM extraction failed before install.", ex);
                                return InstallResult.Failed(Phase, $"Extraction failed: {ex.Message}");
                            }
                        }
                    }
                    else if (usingGeneralRomPlugin
                             && !string.IsNullOrWhiteSpace(context.ArchivePath)
                             && _archiveService.IsSupportedArchive(context.ArchivePath))
                    {
                        context.Logger?.Info($"General ROM plugin selected; skipping pre-install extraction. ArchiveHandlingMode='{archiveHandlingMode}'. Downloaded artifact will be installed directly.");
                    }

                    var platformContext = new RomM.Platforms.Abstractions.Models.Install.InstallContext
                    {
                        GameName = context.Game.Title,
                        InstallDirectory = context.InstallDirectory,
                        ArchivePath = context.ArchivePath,
                        ExtractedPath = context.ExtractedPath,
                        Settings = PlatformInstallSettingsMapper.Map(context.PlatformMapping),
                        RomSettings = PlatformInstallSettingsMapper.MapRomSettings(context.PlatformMapping, context.DataManager, context.Game?.Platform),
                        Logger = _platformLogger
                    };

                    var result = await romInstaller
                        .InstallAsync(platformContext, new Progress<PlatformInstallProgress>(update =>
                        {
                            var percent = update.Percent.HasValue
                                ? Math.Clamp(update.Percent.Value, 0, 100)
                                : (double?)null;
                            progress?.Report(new InstallProgressEvent(Phase, update.Message ?? "Installing...", percent));
                        }), cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.Success)
                    {
                        return InstallResult.Failed(Phase, result.Message ?? "ROM install failed.");
                    }

                    var canonicalizeFailure = EnsureCanonicalRomInstallLayout(context, result, context.Logger, out var canonicalExecutablePath, out var canonicalInstallRootPath);
                    if (!string.IsNullOrWhiteSpace(canonicalizeFailure))
                    {
                        return InstallResult.Failed(Phase, canonicalizeFailure);
                    }

                    var cleanupFailure = FinalizeRomInstallArtifacts(context, canonicalExecutablePath, context.Logger);
                    if (!string.IsNullOrWhiteSpace(cleanupFailure))
                    {
                        return InstallResult.Failed(Phase, cleanupFailure);
                    }

                    if (!string.IsNullOrWhiteSpace(result.ExecutablePath))
                    {
                        context.InstalledExecutablePath = canonicalExecutablePath;
                        context.InstallerArguments = result.Arguments == null
                            ? Array.Empty<string>()
                            : result.Arguments.ToArray();
                    }

                    context.AdditionalApplications = result.AdditionalApplications ?? Array.Empty<RomM.Platforms.Abstractions.Models.Install.AdditionalApplicationLaunchInfo>();

                    context.InstallStateSnapshot.WindowsInstallType = result.InstallType?.ToString();
                    context.InstallStateSnapshot.PlatformContentId = result.PlatformContentId ?? string.Empty;
                    context.InstallStateSnapshot.InstallRootPath = canonicalInstallRootPath;
                    return InstallResult.Successful();
                }
            }

            if (installScenario == InstallScenario.Enhanced || installScenario == InstallScenario.Installer)
            {
                progress?.Report(new InstallProgressEvent(Phase, $"Installing {context.Game.Title}...", 0));
            }

            if (installScenario == InstallScenario.Enhanced)
            {
                finalPath = ResolveTargetFile(context.ExtractedPath, targetImportFile, finalPath, context.Logger);
                if (string.IsNullOrWhiteSpace(finalPath))
                {
                    return InstallResult.Failed(Phase, "Target import file not found in extracted content.");
                }
            }
            else if (installScenario == InstallScenario.Installer)
            {
                var installPath = ExecuteInstaller(context.ExtractedPath, context.DownloadDirectory, targetImportFile, installerSilentArgs, context.Logger);
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    return InstallResult.Failed(Phase, "Installer did not produce a valid install path.");
                }
                finalPath = installPath;
            }

            context.InstalledExecutablePath = finalPath;
            return InstallResult.Successful();
        }

        private static string ResolveTargetFile(string extractedPath, string targetImportFile, string fallbackPath, Services.Logging.LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return fallbackPath;
            }

            if (string.IsNullOrWhiteSpace(targetImportFile))
            {
                return extractedPath;
            }

            try
            {
                var match = Directory.EnumerateFiles(extractedPath, targetImportFile, SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                return string.IsNullOrWhiteSpace(match) ? fallbackPath : match;
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to locate target import file '{targetImportFile}'. {ex.Message}");
                return fallbackPath;
            }
        }

        private static string ExecuteInstaller(string extractedPath, string installDirectory, string targetImportFile, string installerSilentArgs, Services.Logging.LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return string.Empty;
            }

            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return string.Empty;
            }

            var silentArgs = string.IsNullOrWhiteSpace(installerSilentArgs)
                ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
                : installerSilentArgs;
            var quotedInstall = QuoteArgument(installDirectory);
            var arguments = $"{silentArgs} /DIR={quotedInstall}";

            logger?.Info($"Launching installer {setupPath} with args: {arguments}");
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                if (process == null)
                {
                    logger?.Warning("Failed to start installer process.");
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    logger?.Error($"Installer failed. ExitCode={process.ExitCode}. Output={output}. Error={error}");
                    return string.Empty;
                }
            }

            if (!Directory.Exists(installDirectory))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(targetImportFile))
            {
                var targetMatch = Directory.EnumerateFiles(installDirectory, targetImportFile, SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                return string.IsNullOrWhiteSpace(targetMatch) ? installDirectory : targetMatch;
            }

            return installDirectory;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static string NormalizeGameFolderName(string gameName, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(gameName) ? fallback : gameName;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Game";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Game" : cleaned.Trim();
        }

        private static StagingCommitResult TryCommitStaging(
            string stagingRoot,
            string installDirectory,
            string gameName,
            Services.Logging.LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(stagingRoot) || !Directory.Exists(stagingRoot))
            {
                return StagingCommitResult.Failed("Staging folder missing; install could not be finalized.");
            }

            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return StagingCommitResult.Failed("Install directory missing; install could not be finalized.");
            }

            try
            {
                Directory.CreateDirectory(installDirectory);

                var entries = Directory.GetFileSystemEntries(stagingRoot);
                if (entries.Length == 0)
                {
                    return StagingCommitResult.Failed("Staging folder was empty; install could not be finalized.");
                }

                var singleDirectory = entries.Length == 1 && Directory.Exists(entries[0]);
                if (singleDirectory && !Directory.EnumerateFiles(stagingRoot).Any())
                {
                    var stagedRoot = entries[0];
                    var targetRoot = Path.Combine(installDirectory, Path.GetFileName(stagedRoot));
                    if (Directory.Exists(targetRoot))
                    {
                        return StagingCommitResult.Failed($"Install target '{targetRoot}' already exists.");
                    }

                    Directory.Move(stagedRoot, targetRoot);
                    TryCleanupStaging(stagingRoot, logger);
                    return StagingCommitResult.FromSuccess(targetRoot);
                }

                var finalRoot = Path.Combine(installDirectory, NormalizeGameFolderName(gameName, "Game"));
                if (Directory.Exists(finalRoot))
                {
                    return StagingCommitResult.Failed($"Install target '{finalRoot}' already exists.");
                }

                Directory.CreateDirectory(finalRoot);
                foreach (var entry in entries)
                {
                    var name = Path.GetFileName(entry);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var destination = Path.Combine(finalRoot, name);
                    if (Directory.Exists(entry))
                    {
                        Directory.Move(entry, destination);
                    }
                    else
                    {
                        File.Move(entry, destination, overwrite: true);
                    }
                }

                TryCleanupStaging(stagingRoot, logger);
                return StagingCommitResult.FromSuccess(finalRoot);
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to finalize staging folder '{stagingRoot}': {ex.Message}");
                return StagingCommitResult.Failed("Failed to finalize staging folder.");
            }
        }

        private static string RewriteStagedPath(string executablePath, string stagingRoot, string finalRoot)
        {
            if (string.IsNullOrWhiteSpace(executablePath)
                || string.IsNullOrWhiteSpace(stagingRoot)
                || string.IsNullOrWhiteSpace(finalRoot))
            {
                return executablePath;
            }

            try
            {
                var normalizedExecutable = Path.GetFullPath(executablePath);
                var normalizedStaging = Path.GetFullPath(stagingRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedFinal = Path.GetFullPath(finalRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!normalizedExecutable.StartsWith(normalizedStaging, StringComparison.OrdinalIgnoreCase))
                {
                    return executablePath;
                }

                var relative = normalizedExecutable.Substring(normalizedStaging.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.Combine(normalizedFinal, relative);
            }
            catch
            {
                return executablePath;
            }
        }

        private static void TryCleanupStaging(string stagingRoot, Services.Logging.LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(stagingRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }

                var stagingParent = Path.GetDirectoryName(stagingRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(stagingParent)
                    && Directory.Exists(stagingParent)
                    && !Directory.EnumerateFileSystemEntries(stagingParent).Any())
                {
                    Directory.Delete(stagingParent, recursive: false);
                }
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to clean staging root '{stagingRoot}': {ex.Message}");
            }
        }

        private static string FinalizeRomInstallArtifacts(InstallContext context, string finalExecutablePath, Services.Logging.LoggingService logger)
        {
            if (context == null)
            {
                return string.Empty;
            }

            var shouldDeleteArchive = ShouldDeleteArchiveAfterInstall(context, finalExecutablePath);
            if (shouldDeleteArchive)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(context.ArchivePath) && File.Exists(context.ArchivePath))
                    {
                        logger?.Info($"Deleting extracted archive after successful install: '{context.ArchivePath}'.");
                        File.Delete(context.ArchivePath);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Failed to delete archive '{context.ArchivePath}': {ex.Message}");
                    return "Installed content but failed to delete the downloaded archive.";
                }
            }

            var stagingRoot = ResolveOperationStagingRoot(context.ExtractedPath);
            if (string.IsNullOrWhiteSpace(stagingRoot))
            {
                return string.Empty;
            }

            try
            {
                TryCleanupStaging(stagingRoot, logger);
                if (Directory.Exists(stagingRoot))
                {
                    return "Installed content but failed to clean the staging directory.";
                }
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to clean staging root '{stagingRoot}': {ex.Message}");
                return "Installed content but failed to clean the staging directory.";
            }

            return string.Empty;
        }

        private static string FinalizeExtractedRomInstallArtifacts(InstallContext context, Services.Logging.LoggingService logger)
        {
            return FinalizeRomInstallArtifacts(context, context?.InstalledExecutablePath ?? context?.ExtractedPath ?? string.Empty, logger);
        }

        private static bool ShouldDeleteArchiveAfterInstall(InstallContext context, string finalExecutablePath)
        {
            if (context == null
                || string.IsNullOrWhiteSpace(context.ArchivePath)
                || !File.Exists(context.ArchivePath))
            {
                return false;
            }

            var archivePolicy = context.PlatformMapping?.RomArchivePolicy ?? string.Empty;
            if (string.Equals(archivePolicy, "Preserve", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(finalExecutablePath))
            {
                return false;
            }

            try
            {
                return !string.Equals(
                    Path.GetFullPath(context.ArchivePath),
                    Path.GetFullPath(finalExecutablePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return !string.Equals(context.ArchivePath, finalExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string EnsureCanonicalRomInstallLayout(
            InstallContext context,
            RomM.Platforms.Abstractions.Models.Install.InstallResult result,
            Services.Logging.LoggingService logger,
            out string executablePath,
            out string installRootPath)
        {
            executablePath = result?.ExecutablePath ?? string.Empty;
            installRootPath = result?.InstallRootPath ?? context?.InstallDirectory ?? string.Empty;

            if (context == null
                || result == null
                || string.IsNullOrWhiteSpace(result.ExecutablePath)
                || string.IsNullOrWhiteSpace(context.InstallDirectory)
                || InstallDestinationService.IsWindowsPlatform(context.Game?.Platform)
                || !GameInstallPathPolicy.ShouldUseGameSubfolder(context.Game?.Platform, context.RommDetails?.PlatformId))
            {
                return string.Empty;
            }

            var canonicalGameDirectory = !string.IsNullOrWhiteSpace(context.DownloadDirectory)
                ? Path.GetFullPath(context.DownloadDirectory)
                : GameInstallPathHelper.ResolveGameDirectory(context.InstallDirectory, context.Game?.Title);
            var canonicalExecutablePath = GameInstallPathHelper.ResolveTargetFilePath(context.InstallDirectory, result.ExecutablePath, context.Game?.Title);
            if (string.IsNullOrWhiteSpace(canonicalGameDirectory) || string.IsNullOrWhiteSpace(canonicalExecutablePath))
            {
                return "Installed content path could not be resolved.";
            }

            executablePath = canonicalExecutablePath;
            installRootPath = canonicalGameDirectory;

            try
            {
                if (!File.Exists(result.ExecutablePath))
                {
                    return $"Installed content missing on disk at '{result.ExecutablePath}'.";
                }

                if (string.Equals(Path.GetFullPath(result.ExecutablePath), canonicalExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                var actualInstallRoot = ResolveActualInstallRoot(result);
                if (CanMoveInstalledDirectory(actualInstallRoot, context.InstallDirectory, canonicalGameDirectory, result.ExecutablePath))
                {
                    MoveDirectoryContents(actualInstallRoot, canonicalGameDirectory, logger);
                    TryDeleteDirectoryIfEmpty(actualInstallRoot, logger);
                    executablePath = RewriteInstalledPath(result.ExecutablePath, actualInstallRoot, canonicalGameDirectory);
                    logger?.Info($"Canonical ROM install root reconciled: '{actualInstallRoot}' -> '{canonicalGameDirectory}'.");
                    return string.Empty;
                }

                Directory.CreateDirectory(canonicalGameDirectory);
                MoveInstalledFile(result.ExecutablePath, canonicalExecutablePath, logger);
                logger?.Info($"Canonical ROM artifact reconciled: '{result.ExecutablePath}' -> '{canonicalExecutablePath}'.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to reconcile installed ROM layout: {ex.Message}");
                return "Installed content could not be reconciled to the canonical game directory.";
            }
        }

        private static string ResolveActualInstallRoot(RomM.Platforms.Abstractions.Models.Install.InstallResult result)
        {
            if (!string.IsNullOrWhiteSpace(result?.InstallRootPath) && Directory.Exists(result.InstallRootPath))
            {
                return Path.GetFullPath(result.InstallRootPath);
            }

            if (!string.IsNullOrWhiteSpace(result?.ExecutablePath))
            {
                return Path.GetDirectoryName(Path.GetFullPath(result.ExecutablePath)) ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool CanMoveInstalledDirectory(string actualInstallRoot, string platformInstallRoot, string canonicalGameDirectory, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(actualInstallRoot)
                || string.IsNullOrWhiteSpace(platformInstallRoot)
                || string.IsNullOrWhiteSpace(canonicalGameDirectory)
                || !Directory.Exists(actualInstallRoot)
                || !GameInstallPathHelper.IsPathUnderDirectory(executablePath, actualInstallRoot))
            {
                return false;
            }

            var normalizedActual = Path.GetFullPath(actualInstallRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPlatform = Path.GetFullPath(platformInstallRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedCanonical = Path.GetFullPath(canonicalGameDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return !string.Equals(normalizedActual, normalizedPlatform, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedActual, normalizedCanonical, StringComparison.OrdinalIgnoreCase);
        }

        private static void MoveInstalledFile(string sourcePath, string destinationPath, Services.Logging.LoggingService logger)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException("Destination directory could not be resolved.");
            }

            Directory.CreateDirectory(destinationDirectory);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            if (string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.Delete(sourcePath);
            }

            var sourceDirectory = Path.GetDirectoryName(sourcePath);
            TryDeleteDirectoryIfEmpty(sourceDirectory, logger);
        }

        private static void MoveDirectoryContents(string sourceDirectory, string destinationDirectory, Services.Logging.LoggingService logger)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var destination = Path.Combine(destinationDirectory, name);
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(destination))
                    {
                        Directory.Delete(destination, recursive: true);
                    }

                    if (string.Equals(Path.GetPathRoot(entry), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.Move(entry, destination);
                    }
                    else
                    {
                        CopyDirectory(entry, destination);
                        Directory.Delete(entry, recursive: true);
                    }
                }
                else
                {
                    MoveInstalledFile(entry, destination, logger);
                }
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
                File.Copy(file, destinationPath, overwrite: true);
            }
        }

        private static string RewriteInstalledPath(string executablePath, string sourceRoot, string destinationRoot)
        {
            if (string.IsNullOrWhiteSpace(executablePath)
                || string.IsNullOrWhiteSpace(sourceRoot)
                || string.IsNullOrWhiteSpace(destinationRoot))
            {
                return executablePath;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, executablePath);
            return Path.Combine(destinationRoot, relativePath);
        }

        private static void TryDeleteDirectoryIfEmpty(string directoryPath, Services.Logging.LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath, recursive: false);
                }
            }
            catch (Exception ex)
            {
                logger?.Debug($"Directory cleanup skipped for '{directoryPath}': {ex.Message}");
            }
        }

        private static string ResolveOperationStagingRoot(string extractedPath)
        {
            try
            {
                var currentDirectoryPath = Directory.Exists(extractedPath)
                    ? extractedPath
                    : Path.GetDirectoryName(extractedPath);
                if (string.IsNullOrWhiteSpace(currentDirectoryPath))
                {
                    return string.Empty;
                }

                var current = new DirectoryInfo(Path.GetFullPath(currentDirectoryPath));
                while (current?.Parent != null)
                {
                    if (string.Equals(current.Parent.Name, ".staging", StringComparison.OrdinalIgnoreCase))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private readonly struct StagingCommitResult
        {
            public bool Success { get; }
            public string Message { get; }
            public string FinalInstallRoot { get; }

            private StagingCommitResult(bool success, string message, string finalInstallRoot)
            {
                Success = success;
                Message = message;
                FinalInstallRoot = finalInstallRoot;
            }

            public static StagingCommitResult FromSuccess(string finalInstallRoot)
            {
                return new StagingCommitResult(true, string.Empty, finalInstallRoot ?? string.Empty);
            }

            public static StagingCommitResult Failed(string message)
            {
                return new StagingCommitResult(false, message ?? "Install finalization failed.", string.Empty);
            }
        }

        internal static string ResolveInstallerKey(
            string platformKey,
            string platformDisplayName,
            string launchBoxPlatformName,
            PlatformInstallerRegistry registry,
            Services.Logging.LoggingService logger)
        {
            if (registry == null)
            {
                return platformKey ?? string.Empty;
            }

            logger?.Info($"Installer resolution candidates: RomMId='{platformKey ?? string.Empty}', RomMName='{platformDisplayName ?? string.Empty}', LaunchBox='{launchBoxPlatformName ?? string.Empty}'.");

            if (!string.IsNullOrWhiteSpace(platformKey)
                && registry.TryGetInstaller(platformKey, out _))
            {
                logger?.Info($"Resolved platform installer by direct key lookup: '{platformKey}'.");
                return platformKey;
            }

            var allInstallers = registry.GetAll().Values
                .Where(installer => installer != null)
                .ToArray();

            var candidates = new[]
            {
                platformDisplayName,
                launchBoxPlatformName
            };

            var normalizedCandidates = candidates
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(ExpandNormalizedPlatformCandidates)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var installer in allInstallers)
            {
                var normalizedDisplay = NormalizePlatformToken(installer.DisplayName);
                var normalizedKey = NormalizePlatformToken(installer.PlatformKey);
                if (normalizedCandidates.Any(candidate => string.Equals(candidate, normalizedDisplay, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate, normalizedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    logger?.Info($"Resolved platform installer by name match: '{installer.DisplayName}' ({installer.PlatformKey}).");
                    return installer.PlatformKey;
                }
            }

            foreach (var installer in allInstallers)
            {
                if (installer is not RomM.Platforms.Abstractions.IPlatformInstallerIdentityMetadata identity
                    || identity.SupportedPlatformAliases == null)
                {
                    continue;
                }

                var normalizedAliases = identity.SupportedPlatformAliases
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(NormalizePlatformToken)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalizedAliases.Length == 0)
                {
                    continue;
                }

                if (normalizedCandidates.Any(candidate => normalizedAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
                {
                    logger?.Info($"Resolved platform installer by alias match: '{installer.DisplayName}' ({installer.PlatformKey}).");
                    return installer.PlatformKey;
                }
            }

            if (!string.IsNullOrWhiteSpace(platformKey))
            {
                foreach (var installer in allInstallers)
                {
                    if (installer is RomM.Platforms.Abstractions.IPlatformInstallerIdentityMetadata identity
                        && identity.SupportedPlatformIds != null
                        && identity.SupportedPlatformIds.Any(id => string.Equals(id?.Trim(), platformKey.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        logger?.Info($"Resolved platform installer by RomM platform id '{platformKey}': '{installer.DisplayName}' ({installer.PlatformKey}).");
                        return installer.PlatformKey;
                    }
                }
            }

            logger?.Warning($"No dedicated platform installer match found. RomMId='{platformKey ?? string.Empty}', RomMName='{platformDisplayName ?? string.Empty}', LaunchBox='{launchBoxPlatformName ?? string.Empty}'. Registered keys=[{string.Join(",", registry.GetAll().Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}].");
            return platformKey ?? string.Empty;
        }

        private static string NormalizePlatformToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return normalized;
        }

        private static IEnumerable<string> ExpandNormalizedPlatformCandidates(string value)
        {
            var normalized = NormalizePlatformToken(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            yield return normalized;

            foreach (var prefix in new[] { "sony", "nintendo", "microsoft" })
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && normalized.Length > prefix.Length)
                {
                    yield return normalized.Substring(prefix.Length);
                }
            }
        }

        internal static bool ShouldWarnOnResolvedKeyDifference(
            string providedKey,
            string resolvedKey,
            PlatformInstallerRegistry registry)
        {
            if (registry == null
                || string.IsNullOrWhiteSpace(providedKey)
                || string.IsNullOrWhiteSpace(resolvedKey)
                || string.Equals(providedKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Warn only when the provided key already maps to a concrete installer.
            // If it does not, this is likely a foreign RomM identifier (for example numeric ids)
            // that was intentionally mapped via aliases/name matching.
            return registry.TryGetInstaller(providedKey, out _);
        }

    }
}
