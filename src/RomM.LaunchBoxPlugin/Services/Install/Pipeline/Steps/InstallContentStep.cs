using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

            if (_platformInstallers != null && context.RommDetails != null
                && _platformInstallers.TryGetInstaller(context.RommDetails.PlatformId ?? string.Empty, out var romInstaller))
            {
                progress?.Report(new InstallProgressEvent(Phase, $"Installing {context.Game.Title}...", 0));
                context.Logger?.Info($"ROM install input: ArchivePath='{context.ArchivePath ?? string.Empty}', ExtractedPath='{context.ExtractedPath ?? string.Empty}', InstallDir='{context.InstallDirectory}'.");

                if (context.PlatformMapping?.ExtractAfterDownload == false
                    && !string.IsNullOrWhiteSpace(context.ArchivePath)
                    && _archiveService.IsSupportedArchive(context.ArchivePath))
                {
                    var archivePolicy = context.PlatformMapping?.RomArchivePolicy ?? string.Empty;
                    if (!string.Equals(archivePolicy, "Preserve", StringComparison.OrdinalIgnoreCase))
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

                var platformContext = new RomM.Platforms.Abstractions.Models.Install.InstallContext
                {
                    GameName = context.Game.Title,
                    InstallDirectory = context.InstallDirectory,
                    ArchivePath = context.ArchivePath,
                    ExtractedPath = context.ExtractedPath,
                    Settings = PlatformInstallSettingsMapper.Map(context.PlatformMapping),
                    RomSettings = PlatformInstallSettingsMapper.MapRomSettings(context.PlatformMapping),
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

                if (!string.IsNullOrWhiteSpace(result.ExecutablePath))
                {
                    context.InstalledExecutablePath = result.ExecutablePath;
                    context.InstallerArguments = result.Arguments == null
                        ? Array.Empty<string>()
                        : result.Arguments.ToArray();
                }

                context.InstallStateSnapshot.WindowsInstallType = result.InstallType?.ToString();
                context.InstallStateSnapshot.InstallRootPath = result.InstallRootPath ?? context.InstallDirectory;
                return InstallResult.Successful();
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

    }
}
