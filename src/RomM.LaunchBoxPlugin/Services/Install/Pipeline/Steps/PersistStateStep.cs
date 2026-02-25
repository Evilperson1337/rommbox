using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class PersistStateStep : IInstallStep
    {
        public InstallPhase Phase => InstallPhase.PostProcessing;

        public async Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return InstallResult.Failed(Phase, "Install state missing.");
            }

            await context.InstallStateService
                .UpsertStateAsync(context.InstallStateSnapshot.ToInstallState(), cancellationToken)
                .ConfigureAwait(false);

            var additionalAppService = new RommAdditionalApplicationService(context.Logger, context.InstallStateService);
            try
            {
                var operationId = Guid.NewGuid().ToString("N");
                var state = await context.InstallStateService.GetStateAsync(context.Game.Id, cancellationToken).ConfigureAwait(false);
                await additionalAppService.SyncAdditionalApplicationAsync(context.Game, state, operationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Logger?.Write(LogLevel.Error, "RomMAdditionalAppSyncFailed", ex,
                    "BaseGameId", context.Game?.Id ?? string.Empty);
            }

            SaveAndReloadDataManager(context.DataManager, context.Logger);
            return InstallResult.Successful();
        }

        private static void SaveAndReloadDataManager(Unbroken.LaunchBox.Plugins.Data.IDataManager dataManager, Services.Logging.LoggingService logger)
        {
            if (dataManager == null)
            {
                return;
            }

            try
            {
                var operationId = Guid.NewGuid().ToString("N");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                logger?.Debug($"PersistState save/reload started. OpId={operationId}.");
                dataManager.Save(false);
                logger?.Debug($"PersistState Save(false) completed. OpId={operationId}, ElapsedMs={stopwatch.ElapsedMilliseconds}.");
                dataManager.ReloadIfNeeded();
                logger?.Debug($"PersistState ReloadIfNeeded completed. OpId={operationId}, ElapsedMs={stopwatch.ElapsedMilliseconds}.");
                dataManager.ForceReload();
                logger?.Debug($"PersistState ForceReload completed. OpId={operationId}, ElapsedMs={stopwatch.ElapsedMilliseconds}.");
            }
            catch (Exception ex)
            {
                logger?.Warning($"Save/Reload failed after install: {ex.Message}");
            }
        }
    }
}
