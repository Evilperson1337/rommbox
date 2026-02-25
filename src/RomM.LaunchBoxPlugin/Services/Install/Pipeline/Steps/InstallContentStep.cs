using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Services.Install;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class InstallContentStep : IInstallStep
    {
        private readonly WindowsInstallSubsystem _windowsSubsystem;

        public InstallContentStep(WindowsInstallSubsystem windowsSubsystem)
        {
            _windowsSubsystem = windowsSubsystem;
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
                var extractionProgress = new Progress<Models.Download.DownloadProgress>(update =>
                {
                    if (update.TotalBytes.HasValue && update.TotalBytes.Value > 0)
                    {
                        var percent = Math.Clamp((update.BytesReceived / (double)update.TotalBytes.Value) * 100d, 0, 100);
                        progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extracting...", percent));
                    }
                    else
                    {
                        progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extracting..."));
                    }
                });

                var stagingRoot = Path.Combine(context.InstallDirectory, ".staging", context.OperationId ?? Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingRoot);
                var result = await _windowsSubsystem
                    .InstallAsync(context.ArchivePath, context.ExtractedPath, stagingRoot, context.PlatformMapping, context.Game.Title, cancellationToken, extractionProgress, installProgress, context.InstallDirectory, preferFinalInstallDirForInstaller: true)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    TryCleanupStaging(stagingRoot, context.Logger);
                    return InstallResult.Failed(Phase, result.Message ?? "Windows install failed.");
                }

                var usedStaging = result.InstallType != InstallType.Installer;
                string finalInstallRoot = context.InstallDirectory;
                string stagingRewriteRoot = stagingRoot;
                if (usedStaging)
                {
                    var safeGameName = WindowsInstallSubsystem.NormalizeGameFolderNameInternal(context.Game.Title, "Game");
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
                var updatedResult = new WindowsInstallResultProxy(result, context.InstalledExecutablePath);
                context.InstallStateSnapshot.InstallRootPath = ResolveInstallRootPath(updatedResult, context.InstallDirectory);
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

        private static string ResolveInstallRootPath(WindowsInstallResult result, string installDirectory)
        {
            if (!string.IsNullOrWhiteSpace(result?.ExecutablePath))
            {
                var directory = Path.GetDirectoryName(result.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    if (!string.IsNullOrWhiteSpace(installDirectory))
                    {
                        var normalizedRoot = installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        if (directory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            var remainder = directory.Substring(normalizedRoot.Length);
                            var segments = remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            var firstSegment = segments.FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
                            if (!string.IsNullOrWhiteSpace(firstSegment))
                            {
                                return Path.Combine(installDirectory, firstSegment);
                            }
                        }
                    }

                    return directory;
                }
            }

            return installDirectory;
        }

        private static string FormatBytes(long bytes)
        {
            const double scale = 1024d;
            var abs = Math.Abs(bytes);
            if (abs >= scale * scale * scale)
            {
                return (bytes / (scale * scale * scale)).ToString("0.0") + " GB";
            }
            if (abs >= scale * scale)
            {
                return (bytes / (scale * scale)).ToString("0.0") + " MB";
            }
            if (abs >= scale)
            {
                return (bytes / scale).ToString("0.0") + " KB";
            }
            return bytes + " B";
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

        private static (bool Success, string FinalInstallRoot, string Message) TryCommitStaging(string stagingRoot, string installRoot, string gameName, Services.Logging.LoggingService logger)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stagingRoot) || !Directory.Exists(stagingRoot))
                {
                    return (false, string.Empty, "Staging root missing after install.");
                }

                var safeGameName = WindowsInstallSubsystem.NormalizeGameFolderNameInternal(gameName, "Game");
                var stagedGameRoot = Path.Combine(stagingRoot, safeGameName);
                var finalGameRoot = Path.Combine(installRoot, safeGameName);
                var sourceRoot = Directory.Exists(stagedGameRoot) ? stagedGameRoot : stagingRoot;

                var previousRoot = finalGameRoot + ".previous";
                if (Directory.Exists(previousRoot))
                {
                    Directory.Delete(previousRoot, recursive: true);
                }

                if (Directory.Exists(finalGameRoot))
                {
                    Directory.Move(finalGameRoot, previousRoot);
                }

                Directory.CreateDirectory(installRoot);
                Directory.Move(sourceRoot, finalGameRoot);

                if (Directory.Exists(previousRoot))
                {
                    Directory.Delete(previousRoot, recursive: true);
                }

                TryCleanupStaging(stagingRoot, logger);
                return (true, finalGameRoot, string.Empty);
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to commit staging install: {ex.Message}");
                return (false, string.Empty, $"Failed to commit staging install: {ex.Message}");
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

        private static string RewriteStagedPath(string path, string stagingRoot, string finalRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(stagingRoot) || string.IsNullOrWhiteSpace(finalRoot))
            {
                return path;
            }

            try
            {
                var normalizedStaging = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var normalizedPath = Path.GetFullPath(path);
                if (normalizedPath.StartsWith(normalizedStaging, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = normalizedPath.Substring(normalizedStaging.Length);
                    return Path.Combine(finalRoot, suffix);
                }
            }
            catch
            {
            }

            return path;
        }


        private sealed class WindowsInstallResultProxy
        {
            public WindowsInstallResultProxy(WindowsInstallResult original, string executablePath)
            {
                ExecutablePath = executablePath ?? original?.ExecutablePath;
            }

            public string ExecutablePath { get; }
        }

        private static string ResolveInstallRootPath(WindowsInstallResultProxy result, string installDirectory)
        {
            if (!string.IsNullOrWhiteSpace(result?.ExecutablePath))
            {
                var directory = Path.GetDirectoryName(result.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    if (!string.IsNullOrWhiteSpace(installDirectory))
                    {
                        var normalizedRoot = installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        if (directory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            var remainder = directory.Substring(normalizedRoot.Length);
                            var segments = remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            var firstSegment = segments.FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
                            if (!string.IsNullOrWhiteSpace(firstSegment))
                            {
                                return Path.Combine(installDirectory, firstSegment);
                            }
                        }
                    }

                    return directory;
                }
            }

            return installDirectory;
        }
    }
}
