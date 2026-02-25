using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.GameActions;
using RomMbox.UI.ViewModels;
using RomMbox.UI.Views;
using RomMbox.Services.Settings;
using RomMbox.Services.Install;
using RomMbox.Services.Install.Pipeline;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.Paths;
using RomMbox.UI;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.GameMenu
{
    /// <summary>
    /// Adds a RomM submenu to the LaunchBox game context menu for RomM-sourced games.
    /// Provides install/uninstall and "Play on RomM" actions.
    /// </summary>
    [Export(typeof(IGameMultiMenuItemPlugin))]
    public sealed class RommMultiMenuItem : IGameMultiMenuItemPlugin
    {
        /// <summary>
        /// Cache duration for save availability checks to avoid repeated API calls.
        /// </summary>
        private static readonly TimeSpan SaveAvailabilityCacheTtl = TimeSpan.FromMinutes(10);
        /// <summary>
        /// In-memory cache for whether a RomM game has cloud saves available.
        /// </summary>
        private static readonly ConcurrentDictionary<string, SaveAvailabilityEntry> SaveAvailabilityCache = new();
        /// <summary>
        /// Tracks in-flight save availability requests to prevent duplicate calls.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> SaveAvailabilityRequests = new();
        private static readonly ConcurrentDictionary<string, Image> BadgeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Image> PluginAssetCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Lazy<Image> RommBadgeCache = new(ResolveRommBadgeInternal);

        /// <summary>
        /// Indicates whether multi-select is supported (not in v1).
        /// </summary>
        public bool SupportsMultipleGames => false;

        /// <summary>
        /// Caption displayed for the RomM submenu entry.
        /// </summary>
        public string Caption => "RomM";

        /// <summary>
        /// Icon shown for the submenu, sourced from plugin assets when available.
        /// </summary>
        public System.Drawing.Image IconImage => ResolveRommBadge();

        /// <summary>
        /// Display the RomM submenu in LaunchBox.
        /// </summary>
        public bool ShowInLaunchBox => true;

        /// <summary>
        /// Big Box is not enabled for this menu in v1.
        /// </summary>
        public bool ShowInBigBox => false;

        /// <summary>
        /// Determines whether the RomM menu should appear for the selected game.
        /// </summary>
        public bool GetIsValidForGame(IGame selectedGame)
        {
            if (selectedGame == null)
            {
                return false;
            }

            var service = PluginEntry.InstallStateService;
            return service != null && service.IsRomMSourcedGame(selectedGame);
        }

        public bool GetIsValidForGames(IGame[] selectedGames) => false;

        /// <summary>
        /// Builds the RomM submenu and child actions for the selected game.
        /// </summary>
        public IEnumerable<IGameMenuItem> GetMenuItems(IGame[] selectedGames)
        {
            var stopwatch = Stopwatch.StartNew();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var syncContext = SynchronizationContext.Current?.GetType().Name ?? "<null>";
            var game = selectedGames?.FirstOrDefault();
            var menuBuilt = false;
            var childCount = 0;
            try
            {
                PluginEntry.Logger?.Debug($"RomM menu build start. Game='{game?.Title ?? "<null>"}', ThreadId={threadId}, SyncContext={syncContext}.");
                if (game == null)
                {
                    PluginEntry.Logger?.Debug($"RomM menu requested with null game selection. ThreadId={threadId}, SyncContext={syncContext}.");
                    return Array.Empty<IGameMenuItem>();
                }

                var service = PluginEntry.InstallStateService;
                if (service == null || !service.IsRomMSourcedGame(game))
                {
                    PluginEntry.Logger?.Debug($"RomM menu hidden. ServiceAvailable={service != null}, IsRomMSourced={service?.IsRomMSourcedGame(game) ?? false}, Game='{game?.Title}', ThreadId={threadId}, SyncContext={syncContext}.");
                    return Array.Empty<IGameMenuItem>();
                }

                // Check install state using application path resolution.
                var isInstalled = HasValidApplicationPath(game, out var resolvedPath);
                PluginEntry.Logger?.Debug($"RomM menu for '{game?.Title}': InstalledFlag={game?.Installed == true}, ApplicationPath='{game?.ApplicationPath}', ResolvedPath='{resolvedPath}', IsInstalled={isInstalled}, ThreadId={threadId}, SyncContext={syncContext}.");
                var children = new List<IGameMenuItem>();
                if (isInstalled)
                {
                    children.Add(new RommGameMenuItem("Uninstall Game", true, ResolveBadge("Not Installed.png"), () => UninstallGame(game)));
                }
                else
                {
                    children.Add(new RommGameMenuItem("Install Game", true, ResolveBadge("Installed.png"), () => InstallGame(game)));
                    children.Add(new RommGameMenuItem("Link to Local Game", true, ResolveBadge("Installed.png"), () => LinkLocalGame(game)));
                }

            if (CanPlayOnRomMFast(game))
            {
                children.Add(new RommGameMenuItem("Play on RomM", true, ResolvePluginAssetImage("gaming.png"), () => PlayOnRomM(game)));
            }

                children.Add(new RommGameMenuItem("View on RomM", true, ResolvePluginAssetImage("romm.png"), () => ViewOnRomM(game)));
                children.Add(new RommGameMenuItem("Properties", true, ResolveBadge("Not Installed.png"), () => OpenProperties(game)));
                children.Add(new RommGameMenuItem("Open RomM", true, ResolvePluginAssetImage("romm.png"), OpenRommServer));

                // TODO: Future deployment - re-enable save import/upload menu items when save management is implemented.

                childCount = children.Count;
                menuBuilt = true;
                return new List<IGameMenuItem>
                {
                    new RommGameMenuItem("RomM", true, ResolveRommBadge(), children)
                };
            }
            finally
            {
                stopwatch.Stop();
                PluginEntry.Logger?.Debug($"RomM menu build end. DurationMs={stopwatch.ElapsedMilliseconds}, MenuBuilt={menuBuilt}, ChildCount={childCount}, Game='{game?.Title ?? "<null>"}'.");
                LogMenuTiming(stopwatch, game);
            }
        }

        private static void LogMenuTiming(Stopwatch stopwatch, IGame game)
        {
            if (stopwatch == null)
            {
                return;
            }
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            if (elapsedMs > 100)
            {
                PluginEntry.Logger?.Warning($"RomM menu build slow. DurationMs={elapsedMs}, Game='{game?.Title ?? "<null>"}'.");
            }
        }

        /// <summary>
        /// Installs the selected game locally by downloading from RomM.
        /// UI status is shown in an install progress dialog.
        /// </summary>
        private static void InstallGame(IGame game)
        {
            PluginEntry.EnsureInitialized();
            PluginEntry.Logger?.Info($"RomM Install selected for '{game?.Title}'. ApplicationPath='{game?.ApplicationPath}', Installed={game?.Installed == true}.");
            
            // Show progress window on the UI thread
            var progressWindow = new InstallProgressWindow
            {
                Title = "RomM Install - " + game?.Title
            };
            var owner = System.Windows.Application.Current?.Windows
                ?.OfType<MainWindow>()
                .FirstOrDefault();
            if (owner != null && owner.IsVisible)
            {
                progressWindow.Owner = owner;
            }
            PluginEntry.Logger?.Debug($"RomM Install progress window owner set. OwnerVisible={owner?.IsVisible == true}.");
            var viewModel = new InstallProgressViewModel
            {
                HeaderText = "Installing Game",
                StatusText = "Preparing download...",
                ProgressValue = 0,
                IsIndeterminate = true
            };
            progressWindow.DataContext = viewModel;
            
            // Show the window non-modally first
            progressWindow.Show();

            // Install operations can be long (large downloads/extractions). Avoid hard-closing the UI window.
            
            Task.Run(async () =>
            {
                PluginEntry.Logger?.Info($"RomM Install background task started for '{game?.Title}'.");
                try
                {
                    var logger = PluginEntry.Logger;
                    var dataManager = PluginHelper.DataManager;
                    var installStateService = PluginEntry.InstallStateService;
                    if (dataManager == null || installStateService == null)
                    {
                        logger?.Warning($"RomM Install aborted: services unavailable. DataManager={dataManager != null}, InstallStateService={installStateService != null}.");
                        logger?.Warning("RomM Install aborted: services unavailable.");
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            viewModel.StatusText = "Install aborted: services unavailable.";
                            System.Threading.Thread.Sleep(1500);
                            progressWindow.Close();
                        });
                        return;
                    }

                    var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(logger);
                    var client = new RommClient(logger, settingsManager);
                    var destinationService = new InstallDestinationService(logger, settingsManager);
                    var mappingStore = new PlatformMappingStore(logger);
                    var archiveService = new ArchiveService(logger, settingsManager);
                    var downloadService = new DownloadService(logger, client, archiveService, settingsManager);
                    var windowsSubsystem = new WindowsInstallSubsystem(logger, archiveService);

                    var steps = new List<IInstallStep>
                    {
                        new ResolveMetadataStep(client),
                        new ResolveDestinationStep(destinationService, mappingStore),
                        new DownloadStep(downloadService),
                        new InstallContentStep(windowsSubsystem),
                        new PostProcessStep(),
                        new PersistStateStep()
                    };

                    var coordinator = new InstallCoordinator(logger, settingsManager, installStateService, steps);
                    var progress = new Progress<InstallProgressEvent>(update =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            viewModel.HeaderText = update.Phase switch
                            {
                                InstallPhase.Downloading => "Downloading Game",
                                InstallPhase.Extracting => "Extracting Game",
                                InstallPhase.Installing => "Installing Game",
                                _ => "Preparing Install"
                            };
                            viewModel.StatusText = update.Message;
                            if (update.Percent.HasValue)
                            {
                                viewModel.ProgressValue = Math.Round(update.Percent.Value, 0, MidpointRounding.AwayFromZero);
                                viewModel.IsIndeterminate = false;
                            }
                            else
                            {
                                viewModel.IsIndeterminate = true;
                            }
                        });
                    });

                    var result = await coordinator
                        .RunAsync(new InstallRequest(game, dataManager), progress, CancellationToken.None)
                        .ConfigureAwait(false);
                    logger?.Info($"RomM Install result for '{game?.Title}': Success={result.Success}, Message='{result.Message}'.");

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (result.Success)
                        {
                            viewModel.HeaderText = "Installing Game";
                            viewModel.StatusText = "Install completed successfully!";
                            viewModel.ProgressValue = 100;
                            viewModel.IsIndeterminate = false;
                            logger?.Info($"RomM Install completed for '{game?.Title}'. {result.Message}");
                        }
                        else
                        {
                            viewModel.HeaderText = "Installing Game";
                            viewModel.StatusText = "Install failed: " + result.Message;
                            viewModel.ProgressValue = 0;
                            viewModel.IsIndeterminate = false;
                            logger?.Warning($"RomM Install failed for '{game?.Title}'. {result.Message}");
                        }

                        System.Threading.Thread.Sleep(1500);
                        progressWindow.Close();
                    });
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("RomM Install menu failed.", ex);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        viewModel.StatusText = "Install failed with error.";
                        System.Threading.Thread.Sleep(1500);
                        progressWindow.Close();
                    });
                }
            });
        }

        private static void LinkLocalGame(IGame game)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(async () =>
            {
                try
                {
                    var logger = PluginEntry.Logger;
                    var dataManager = PluginHelper.DataManager;
                    var installStateService = PluginEntry.InstallStateService;
                    if (dataManager == null || installStateService == null || game == null)
                    {
                        logger?.Warning("Link Local Game aborted: services unavailable.");
                        return;
                    }

                    if (HasValidApplicationPath(game, out var resolvedPath))
                    {
                        logger?.Info($"Link Local Game resolved existing path for '{game?.Title}': '{resolvedPath}'.");
                        var updated = ApplyLinkedPath(game, resolvedPath, installStateService, dataManager, logger);
                        if (!updated)
                        {
                            ShowInfoDialog("RomM", "Unable to link game - the existing path could not be saved.");
                        }
                        return;
                    }

                    string selectedPath = null;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new OpenFileDialog
                        {
                            Title = $"Link Local Game - {game?.Title}",
                            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                            CheckFileExists = true,
                            Multiselect = false
                        };

                        var result = dialog.ShowDialog();
                        if (result == true)
                        {
                            selectedPath = dialog.FileName;
                        }
                    });

                    if (string.IsNullOrWhiteSpace(selectedPath))
                    {
                        logger?.Info($"Link Local Game cancelled for '{game?.Title}'.");
                        return;
                    }

                    if (!File.Exists(selectedPath))
                    {
                        logger?.Warning($"Link Local Game selected invalid path for '{game?.Title}': '{selectedPath}'.");
                        ShowInfoDialog("RomM", "Selected file does not exist.");
                        return;
                    }

                    var applied = ApplyLinkedPath(game, selectedPath, installStateService, dataManager, logger);
                    if (!applied)
                    {
                        ShowInfoDialog("RomM", "Unable to link the selected game executable.");
                    }
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Link Local Game menu failed.", ex);
                    ShowInfoDialog("RomM", "Failed to link local game.");
                }
            });
        }

        private static bool ApplyLinkedPath(
            IGame game,
            string absolutePath,
            InstallStateService installStateService,
            IDataManager dataManager,
            LoggingService logger)
        {
            if (game == null || string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            var launchBoxPath = ToLaunchBoxRelativePath(absolutePath);
            logger?.Info($"Link Local Game applying for '{game?.Title}': Absolute='{absolutePath}', LaunchBox='{launchBoxPath}'.");
            game.ApplicationPath = launchBoxPath;
            game.Installed = true;
            game.Status = "Installed";
            EnsureEmulatorId(dataManager, game);

            dataManager.Save(false);

            try
            {
                installStateService.UpdateInstalledPathAsync(game.Id, absolutePath, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to persist installed path for '{game?.Title}'. {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Formats byte counts into human-readable text for progress messages.
        /// </summary>
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

        /// <summary>
        /// Removes local content and resets the LaunchBox install state for a RomM game.
        /// UI status is shown in an uninstall progress dialog.
        /// </summary>
        private static void UninstallGame(IGame game)
        {
            PluginEntry.EnsureInitialized();
            PluginEntry.Logger?.Info($"RomM Uninstall selected for '{game?.Title}'. ApplicationPath='{game?.ApplicationPath}', Installed={game?.Installed == true}.");

            var progressWindow = new UninstallProgressWindow
            {
                Title = "RomM Uninstall - " + game?.Title
            };
            var owner = System.Windows.Application.Current?.Windows
                ?.OfType<MainWindow>()
                .FirstOrDefault();
            if (owner != null && owner.IsVisible)
            {
                progressWindow.Owner = owner;
            }
            PluginEntry.Logger?.Debug($"RomM Uninstall progress window owner set. OwnerVisible={owner?.IsVisible == true}.");
            var viewModel = new UninstallProgressViewModel
            {
                HeaderText = "Uninstalling Game",
                StatusText = "Preparing uninstall...",
                ProgressValue = 0,
                IsIndeterminate = true
            };
            progressWindow.DataContext = viewModel;

            progressWindow.Show();
            ScheduleProgressWindowHardTimeout(progressWindow, 120000, "Uninstall");

            Task.Run(async () =>
            {
                try
                {
                    var logger = PluginEntry.Logger;
                    var dataManager = PluginHelper.DataManager;
                    var installStateService = PluginEntry.InstallStateService;
                    if (dataManager == null || installStateService == null)
                    {
                        logger?.Warning($"RomM Uninstall aborted: services unavailable. DataManager={dataManager != null}, InstallStateService={installStateService != null}.");
                        logger?.Warning("RomM Uninstall aborted: services unavailable.");
                        var dispatcher = progressWindow.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                viewModel.StatusText = "Uninstall aborted: services unavailable.";
                            });
                            await Task.Delay(1500).ConfigureAwait(false);
                            await dispatcher.InvokeAsync(() => progressWindow.Close());
                        }
                        return;
                    }

                    var uninstallService = new RomMUninstallService(logger, installStateService);
                    var progress = new Progress<RomMbox.Models.Install.UninstallProgress>(update =>
                    {
                        var dispatcher = System.Windows.Application.Current.Dispatcher;
                        if (dispatcher == null || dispatcher.HasShutdownStarted)
                        {
                            return;
                        }
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            viewModel.HeaderText = update.Stage;
                            viewModel.StatusText = update.Message;
                            viewModel.ProgressValue = update.Percent;
                            viewModel.IsIndeterminate = update.IsIndeterminate;
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    });

                    logger?.Info($"RomM Uninstall calling DeleteOrUninstall for '{game?.Title}'.");
                    var result = await uninstallService.UninstallAsync(game, dataManager, CancellationToken.None, progress).ConfigureAwait(false);
                    logger?.Info($"RomM Uninstall result for '{game?.Title}': Success={result.Success}, Message='{result.Message}'.");

                    var resultDispatcher = progressWindow.Dispatcher;
                    if (resultDispatcher != null && !resultDispatcher.HasShutdownStarted)
                    {
                        await resultDispatcher.InvokeAsync(() =>
                        {
                            if (result.Success)
                            {
                                viewModel.HeaderText = "Uninstalling Game";
                                viewModel.StatusText = "Uninstall completed successfully!";
                                viewModel.ProgressValue = 100;
                                viewModel.IsIndeterminate = false;
                                logger?.Info($"RomM Uninstall completed for '{game?.Title}'. {result.Message}");
                            }
                            else
                            {
                                viewModel.HeaderText = "Uninstalling Game";
                                viewModel.StatusText = "Uninstall failed: " + result.Message;
                                viewModel.ProgressValue = 0;
                                viewModel.IsIndeterminate = false;
                                logger?.Warning($"RomM Uninstall failed for '{game?.Title}'. {result.Message}");
                            }
                        });
                        await Task.Delay(1500).ConfigureAwait(false);
                        await resultDispatcher.InvokeAsync(() => progressWindow.Close());
                    }
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("RomM Uninstall menu failed.", ex);
                    var errorDispatcher = progressWindow.Dispatcher;
                    if (errorDispatcher != null && !errorDispatcher.HasShutdownStarted)
                    {
                        await errorDispatcher.InvokeAsync(() =>
                        {
                            viewModel.StatusText = "Uninstall failed with error.";
                        });
                        await Task.Delay(1500).ConfigureAwait(false);
                        await errorDispatcher.InvokeAsync(() => progressWindow.Close());
                    }
                }
                finally
                {
                    PluginEntry.Logger?.Info($"RomM Uninstall background task finished for '{game?.Title}'.");
                }
            });
        }

        private static void ScheduleProgressWindowHardTimeout(Window progressWindow, int delayMilliseconds, string operationName)
        {
            if (progressWindow == null)
            {
                PluginEntry.Logger?.Warning($"RomM {operationName} hard timeout skipped: progress window is null.");
                return;
            }

            var dispatcher = progressWindow.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                PluginEntry.Logger?.Warning($"RomM {operationName} hard timeout skipped: dispatcher unavailable. HasShutdownStarted={dispatcher?.HasShutdownStarted == true}.");
                return;
            }

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(delayMilliseconds).ConfigureAwait(true);
                    if (progressWindow.IsVisible)
                    {
                        PluginEntry.Logger?.Warning($"RomM {operationName} hard timeout reached; closing progress window.");
                        progressWindow.Close();
                    }
                }
                catch
                {
                    PluginEntry.Logger?.Warning($"RomM {operationName} hard timeout close failed during shutdown.");
                }
            });
        }

        /// <summary>
        /// Placeholder for save import; currently disabled.
        /// </summary>
        private static void ImportSaves(IGame game)
        {
            PluginEntry.EnsureInitialized();
            PluginEntry.Logger?.Info($"RomM Import Saves selected for '{game?.Title}'.");
            PluginEntry.Logger?.Warning("Save import is disabled (TODO: implement save import). Re-enable when ready.");

            MessageBox.Show("Save import is disabled for now (TODO: implement save import).", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        /// <summary>
        /// Placeholder for save upload; currently disabled.
        /// </summary>
        private static void UploadSave(IGame game)
        {
            PluginEntry.EnsureInitialized();
            PluginEntry.Logger?.Info($"RomM Upload Save selected for '{game?.Title}'.");
            PluginEntry.Logger?.Warning("Save upload is disabled (TODO: implement save upload). Re-enable when ready.");

            MessageBox.Show("Save upload is disabled for now (TODO: implement save upload).", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        /// <summary>
        /// Attempts to extract the emulator core name from emulator command line settings.
        /// </summary>
        private static string ResolveEmulatorCore(IDataManager dataManager, IGame game, string platformName)
        {
            if (dataManager == null || game == null || string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            IEmulator emulator = null;
            if (!string.IsNullOrWhiteSpace(game.EmulatorId))
            {
                emulator = dataManager.GetEmulatorById(game.EmulatorId);
            }

            if (emulator == null)
            {
                var emulators = dataManager.GetAllEmulators() ?? Array.Empty<IEmulator>();
                emulator = emulators.FirstOrDefault(entry =>
                    entry?.GetAllEmulatorPlatforms()?.Any(platform =>
                        string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true) == true)
                    ?? emulators.FirstOrDefault(entry =>
                        entry?.GetAllEmulatorPlatforms()?.Any(platform =>
                            string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)) == true);
            }

            if (emulator == null)
            {
                return string.Empty;
            }

            var platforms = emulator.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            var platformEntry = platforms.FirstOrDefault(platform =>
                string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                && platform?.IsDefault == true)
                ?? platforms.FirstOrDefault(platform =>
                    string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase));

            var commandLine = platformEntry?.CommandLine ?? emulator.CommandLine ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return string.Empty;
            }

            var coreArg = ExtractCommandLineValue(commandLine, "-L");
            if (string.IsNullOrWhiteSpace(coreArg))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileNameWithoutExtension(coreArg);
            }
            catch
            {
                return coreArg.Trim();
            }
        }

        /// <summary>
        /// Extracts a value that follows a command line flag (e.g., -L core.dll).
        /// </summary>
        private static string ExtractCommandLineValue(string commandLine, string flag)
        {
            if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(flag))
            {
                return string.Empty;
            }

            var tokens = TokenizeCommandLine(commandLine).ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!string.Equals(tokens[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < tokens.Count)
                {
                    return tokens[i + 1];
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Tokenizes a command line string while preserving quoted segments.
        /// </summary>
        private static IEnumerable<string> TokenizeCommandLine(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                yield break;
            }

            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            foreach (var ch in commandLine)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        /// <summary>
        /// Reads cached save availability for a game if present and not expired.
        /// </summary>
        private static bool? GetSaveAvailability(IGame game, InstallStateService service)
        {
            if (game == null || service == null)
            {
                return null;
            }

            var details = service.GetRomMDetails(game);
            if (string.IsNullOrWhiteSpace(details.RommRomId))
            {
                return null;
            }

            var key = BuildSaveAvailabilityKey(details.RommRomId, details.RommPlatformId);
            if (!SaveAvailabilityCache.TryGetValue(key, out var entry))
            {
                return null;
            }

            var age = DateTimeOffset.UtcNow - entry.CheckedAt;
            if (age > SaveAvailabilityCacheTtl)
            {
                return null;
            }

            return entry.HasSaves;
        }

        /// <summary>
        /// Starts a background request to cache whether the game has saves in RomM.
        /// </summary>
        private static void QueueSaveAvailabilityCheck(IGame game, InstallStateService service)
        {
            if (game == null || service == null)
            {
                return;
            }

            var details = service.GetRomMDetails(game);
            if (string.IsNullOrWhiteSpace(details.RommRomId))
            {
                return;
            }

            var key = BuildSaveAvailabilityKey(details.RommRomId, details.RommPlatformId);
            if (!SaveAvailabilityRequests.TryAdd(key, 0))
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var logger = PluginEntry.Logger;
                    var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(logger);
                    var client = new RommClient(logger, settingsManager);
                    var saves = await client.ListSavesAsync(details.RommRomId, details.RommPlatformId, CancellationToken.None).ConfigureAwait(false);
                    var hasSaves = saves != null && saves.Count > 0;
                    SaveAvailabilityCache[key] = new SaveAvailabilityEntry(hasSaves, DateTimeOffset.UtcNow);
                    logger?.Debug($"Save availability cached for RomM ID {details.RommRomId} (Platform={details.RommPlatformId ?? "<none>"}): HasSaves={hasSaves}");
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Save availability check failed.", ex);
                }
                finally
                {
                    SaveAvailabilityRequests.TryRemove(key, out _);
                }
            });
        }

        /// <summary>
        /// Builds a stable cache key for a RomM game + platform pair.
        /// </summary>
        private static string BuildSaveAvailabilityKey(string romId, string platformId)
        {
            return string.Concat(romId ?? string.Empty, "|", platformId ?? string.Empty);
        }

        /// <summary>
        /// Opens the browser at the RomM play URL for the selected game.
        /// </summary>
        private static void PlayOnRomM(IGame game)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    var installStateService = PluginEntry.InstallStateService;
                    if (installStateService == null)
                    {
                        PluginEntry.Logger?.Warning("InstallStateService is unavailable for Play on RomM.");
                        return;
                    }

                    var details = installStateService.GetRomMDetails(game);
                    var urlService = new RomMPlayUrlService(PluginEntry.Logger);
                    var playUrl = urlService.BuildPlayUrl(details.ServerUrl, details.RommRomId);
                    if (string.IsNullOrWhiteSpace(playUrl))
                    {
                        PluginEntry.Logger?.Warning("Play URL could not be built for selected game.");
                        return;
                    }

                    PluginEntry.Logger?.Info($"Play on RomM selected for game '{game?.Title}'. URL={playUrl}");
                    var launcher = new ExternalLauncherService(PluginEntry.Logger);
                    if (!launcher.TryOpenUrl(playUrl))
                    {
                        PluginEntry.Logger?.Warning("Failed to launch browser for Play on RomM.");
                    }
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Play on RomM menu failed.", ex);
                }
            });
        }

        private static void ViewOnRomM(IGame game)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(() =>
            {
                var logger = PluginEntry.Logger;
                var operationId = Guid.NewGuid().ToString("N");
                using (logger?.BeginOperation(operationId))
                {
                    var start = DateTimeOffset.UtcNow;
                    logger?.Write(LogLevel.Debug, "ViewOnRomMRequested", null,
                        "OperationId", operationId,
                        "GameTitle", game?.Title ?? string.Empty,
                        "Platform", game?.Platform ?? string.Empty);

                    try
                    {
                        var installStateService = PluginEntry.InstallStateService;
                        if (installStateService == null)
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMMissingInstallStateService", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "RomM services are not available.");
                            return;
                        }

                        var details = installStateService.GetRomMDetails(game);
                        var serverUrl = details.ServerUrl?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(serverUrl))
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMServerUrlMissing", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "Server URL is not configured. Configure it in RomM settings.");
                            return;
                        }

                        var romId = details.RommRomId?.Trim();
                        if (string.IsNullOrWhiteSpace(romId))
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMMissingRomId", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "RomM ID is missing for this game.");
                            return;
                        }

                        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMInvalidServerUrl", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "Server URL is invalid. Please update it in RomM settings.");
                            return;
                        }

                        var urlService = new RommViewUrlService(logger);
                        var viewUrl = urlService.BuildViewUrl(serverUrl, romId);
                        if (string.IsNullOrWhiteSpace(viewUrl))
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMUrlBuildFailed", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "RomM URL could not be built. Please check settings.");
                            return;
                        }

                        var launcher = new ExternalLauncherService(logger);
                        if (!launcher.TryOpenUrl(viewUrl))
                        {
                            logger?.Write(LogLevel.Warning, "ViewOnRomMBrowserLaunchFailed", null,
                                "OperationId", operationId,
                                "GameTitle", game?.Title ?? string.Empty,
                                "Platform", game?.Platform ?? string.Empty);
                            ShowInfoDialog("RomM", "Failed to open the RomM page in your browser.");
                            return;
                        }

                        logger?.Write(LogLevel.Info, "ViewOnRomMOpened", null,
                            "OperationId", operationId,
                            "GameTitle", game?.Title ?? string.Empty,
                            "Platform", game?.Platform ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        logger?.Write(LogLevel.Error, "ViewOnRomMFailed", ex,
                            "OperationId", operationId,
                            "GameTitle", game?.Title ?? string.Empty,
                            "Platform", game?.Platform ?? string.Empty);
                        ShowInfoDialog("RomM", "Unable to open the RomM page. Please try again.");
                    }
                    finally
                    {
                        var duration = DateTimeOffset.UtcNow - start;
                        logger?.Write(LogLevel.Info, "ViewOnRomMCompleted", null,
                            "OperationId", operationId,
                            "DurationMs", (long)duration.TotalMilliseconds,
                            "Game", $"{game?.Title ?? string.Empty} ({game?.Platform ?? string.Empty})");
                    }
                }
            });
        }

        private static void OpenProperties(IGame game)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    var logger = PluginEntry.Logger;
                    var installStateService = PluginEntry.InstallStateService;
                    if (installStateService == null)
                    {
                        logger?.Warning("InstallStateService unavailable while opening properties.");
                        ShowInfoDialog("RomM", "RomM services are not available.");
                        return;
                    }

                    var contextBuilder = new GamePropertiesContextBuilder(installStateService, logger);
                    var context = contextBuilder.BuildAsync(game, CancellationToken.None).GetAwaiter().GetResult();
                    if (context == null)
                    {
                        logger?.Warning("Failed to build game properties context.");
                        ShowInfoDialog("RomM", "Failed to load game properties.");
                        return;
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var dialog = new GamePropertiesWindow();
                        var viewModel = new GamePropertiesViewModel(context, logger)
                        {
                            CloseAction = dialog.Close
                        };
                        dialog.DataContext = viewModel;
                        dialog.Owner = Application.Current?.MainWindow;
                        dialog.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Error handling RomM game properties selection.", ex);
                    ShowInfoDialog("RomM", "Unable to open game properties.");
                }
            });
        }

        private static void ShowInfoDialog(string title, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            dispatcher.Invoke(() =>
            {
                var dialog = new RomMbox.UI.Views.InfoDialog(title, message)
                {
                    Owner = Application.Current?.MainWindow
                };
                dialog.ShowDialog();
            });
        }

        private static void EnsureEmulatorId(IDataManager dataManager, IGame game)
        {
            if (dataManager == null || game == null || string.IsNullOrWhiteSpace(game.Platform))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(game.EmulatorId))
            {
                return;
            }

            var emulatorId = ResolveEmulatorId(dataManager, game.Platform);
            if (!string.IsNullOrWhiteSpace(emulatorId))
            {
                game.EmulatorId = emulatorId;
            }
        }

        private static void OpenRommServer()
        {
            PluginEntry.EnsureInitialized();
            try
            {
                var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(PluginEntry.Logger);
                var settings = settingsManager.Load();
                var serverUrl = settings?.ServerUrl?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    PluginEntry.Logger?.Warning("Open RomM skipped: server URL not configured.");
                    return;
                }

                var launcher = new ExternalLauncherService(PluginEntry.Logger);
                if (!launcher.TryOpenUrl(serverUrl))
                {
                    PluginEntry.Logger?.Warning("Failed to launch browser for RomM server URL.");
                }
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Error("Open RomM menu failed.", ex);
            }
        }

        private static string ToLaunchBoxRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                var root = PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(root))
                {
                    return absolutePath;
                }

                var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
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
        /// Checks whether the game is playable via RomM for its platform.
        /// </summary>
        private static bool CanPlayOnRomM(IGame game, InstallStateService service)
        {
            if (game == null || service == null)
            {
                return false;
            }

            var stopwatch = Stopwatch.StartNew();

            var details = service.GetRomMDetails(game);
            var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(PluginEntry.Logger);
            var playable = RommPlayability.IsPlayablePlatform(details.RommPlatformId, game.Platform, settingsManager);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                PluginEntry.Logger?.Warning($"CanPlayOnRomM slow. DurationMs={stopwatch.ElapsedMilliseconds}, Game='{game?.Title ?? "<null>"}', Platform='{game?.Platform ?? "<null>"}'.");
            }
            if (!playable)
            {
                return false;
            }

            return true;
        }

        private static bool CanPlayOnRomMFast(IGame game)
        {
            if (game == null)
            {
                return false;
            }

            var stopwatch = Stopwatch.StartNew();
            var playable = RommPlayability.IsPlayablePlatform(game.Platform);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                PluginEntry.Logger?.Warning($"CanPlayOnRomMFast slow. DurationMs={stopwatch.ElapsedMilliseconds}, Game='{game?.Title ?? "<null>"}', Platform='{game?.Platform ?? "<null>"}'.");
            }
            return playable;
        }

        /// <summary>
        /// Simple menu item wrapper used by the RomM submenu.
        /// </summary>
        private sealed class RommGameMenuItem : IGameMenuItem
        {
            private readonly Action _onSelect;
            private readonly IReadOnlyList<IGameMenuItem> _children;
            private readonly Image _icon;

            public RommGameMenuItem(string caption, bool enabled, Image icon, Action onSelect)
            {
                Caption = caption;
                Enabled = enabled;
                _onSelect = onSelect;
                _icon = icon;
                _children = null;
            }

            public RommGameMenuItem(string caption, bool enabled, Image icon, IReadOnlyList<IGameMenuItem> children)
            {
                Caption = caption;
                Enabled = enabled;
                _icon = icon;
                _children = children;
            }

            public string Caption { get; }

            public IEnumerable<IGameMenuItem> Children => _children;

            public bool Enabled { get; }

            public System.Drawing.Image Icon => _icon;

            public void OnSelect(IGame[] games)
            {
                if (!Enabled)
                {
                    return;
                }

                _onSelect?.Invoke();
            }
        }

        /// <summary>
        /// Loads the RomM badge image from the plugin asset folder.
        /// </summary>
        private static Image ResolveRommBadge()
        {
            return RommBadgeCache.Value;
        }

        private static Image ResolveRommBadgeInternal()
        {
            var stopwatch = Stopwatch.StartNew();
            var pluginRoot = PluginPaths.GetPluginRootDirectory();
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                stopwatch.Stop();
                return null;
            }

            var assetPath = Path.Combine(pluginRoot, "system", "assets", "romm.png");
            var image = LoadImageFromFile(assetPath);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                PluginEntry.Logger?.Warning($"ResolveRommBadge slow. DurationMs={stopwatch.ElapsedMilliseconds}, AssetPath='{assetPath}'.");
            }
            return image;
        }

        /// <summary>
        /// Locates standard LaunchBox badges by filename.
        /// </summary>
        private static Image ResolveBadge(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (MenuImageLoadingDisabled)
            {
                PluginEntry.Logger?.Debug($"Menu image loading disabled. Badge='{fileName}'.");
                return null;
            }

            var stopwatch = Stopwatch.StartNew();

            if (BadgeCache.TryGetValue(fileName, out var cached))
            {
                stopwatch.Stop();
                return cached;
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                stopwatch.Stop();
                return null;
            }

            var candidates = new[]
            {
                Path.Combine(launchBoxRoot, "Images", "Badges", fileName),
                Path.Combine(launchBoxRoot, "Images", "Media Packs", "Badges", "Default Badges", fileName),
                Path.Combine(launchBoxRoot, "Images", "Media Packs", "Badges", "Nostalgic Platform Badges", fileName)
            };

            foreach (var path in candidates)
            {
                var image = LoadImageFromFile(path);
                if (image != null)
                {
                    BadgeCache[fileName] = image;
                    stopwatch.Stop();
                    if (stopwatch.ElapsedMilliseconds > 50)
                    {
                        PluginEntry.Logger?.Warning($"ResolveBadge slow. DurationMs={stopwatch.ElapsedMilliseconds}, FileName='{fileName}'.");
                    }
                    return image;
                }
            }

            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                PluginEntry.Logger?.Warning($"ResolveBadge slow (not found). DurationMs={stopwatch.ElapsedMilliseconds}, FileName='{fileName}'.");
            }
            return null;
        }

        /// <summary>
        /// Loads an image from the plugin asset folder.
        /// </summary>
        private static Image ResolvePluginAssetImage(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (MenuImageLoadingDisabled)
            {
                PluginEntry.Logger?.Debug($"Menu image loading disabled. Asset='{fileName}'.");
                return null;
            }

            var stopwatch = Stopwatch.StartNew();

            if (PluginAssetCache.TryGetValue(fileName, out var cached))
            {
                stopwatch.Stop();
                return cached;
            }

            var pluginRoot = PluginPaths.GetPluginRootDirectory();
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                stopwatch.Stop();
                return null;
            }

            var assetPath = Path.Combine(pluginRoot, "system", "assets", fileName);
            var image = LoadImageFromFile(assetPath);
            if (image != null)
            {
                PluginAssetCache[fileName] = image;
            }
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                PluginEntry.Logger?.Warning($"ResolvePluginAssetImage slow. DurationMs={stopwatch.ElapsedMilliseconds}, FileName='{fileName}'.");
            }
            return image;
        }

        /// <summary>
        /// Loads an image from disk into memory, returning a clone to avoid file locks.
        /// </summary>
        private static Image LoadImageFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || MenuImageLoadingDisabled || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                var cloned = (Image)image.Clone();
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 50)
                {
                    PluginEntry.Logger?.Warning($"Menu image load slow. DurationMs={stopwatch.ElapsedMilliseconds}, Path='{path}'.");
                }
                return cloned;
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Warning($"Menu image load failed. Path='{path}'. {ex.Message}");
                return null;
            }
        }

        private static bool MenuImageLoadingDisabled
        {
            get
            {
                try
                {
                    return string.Equals(Environment.GetEnvironmentVariable("ROMM_MENU_NO_IMAGES"), "1", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Resolves the game application path to an absolute path and validates it.
        /// </summary>
        private static bool HasValidApplicationPath(IGame game, out string resolvedPath)
        {
            var stopwatch = Stopwatch.StartNew();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            resolvedPath = game?.ApplicationPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                stopwatch.Stop();
                return false;
            }

            if (game?.Installed != true)
            {
                stopwatch.Stop();
                return false;
            }

            try
            {
                if (!System.IO.Path.IsPathRooted(resolvedPath))
                {
                    var root = Services.Paths.PluginPaths.GetLaunchBoxRootDirectory();
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        resolvedPath = System.IO.Path.Combine(root, resolvedPath);
                    }
                }

                var exists = System.IO.File.Exists(resolvedPath) || System.IO.Directory.Exists(resolvedPath);
                return exists;
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Warning($"Application path validation failed. Path='{resolvedPath}'. {ex.Message}");
                return false;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 50)
                {
                    PluginEntry.Logger?.Warning($"Application path validation slow. DurationMs={stopwatch.ElapsedMilliseconds}, ThreadId={threadId}, Path='{resolvedPath}'.");
                }
            }
        }

        private readonly struct SaveAvailabilityEntry
        {
            /// <summary>
            /// Captures cached save availability and the time it was checked.
            /// </summary>
            public SaveAvailabilityEntry(bool hasSaves, DateTimeOffset checkedAt)
            {
                HasSaves = hasSaves;
                CheckedAt = checkedAt;
            }

            /// <summary>
            /// True when at least one save was found for the RomM game.
            /// </summary>
            public bool HasSaves { get; }

            /// <summary>
            /// UTC timestamp for when the availability check completed.
            /// </summary>
            public DateTimeOffset CheckedAt { get; }
        }
    }
}
