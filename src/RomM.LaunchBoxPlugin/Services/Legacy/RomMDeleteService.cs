using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models;
using RomMbox.Services.Logging;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Legacy delete/uninstall workflow for RomM-sourced games. This focuses on
    /// local content cleanup while leaving the LaunchBox game entry in place.
    /// </summary>
    internal sealed class RomMDeleteService
    {
        private readonly LoggingService _logger;
        private readonly InstallStateService _installStateService;
        private readonly StubApplicationPathService _stubService;

        /// <summary>
        /// Creates the delete/uninstall service.
        /// </summary>
        /// <param name="logger">Logger for operational diagnostics.</param>
        /// <param name="installStateService">Install state lookup and persistence.</param>
        public RomMDeleteService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
            _stubService = new StubApplicationPathService(logger, installStateService);
        }

        /// <summary>
        /// Removes local content for a RomM-sourced game and clears install metadata.
        /// This keeps the LaunchBox entry so the user can still access it via RomM.
        /// </summary>
        /// <param name="game">LaunchBox game to uninstall.</param>
        /// <param name="dataManager">LaunchBox data manager instance.</param>
        /// <param name="cancellationToken">Cancellation token for async state calls.</param>
        /// <returns>Result that indicates success and whether a stub was created.</returns>
        public RomMDeleteResult DeleteOrUninstall(IGame game, IDataManager dataManager, CancellationToken cancellationToken)
        {
            if (game == null || dataManager == null)
            {
                return RomMDeleteResult.Failed("Game or DataManager unavailable.");
            }

            if (!_installStateService.IsRomMSourcedGame(game))
            {
                return RomMDeleteResult.Failed("Selected game is not RomM-sourced.");
            }

            try
            {
                var state = _installStateService.GetStateAsync(game.Id, cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                var wasInstalled = state != null && state.IsInstalled;

                // Clean the on-disk content first, then update LaunchBox metadata.
                var (removedFiles, cleanupMessage) = CleanupLocalContent(game, state);
                _logger?.Info($"RomM delete/uninstall cleanup for '{game.Title}': RemovedFiles={removedFiles}, Message={cleanupMessage ?? "none"}.");

                if (wasInstalled)
                {
                    game.ApplicationPath = string.Empty;
                    game.Installed = false;
                    game.Status = "Available Remotely";
                    // Optional: create a stub app path to keep the game visible.
                    var stubCreated = _stubService.TryCreateStubForGame(dataManager, game);
                    if (state != null)
                    {
                        _installStateService.UpsertIdentityAsync(
                                game.Id,
                                state.RommRomId,
                                state.RommPlatformId,
                                remoteMd5: state.RemoteMd5,
                                localMd5: state.LocalMd5,
                                windowsInstallType: state.WindowsInstallType,
                                cancellationToken)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                    }
                    QueueLaunchBoxSaveAndCleanup(dataManager, game.Id, cancellationToken);
                    return RomMDeleteResult.UninstallResult(stubCreated);
                }

                _logger?.Warning($"RomM uninstall requested for a non-installed game '{game.Title}'. Skipping removal from LaunchBox.");
                game.ApplicationPath = string.Empty;
                game.Installed = false;
                game.Status = "Available Remotely";
                if (state != null)
                {
                    _installStateService.UpsertIdentityAsync(
                            game.Id,
                            state.RommRomId,
                            state.RommPlatformId,
                            remoteMd5: state.RemoteMd5,
                            localMd5: state.LocalMd5,
                            windowsInstallType: state.WindowsInstallType,
                            cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
                QueueLaunchBoxSaveAndCleanup(dataManager, game.Id, cancellationToken);
                return RomMDeleteResult.UninstallResult(stubCreated: false);
            }
            catch (Exception ex)
            {
                _logger?.Error("RomM delete/uninstall failed.", ex);
                return RomMDeleteResult.Failed(ex.Message);
            }
        }

        private void QueueLaunchBoxSaveAndCleanup(IDataManager dataManager, string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (dataManager == null || string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    dataManager.Save(true);
                    _installStateService.DeleteStateAsync(launchBoxGameId, cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    RefreshLaunchBoxData(dataManager);
                    _logger?.Info("LaunchBox data saved and install state cleaned up after uninstall.");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"LaunchBox save/cleanup failed after uninstall: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Deletes local files/directories tied to a RomM install, with safeguards
        /// to prevent deleting paths that do not clearly belong to the game.
        /// </summary>
        private (int RemovedCount, string Message) CleanupLocalContent(IGame game, InstallState state)
        {
            var removed = 0;
            var messages = new System.Collections.Generic.List<string>();

            _logger?.Info($"Uninstall cleanup start for '{game?.Title}': InstallRootPath='{state?.InstallRootPath ?? "<null>"}', InstalledPath='{state?.InstalledPath ?? "<null>"}', ArchivePath='{state?.ArchivePath ?? "<null>"}', ApplicationPath='{game?.ApplicationPath ?? "<null>"}'.");

            var installRoot = ResolveInstallRoot(state, game, messages);
            var identity = _installStateService.GetIdentity(game);
            var installType = identity.WindowsInstallType;
            _logger?.Info($"Uninstall cleanup using install root '{installRoot ?? "<null>"}'. InstallType='{installType ?? "<none>"}'.");

            if (string.Equals(installType, "Installer", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(installRoot))
                {
                    messages.Add("Install root missing; cannot locate Inno uninstaller.");
                }
                else
                {
                    var ran = TryRunInnoUninstaller(installRoot, messages);
                    if (!ran)
                    {
                        messages.Add("Inno uninstaller not found or failed; falling back to delete.");
                    }

                    removed += EnsureDirectoryRemoved(installRoot, messages);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(installRoot))
                {
                    messages.Add("Install root missing; portable uninstall skipped.");
                }
                else
                {
                    removed += TryDeletePath(installRoot, messages);
                }
            }

            _logger?.Info("Uninstall cleanup proceeding with fallback deletes for installed/archive/app paths.");
            removed += TryDeletePath(state?.InstalledPath, messages);
            removed += TryDeletePath(state?.ArchivePath, messages);

            var appPath = game?.ApplicationPath;
            if (!string.IsNullOrWhiteSpace(appPath))
            {
                removed += TryDeletePath(appPath, messages);
            }

            var emptyFolders = messages.Count == 0 ? null : string.Join("; ", messages);
            _logger?.Info($"Uninstall cleanup completed for '{game?.Title}': RemovedFiles={removed}, Message='{emptyFolders ?? "none"}'.");
            return (removed, emptyFolders);
        }

        /// <summary>
        /// Attempts to delete a file or directory, returning how many targets were removed.
        /// </summary>
        private int TryDeletePath(string path, System.Collections.Generic.List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            try
            {
                if (File.Exists(path))
                {
                    _logger?.Info($"Deleting file '{path}'.");
                    File.Delete(path);
                    return 1;
                }

                if (Directory.Exists(path))
                {
                    _logger?.Info($"Deleting directory '{path}'.");
                    Directory.Delete(path, true);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to delete '{path}': {ex.Message}");
                messages?.Add($"Failed to delete '{path}': {ex.Message}");
            }

            return 0;
        }

        private void TryDeleteEmptyParent(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Resolves the install root to remove for uninstall operations.
        /// </summary>
        private string ResolveInstallRoot(InstallState state, IGame game, System.Collections.Generic.List<string> messages)
        {
            var root = state?.InstallRootPath;
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            var installedPath = state?.InstalledPath;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                installedPath = game?.ApplicationPath;
            }

            if (string.IsNullOrWhiteSpace(installedPath))
            {
                messages?.Add("Install root could not be resolved from install state or application path.");
                return string.Empty;
            }

            if (Directory.Exists(installedPath))
            {
                return installedPath;
            }

            if (File.Exists(installedPath))
            {
                return Path.GetDirectoryName(installedPath) ?? string.Empty;
            }

            messages?.Add($"Install root not found on disk for '{installedPath}'.");
            return string.Empty;
        }

        /// <summary>
        /// Attempts to run an Inno Setup uninstaller if one exists in the install root.
        /// </summary>
        private bool TryRunInnoUninstaller(string installRoot, System.Collections.Generic.List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                return false;
            }

            var uninstaller = FindInnoUninstaller(installRoot);
            if (string.IsNullOrWhiteSpace(uninstaller))
            {
                _logger?.Warning($"Inno uninstaller not found in '{installRoot}'.");
                return false;
            }

            try
            {
                var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /FORCECLOSEAPPLICATIONS /LOG";
                _logger?.Info($"Running Inno uninstaller: {uninstaller} {args}");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uninstaller,
                    Arguments = args,
                    UseShellExecute = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to run Inno uninstaller '{uninstaller}': {ex.Message}");
                messages?.Add($"Failed to run Inno uninstaller '{uninstaller}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds an Inno Setup uninstaller (unins*.exe) in or under the install root.
        /// </summary>
        private static string FindInnoUninstaller(string installRoot)
        {
            try
            {
                var topLevel = Directory.EnumerateFiles(installRoot, "unins*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(topLevel))
                {
                    return topLevel;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ensures an install directory is removed after an installer-based uninstall.
        /// </summary>
        private int EnsureDirectoryRemoved(string installRoot, System.Collections.Generic.List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return 0;
            }

            if (!Directory.Exists(installRoot))
            {
                return 0;
            }

            _logger?.Info($"Installer uninstall left directory '{installRoot}'. Removing now.");
            return TryDeletePath(installRoot, messages);
        }

        /// <summary>
        /// Executes an action with backoff retries, intended for file IO operations.
        /// </summary>
        private void ExecuteWithRetry(Action action, string operationName, int maxAttempts = 5)
        {
            if (action == null)
            {
                return;
            }

            var delayMs = 50;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    _logger?.Warning($"{operationName} failed on attempt {attempt}. Retrying. {ex.Message}");
                    Thread.Sleep(delayMs);
                    delayMs = Math.Min(delayMs * 2, 1000);
                }
            }

            action();
        }

        private void RefreshLaunchBoxData(IDataManager dataManager)
        {
            if (dataManager == null)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    dataManager.BackgroundReloadSave(() => { });
                    dataManager.ReloadIfNeeded();
                    dataManager.ForceReload();
                    _logger?.Info("LaunchBox data refresh completed after uninstall.");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"LaunchBox data refresh failed after uninstall: {ex.Message}");
                }
            });
        }
    }

    internal sealed class RomMDeleteResult
    {
        public bool Success { get; private set; }
        public bool RemovedFromLaunchBox { get; private set; }
        public bool Uninstalled { get; private set; }
        public bool StubCreated { get; private set; }
        public string Message { get; private set; }

        /// <summary>
        /// Result used when a game is removed from the LaunchBox library entirely.
        /// </summary>
        public static RomMDeleteResult Removed()
        {
            return new RomMDeleteResult
            {
                Success = true,
                RemovedFromLaunchBox = true,
                Uninstalled = false,
                StubCreated = false,
                Message = "Game removed from LaunchBox."
            };
        }

        /// <summary>
        /// Result used when local content is removed and the entry is left in LaunchBox.
        /// </summary>
        public static RomMDeleteResult UninstallResult(bool stubCreated)
        {
            return new RomMDeleteResult
            {
                Success = true,
                RemovedFromLaunchBox = false,
                Uninstalled = true,
                StubCreated = stubCreated,
                Message = stubCreated
                    ? "Local content removed. Stub created for local launch."
                    : "Local content removed. ApplicationPath cleared for RomM interface."
            };
        }

        /// <summary>
        /// Result used for a failed delete/uninstall operation.
        /// </summary>
        public static RomMDeleteResult Failed(string message)
        {
            return new RomMDeleteResult
            {
                Success = false,
                RemovedFromLaunchBox = false,
                Uninstalled = false,
                StubCreated = false,
                Message = message ?? "Delete/Uninstall failed."
            };
        }
    }
}
