using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class ResolveMetadataStep : IInstallStep
    {
        private readonly IRommClient _client;

        public ResolveMetadataStep(IRommClient client)
        {
            _client = client;
        }

        public InstallPhase Phase => InstallPhase.ResolvingMetadata;

        public async Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.Game == null)
            {
                return InstallResult.Failed(Phase, "Game missing.");
            }

            var details = context.InstallStateService.GetRomMDetails(context.Game);
            if (string.IsNullOrWhiteSpace(details.RommRomId))
            {
                return InstallResult.Failed(Phase, "RomM ROM id is missing.");
            }

            progress?.Report(new InstallProgressEvent(Phase, "Fetching game details..."));
            var rom = await _client.GetRomDetailsAsync(details.RommRomId, cancellationToken).ConfigureAwait(false);
            if (rom == null)
            {
                return InstallResult.Failed(Phase, "Failed to resolve RomM details.");
            }

            context.RommDetails = rom;
            context.InstallStateSnapshot = new InstallStateSnapshot
            {
                LaunchBoxGameId = context.Game.Id,
                RommRomId = rom.Id,
                RommPlatformId = rom.PlatformId,
                ServerUrl = context.SettingsManager.Load().ServerUrl
            };
            return InstallResult.Successful();
        }
    }
}
