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
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                progress?.Report(new UninstallProgress("Removing Content", "Deleting local content...", 35, true));
                var result = await Task.Run(() => _deleteService.DeleteOrUninstall(game, dataManager, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);

                progress?.Report(new UninstallProgress("Finishing", "Finalizing uninstall...", 90, true));
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

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
