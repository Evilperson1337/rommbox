using System;
using System.Threading;
using System.Threading.Tasks;

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

            SaveAndReloadDataManager(context.DataManager);
            return InstallResult.Successful();
        }

        private static void SaveAndReloadDataManager(Unbroken.LaunchBox.Plugins.Data.IDataManager dataManager)
        {
            if (dataManager == null)
            {
                return;
            }

            dataManager.Save(true);
            dataManager.ReloadIfNeeded();
            dataManager.ForceReload();
        }
    }
}
