using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Services.Logging;
using RomMbox.Plugin;
using RomMbox.Services.PlatformInstallers;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Orchestrates uninstall operations with progress reporting.
    /// </summary>
    internal sealed class RomMUninstallService
    {
        private readonly LoggingService _logger;
        private readonly InstallStateService _installStateService;
        private readonly RomMDeleteService _deleteService;
        private readonly PlatformInstallerRegistry _platformInstallers;
        private readonly PlatformLoggerAdapter _platformLogger;

        /// <summary>
        /// Creates a new uninstall service.
        /// </summary>
        public RomMUninstallService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
            _deleteService = new RomMDeleteService(logger, installStateService);
            _platformInstallers = PluginEntry.PlatformInstallers ?? new PlatformInstallerLoader(logger).Load();
            _platformLogger = new PlatformLoggerAdapter(logger);
        }

        /// <summary>
        /// Uninstalls local content for a RomM-sourced game with progress reporting.
        /// </summary>
        public async Task<RomMDeleteResult> UninstallAsync(
            IGame game,
            IDataManager dataManager,
            CancellationToken cancellationToken,
            IProgress<UninstallProgress> progress)
        {
            progress?.Report(new UninstallProgress("Uninstalling Game", "Preparing uninstall...", 0, true));

            if (game == null || dataManager == null)
            {
                return RomMDeleteResult.Failed("Game or DataManager unavailable.");
            }

            if (_installStateService == null || !_installStateService.IsRomMSourcedGame(game))
            {
                return RomMDeleteResult.Failed("Selected game is not RomM-sourced.");
            }

            try
            {
                progress?.Report(new UninstallProgress("Uninstalling Game", "Resolving install state...", 10, true));
                var resolveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                resolveStopwatch.Stop();
                if (resolveStopwatch.ElapsedMilliseconds > 500)
                {
                    _logger?.Warning($"Uninstall resolve state slow. DurationMs={resolveStopwatch.ElapsedMilliseconds}.");
                }

                progress?.Report(new UninstallProgress("Removing Content", "Deleting local content...", 35, true));
                var uninstallStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await TryPlatformUninstallAsync(game, dataManager, cancellationToken, progress)
                    .ConfigureAwait(false);
                uninstallStopwatch.Stop();
                if (uninstallStopwatch.ElapsedMilliseconds > 500)
                {
                    _logger?.Warning($"Uninstall delete/uninstall slow. DurationMs={uninstallStopwatch.ElapsedMilliseconds}.");
                }

                progress?.Report(new UninstallProgress("Finishing", "Finalizing uninstall...", 90, true));
                var finishStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                finishStopwatch.Stop();
                if (finishStopwatch.ElapsedMilliseconds > 500)
                {
                    _logger?.Warning($"Uninstall finalize slow. DurationMs={finishStopwatch.ElapsedMilliseconds}.");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger?.Warning("RomM uninstall cancelled.");
                return RomMDeleteResult.Failed("Uninstall cancelled.");
            }
            catch (Exception ex)
            {
                _logger?.Error("RomM uninstall failed.", ex);
                return RomMDeleteResult.Failed(ex.Message);
            }
        }

        private async Task<RomMDeleteResult> TryPlatformUninstallAsync(
            IGame game,
            IDataManager dataManager,
            CancellationToken cancellationToken,
            IProgress<UninstallProgress> progress)
        {
            var platform = dataManager.GetPlatformByName(game.Platform);
            if (platform == null)
            {
                return await _deleteService.DeleteOrUninstallAsync(game, dataManager, cancellationToken)
                    .ConfigureAwait(false);
            }

            var state = await _installStateService.GetStateAsync(game.Id, cancellationToken).ConfigureAwait(false);
            if (state == null)
            {
                return await _deleteService.DeleteOrUninstallAsync(game, dataManager, cancellationToken)
                    .ConfigureAwait(false);
            }

            var isWindows = InstallDestinationService.IsWindowsPlatform(platform.Name);
            var platformKey = isWindows ? "windows" : state.RommPlatformId ?? string.Empty;
            var resolvedPlatformKey = Services.Install.Pipeline.Steps.InstallContentStep.ResolveInstallerKey(
                platformKey,
                state.RommPlatformId,
                game.Platform,
                _platformInstallers,
                _logger);

            if (!string.IsNullOrWhiteSpace(resolvedPlatformKey)
                && !string.Equals(resolvedPlatformKey, platformKey, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info($"Uninstall mapped RomM platform identifier '{platformKey}' to installer key '{resolvedPlatformKey}'.");
            }

            _logger?.Info($"Uninstall loaded install state for '{game.Title}'. PlatformContentId='{state.PlatformContentId ?? string.Empty}', InstalledPath='{state.InstalledPath ?? string.Empty}', InstallRootPath='{state.InstallRootPath ?? string.Empty}'.");

            var keyToUse = string.IsNullOrWhiteSpace(resolvedPlatformKey) ? platformKey : resolvedPlatformKey;
            if (_platformInstallers == null || string.IsNullOrWhiteSpace(keyToUse)
                || !_platformInstallers.TryGetInstaller(keyToUse, out var installer))
            {
                _logger?.Warning($"Platform uninstall fallback engaged for '{game.Title}'. RomMPlatformId='{state.RommPlatformId ?? string.Empty}', LaunchBoxPlatform='{game.Platform ?? string.Empty}', ResolvedKey='{keyToUse}'.");
                return await _deleteService.DeleteOrUninstallAsync(game, dataManager, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.Equals(keyToUse, "xbox360", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(state.PlatformContentId))
            {
                var recoveredTitleId = TryRecoverXbox360TitleIdFromInstalledContent(installer, game.Title, state);

                if (!string.IsNullOrWhiteSpace(recoveredTitleId))
                {
                    state.PlatformContentId = recoveredTitleId;
                    await _installStateService.UpsertStateAsync(state, cancellationToken).ConfigureAwait(false);
                    _logger?.Info($"Uninstall backfilled PlatformContentId='{recoveredTitleId}' into install state for '{game.Title}'.");
                }
            }

            var installRoot = state?.InstallRootPath ?? string.Empty;
            var installType = state?.WindowsInstallType ?? string.Empty;
            var uninstallContext = new RomM.Platforms.Abstractions.Models.Uninstall.UninstallContext
            {
                GameName = game.Title,
                InstallRootPath = installRoot,
                InstalledPath = state?.InstalledPath,
                ArchivePath = state?.ArchivePath,
                PlatformContentId = state?.PlatformContentId,
                EmulatorExecutablePath = PlatformInstallSettingsMapper.ResolveEmulatorExecutablePath(new Models.PlatformMapping.PlatformMapping
                {
                    AssociatedEmulatorId = game?.EmulatorId ?? string.Empty
                }, dataManager, game?.Platform),
                WindowsInstallType = installType,
                Logger = _platformLogger
            };

            var result = await installer
                .UninstallAsync(uninstallContext, new Progress<RomM.Platforms.Abstractions.Models.Install.InstallProgress>(update =>
                {
                    var percent = update.Percent.HasValue
                        ? Math.Clamp(update.Percent.Value, 0, 100)
                        : 35;
                    progress?.Report(new UninstallProgress("Removing Content", update.Message ?? "Removing content...", percent, update.IsIndeterminate));
                }), cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return RomMDeleteResult.Failed(result.Message ?? "Uninstall failed.");
            }

            return await _deleteService.DeleteOrUninstallAsync(game, dataManager, cancellationToken)
                .ConfigureAwait(false);
        }

        private string TryRecoverXbox360TitleIdFromInstalledContent(RomM.Platforms.Abstractions.IPlatformInstaller installer, string gameTitle, Models.InstallState state)
        {
            try
            {
                var method = installer.GetType().GetMethod(
                    "TryResolveTitleIdFromInstalledContent",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    _logger?.Warning("Xbox 360 uninstall could not find runtime Title ID recovery method on installer type.");
                    return string.Empty;
                }

                var result = method.Invoke(null, new object[]
                {
                    new RomM.Platforms.Abstractions.Models.Uninstall.UninstallContext
                    {
                        GameName = gameTitle,
                        InstallRootPath = state?.InstallRootPath,
                        InstalledPath = state?.InstalledPath,
                        ArchivePath = state?.ArchivePath,
                        Logger = _platformLogger
                    },
                    _platformLogger
                }) as string;

                return result ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Xbox 360 uninstall failed to recover Title ID for DB backfill: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
