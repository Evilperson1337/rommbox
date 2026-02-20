using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using RomMbox.Models;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Services.Install;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Legacy install workflow that downloads, extracts, and registers RomM games in LaunchBox.
    /// </summary>
    internal sealed class RomMInstallService
    {
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly InstallStateService _installStateService;
        private readonly IRommClient _client;
        private readonly DownloadService _downloadService;

        /// <summary>
        /// Creates a new installer service with required dependencies.
        /// </summary>
        /// <param name="logger">Logging service.</param>
        /// <param name="settingsManager">Settings manager.</param>
        /// <param name="installStateService">Install state service.</param>
        /// <param name="client">RomM API client.</param>
        public RomMInstallService(LoggingService logger, SettingsManager settingsManager, InstallStateService installStateService, IRommClient client)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _installStateService = installStateService;
            _client = client;
            var archiveService = new ArchiveService(logger, settingsManager);
            _downloadService = new DownloadService(logger, client, archiveService, settingsManager);
        }

        /// <summary>
        /// Installs a RomM-sourced game, updating LaunchBox metadata and install state.
        /// </summary>
        /// <param name="game">The LaunchBox game.</param>
        /// <param name="dataManager">LaunchBox data manager.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="downloadProgress">Optional download progress reporter.</param>
        /// <param name="extractionProgress">Optional extraction progress reporter.</param>
        /// <returns>The install result.</returns>
        public RomMInstallResult Install(
            IGame game,
            IDataManager dataManager,
            CancellationToken cancellationToken,
            IProgress<Models.Download.DownloadProgress> downloadProgress = null,
            IProgress<Models.Download.DownloadProgress> extractionProgress = null)
        {
            if (game == null || dataManager == null)
            {
                return RomMInstallResult.Failed("Game or DataManager unavailable.");
            }

            if (!_installStateService.IsRomMSourcedGame(game))
            {
                return RomMInstallResult.Failed("Selected game is not RomM-sourced.");
            }

            RomMbox.Models.Download.DownloadResult downloadResult = null;
            try
            {
                var details = _installStateService.GetRomMDetails(game);
                if (string.IsNullOrWhiteSpace(details.RommRomId))
                {
                    return RomMInstallResult.Failed("RomM ROM id is missing.");
                }

                _logger?.Info($"RomM install requested for '{game.Title}' (RomId={details.RommRomId}).");
                var rom = _client.GetRomDetailsAsync(details.RommRomId, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (rom == null)
                {
                    return RomMInstallResult.Failed("Failed to resolve RomM details.");
                }

                var mapping = new PlatformMappingStore(_logger)
                    .GetPlatformMappingAsync(rom.PlatformId ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                var extractAfterDownload = mapping?.ExtractAfterDownload ?? false;
                var extractionBehavior = mapping?.ExtractionBehavior ?? ExtractionBehavior.Subfolder;
                var installScenario = mapping?.InstallScenario ?? InstallScenario.Basic;
                var targetImportFile = mapping?.TargetImportFile ?? string.Empty;
                var installerSilentArgs = mapping?.InstallerSilentArgs ?? string.Empty;
                var platform = dataManager.GetPlatformByName(game.Platform);
                if (platform == null)
                {
                    return RomMInstallResult.Failed("LaunchBox platform not found.");
                }

                var installDestinationService = new InstallDestinationService(_logger, _settingsManager);
                var installerMode = mapping?.InstallerMode ?? InstallerMode.Manual;
                var installLocation = installDestinationService
                    .ResolveInstallLocationAsync(game, installerMode, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (!installLocation.Success || string.IsNullOrWhiteSpace(installLocation.InstallDirectory))
                {
                    return RomMInstallResult.Failed(installLocation.Message ?? "Install directory unavailable.");
                }

                var downloadDirectory = InstallDestinationService.IsWindowsPlatform(platform.Name)
                    ? installLocation.InstallDirectory
                    : EnsureGameSubfolder(installLocation.InstallDirectory, game.Title);
                _logger?.Info($"Downloading ROM for '{game.Title}' to '{downloadDirectory}'. Scenario={installScenario}, Extract={extractAfterDownload}, Behavior={extractionBehavior}.");

                var serverUrl = _settingsManager.Load().ServerUrl;
                downloadResult = _downloadService
                    .DownloadRomAsync(rom, downloadDirectory, serverUrl, extractionBehavior, extractAfterDownload, cancellationToken, downloadProgress, extractionProgress)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (!downloadResult.Success)
                {
                    return RomMInstallResult.Failed(downloadResult.ErrorMessage ?? "Download failed.");
                }

                var finalPath = !string.IsNullOrWhiteSpace(downloadResult.ExtractedPath)
                    ? downloadResult.ExtractedPath
                    : downloadResult.ArchivePath;
                if (string.IsNullOrWhiteSpace(finalPath))
                {
                    return RomMInstallResult.Failed("Download completed but no output file was produced.");
                }

                if (InstallDestinationService.IsWindowsPlatform(platform.Name))
                {
                    var installSubsystem = new WindowsInstallSubsystem(_logger, new ArchiveService(_logger, _settingsManager));
                    var installResult = installSubsystem
                        .InstallAsync(downloadResult.ArchivePath, downloadResult.ExtractedPath, installLocation.InstallDirectory, mapping, game.Title, cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    if (!installResult.Success)
                    {
                        return RomMInstallResult.Failed(installResult.Message ?? "Windows install failed.");
                    }

                    if (!string.IsNullOrWhiteSpace(installResult.ExecutablePath))
                    {
                        game.ApplicationPath = ToLaunchBoxRelativePath(installResult.ExecutablePath);
                        if (installResult.Arguments?.Count > 0)
                        {
                            game.CommandLine = string.Join(" ", installResult.Arguments);
                        }
                    }

                    game.Installed = true;
                    game.Status = "Installed";

                    var windowsEmulatorId = ResolveEmulatorId(dataManager, game.Platform);
                    if (!string.IsNullOrWhiteSpace(windowsEmulatorId))
                    {
                        game.EmulatorId = windowsEmulatorId;
                    }

                    var windowsInstallRoot = Path.Combine(installLocation.InstallDirectory, NormalizePathSegment(game.Title));
                    if (installResult.InstallType.HasValue)
                    {
                        _installStateService.UpsertIdentityAsync(
                                game.Id,
                                details.RommRomId,
                                details.RommPlatformId,
                                remoteMd5: null,
                                localMd5: null,
                                windowsInstallType: installResult.InstallType.Value.ToString(),
                                cancellationToken)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                    }
                    var windowsState = new InstallState
                    {
                        LaunchBoxGameId = game.Id,
                        RommRomId = details.RommRomId,
                        RommPlatformId = details.RommPlatformId,
                        ServerUrl = details.ServerUrl,
                        WindowsInstallType = installResult.InstallType?.ToString(),
                        InstalledPath = installResult.ExecutablePath ?? finalPath,
                        ArchivePath = downloadResult.ArchivePath,
                        InstallRootPath = windowsInstallRoot,
                        IsInstalled = true,
                        InstalledUtc = DateTimeOffset.UtcNow,
                        LastValidatedUtc = DateTimeOffset.UtcNow
                    };
                    _installStateService.UpsertStateAsync(windowsState, cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    ExecuteWithRetry(() =>
                    {
                        dataManager.Save(true);
                        dataManager.ReloadIfNeeded();
                        dataManager.ForceReload();
                    }, "RomMInstallService Save/Reload");

                    return RomMInstallResult.Successful();
                }

                var installRootPath = !string.IsNullOrWhiteSpace(downloadResult.ExtractedPath)
                    ? downloadResult.ExtractedPath
                    : downloadDirectory;

                if (installScenario == InstallScenario.Enhanced)
                {
                    finalPath = ResolveTargetFile(downloadResult.ExtractedPath, targetImportFile, finalPath);
                    if (string.IsNullOrWhiteSpace(finalPath))
                    {
                        return RomMInstallResult.Failed("Target import file not found in extracted content.");
                    }
                }
                else if (installScenario == InstallScenario.Installer)
                {
                    var installPath = ExecuteInstaller(downloadResult.ExtractedPath, downloadDirectory, targetImportFile, installerSilentArgs);
                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        return RomMInstallResult.Failed("Installer did not produce a valid install path.");
                    }
                    finalPath = installPath;
                }

                game.ApplicationPath = ToLaunchBoxRelativePath(finalPath);
                game.Installed = true;
                game.Status = "Installed";

                var emulatorId = ResolveEmulatorId(dataManager, game.Platform);
                if (!string.IsNullOrWhiteSpace(emulatorId))
                {
                    game.EmulatorId = emulatorId;
                }

                var state = new InstallState
                {
                    LaunchBoxGameId = game.Id,
                    RommRomId = details.RommRomId,
                    RommPlatformId = details.RommPlatformId,
                    ServerUrl = details.ServerUrl,
                    InstalledPath = finalPath,
                    ArchivePath = downloadResult.ArchivePath,
                    InstallRootPath = installRootPath,
                    IsInstalled = true,
                    InstalledUtc = DateTimeOffset.UtcNow,
                    LastValidatedUtc = DateTimeOffset.UtcNow
                };
                _installStateService.UpsertStateAsync(state, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                ExecuteWithRetry(() =>
                {
                    dataManager.Save(true);
                    dataManager.ReloadIfNeeded();
                    dataManager.ForceReload();
                }, "RomMInstallService Save/Reload");

                return RomMInstallResult.Successful();
            }
            catch (Exception ex)
            {
                _logger?.Error("RomM install failed.", ex);
                return RomMInstallResult.Failed(ex.Message);
            }
            finally
            {
                _downloadService.TryCleanupTempRoot(downloadResult?.TempRoot);
            }
        }

        /// <summary>
        /// Ensures a subfolder exists for the specified game title.
        /// </summary>
        /// <param name="baseDirectory">The base directory.</param>
        /// <param name="title">The game title used for the subfolder name.</param>
        /// <returns>The combined folder path.</returns>
        private string EnsureGameSubfolder(string baseDirectory, string title)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var folderName = NormalizePathSegment(title);
            var combined = Path.Combine(baseDirectory, folderName);
            Directory.CreateDirectory(combined);
            return combined;
        }

        /// <summary>
        /// Resolves a target import file inside extracted content.
        /// </summary>
        /// <param name="extractedPath">The extracted content path.</param>
        /// <param name="targetImportFile">The file pattern to locate.</param>
        /// <param name="fallbackPath">Fallback path when no match is found.</param>
        /// <returns>The resolved file path.</returns>
        private string ResolveTargetFile(string extractedPath, string targetImportFile, string fallbackPath)
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
                _logger?.Warning($"Failed to locate target import file '{targetImportFile}'. {ex.Message}");
                return fallbackPath;
            }
        }

        /// <summary>
        /// Executes a silent installer and returns the resulting install path.
        /// </summary>
        /// <param name="extractedPath">The extracted content path.</param>
        /// <param name="installDirectory">The target install directory.</param>
        /// <param name="targetImportFile">Optional target file to locate after install.</param>
        /// <param name="installerSilentArgs">Silent install arguments override.</param>
        /// <returns>The resolved install path or empty on failure.</returns>
        private string ExecuteInstaller(string extractedPath, string installDirectory, string targetImportFile, string installerSilentArgs)
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
            var quotedSetup = QuoteArgument(setupPath);
            var arguments = $"{silentArgs} /DIR={quotedInstall}";

            _logger?.Info($"Launching installer {setupPath} with args: {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    _logger?.Warning("Failed to start installer process.");
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger?.Error($"Installer failed. ExitCode={process.ExitCode}. Output={output}. Error={error}");
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

        /// <summary>
        /// Quotes a command-line argument when needed.
        /// </summary>
        /// <param name="value">The argument value.</param>
        /// <returns>A quoted argument string.</returns>
        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        /// <summary>
        /// Normalizes a string into a safe file-system path segment.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>A normalized path segment.</returns>
        private static string NormalizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .ToCharArray()
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned.Trim();
        }

        /// <summary>
        /// Converts an absolute path into a LaunchBox-relative path when possible.
        /// </summary>
        /// <param name="absolutePath">The absolute file path.</param>
        /// <returns>The LaunchBox-relative path or the original path.</returns>
        private string ToLaunchBoxRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                var root = Paths.PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(root))
                {
                    return absolutePath;
                }

                var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = absolutePath.Substring(normalizedRoot.Length);
                    return relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
            }
            catch
            {
            }

            return absolutePath;
        }

        /// <summary>
        /// Resolves the default emulator ID for the specified platform.
        /// </summary>
        /// <param name="dataManager">LaunchBox data manager.</param>
        /// <param name="platformName">The platform name.</param>
        /// <returns>The emulator ID or empty when not found.</returns>
        private static string ResolveEmulatorId(IDataManager dataManager, string platformName)
        {
            if (dataManager == null || string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            var emulators = dataManager.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true)
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Executes an action with retry and exponential backoff for transient failures.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="operationName">Friendly operation name for logging.</param>
        /// <param name="maxAttempts">Maximum retry attempts.</param>
        private void ExecuteWithRetry(Action action, string operationName, int maxAttempts = 5)
        {
            if (action == null)
            {
                return;
            }

            var attempt = 0;
            var delayMs = 150;
            while (true)
            {
                try
                {
                    attempt++;
                    action();
                    if (attempt > 1)
                    {
                        _logger?.Info($"{operationName} succeeded after {attempt} attempt(s).");
                    }
                    return;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    _logger?.Warning($"{operationName} failed due to IO lock (attempt {attempt} of {maxAttempts}): {ex.Message}");
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    _logger?.Warning($"{operationName} failed (attempt {attempt} of {maxAttempts}): {ex.Message}");
                }

                try
                {
                    Thread.Sleep(delayMs);
                }
                catch
                {
                }
                delayMs = Math.Min(delayMs * 2, 1500);
            }
        }
    }

    /// <summary>
    /// Represents the outcome of an install operation.
    /// </summary>
    internal sealed class RomMInstallResult
    {
        /// <summary>
        /// Gets whether the install succeeded.
        /// </summary>
        public bool Success { get; private set; }
        /// <summary>
        /// Gets the result message.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static RomMInstallResult Successful()
        {
            return new RomMInstallResult { Success = true, Message = "Install completed." };
        }

        /// <summary>
        /// Creates a failed result with the specified message.
        /// </summary>
        /// <param name="message">Failure message.</param>
        public static RomMInstallResult Failed(string message)
        {
            return new RomMInstallResult { Success = false, Message = message ?? "Install failed." };
        }
    }
}
