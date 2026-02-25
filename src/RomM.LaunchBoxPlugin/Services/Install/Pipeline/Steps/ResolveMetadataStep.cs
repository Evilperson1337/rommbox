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
            context.Logger?.Info($"Resolving RomM metadata for '{context.Game.Title}'. RommRomId={details.RommRomId}.");
            var timeout = TimeSpan.FromSeconds(30);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            RomMbox.Models.Romm.RommRom rom;
            try
            {
                rom = await _client.GetRomDetailsAsync(details.RommRomId, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                context.Logger?.Warning($"Resolving RomM metadata timed out after {timeout.TotalSeconds:0} seconds for RommRomId={details.RommRomId}.");
                return InstallResult.Failed(Phase, $"Metadata lookup timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (Exception ex)
            {
                context.Logger?.Warning($"Resolving RomM metadata failed for RommRomId={details.RommRomId}. {ex.Message}");
                return InstallResult.Failed(Phase, $"Metadata lookup failed: {ex.Message}");
            }
            if (rom == null)
            {
                return InstallResult.Failed(Phase, "Failed to resolve RomM details.");
            }

            context.Logger?.Info($"Resolved RomM metadata for '{context.Game.Title}'. RommRomId={details.RommRomId}.");

            context.RommDetails = rom;
            context.InstallStateSnapshot = new InstallStateSnapshot
            {
                LaunchBoxGameId = context.Game.Id,
                RommRomId = rom.Id,
                RommPlatformId = rom.PlatformId,
                ServerUrl = context.SettingsManager.Load().ServerUrl,
                InstallStatus = "InProgress",
                InstallPhase = Phase.ToString(),
                LastAttemptUtc = DateTimeOffset.UtcNow,
                LastOperationId = context.OperationId
            };
            return InstallResult.Successful();
        }
    }
}
