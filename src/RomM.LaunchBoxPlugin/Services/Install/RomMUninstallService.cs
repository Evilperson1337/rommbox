using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Services.Logging;
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

        /// <summary>
        /// Creates a new uninstall service.
        /// </summary>
        public RomMUninstallService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
            _deleteService = new RomMDeleteService(logger, installStateService);
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
                var result = await _deleteService.DeleteOrUninstallAsync(game, dataManager, cancellationToken)
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
    }
}
