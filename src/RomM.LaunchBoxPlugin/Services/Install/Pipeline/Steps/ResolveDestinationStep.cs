using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class ResolveDestinationStep : IInstallStep
    {
        private readonly InstallDestinationService _destinationService;
        private readonly PlatformMappingStore _mappingStore;

        public ResolveDestinationStep(InstallDestinationService destinationService, PlatformMappingStore mappingStore)
        {
            _destinationService = destinationService;
            _mappingStore = mappingStore;
        }

        public InstallPhase Phase => InstallPhase.ResolvingDestination;

        public async Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.RommDetails == null)
            {
                return InstallResult.Failed(Phase, "RomM details missing.");
            }

            progress?.Report(new InstallProgressEvent(Phase, "Resolving platform mapping..."));
            var mapping = await _mappingStore
                .GetPlatformMappingAsync(context.RommDetails.PlatformId ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            context.PlatformMapping = mapping;

            progress?.Report(new InstallProgressEvent(Phase, "Resolving install destination..."));
            var location = await _destinationService
                .ResolveInstallLocationAsync(context.Game, mapping?.InstallerMode ?? InstallerMode.Manual, cancellationToken)
                .ConfigureAwait(false);
            if (!location.Success || string.IsNullOrWhiteSpace(location.InstallDirectory))
            {
                return InstallResult.Failed(Phase, location.Message ?? "Install directory unavailable.");
            }

            var platform = context.DataManager.GetPlatformByName(context.Game.Platform);
            if (platform == null)
            {
                return InstallResult.Failed(Phase, "LaunchBox platform not found.");
            }

            context.InstallDirectory = location.InstallDirectory;
            context.DownloadDirectory = InstallDestinationService.IsWindowsPlatform(platform.Name)
                ? location.InstallDirectory
                : EnsureGameSubfolder(location.InstallDirectory, context.Game.Title);
            return InstallResult.Successful();
        }

        private static string EnsureGameSubfolder(string baseDirectory, string title)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var folderName = NormalizePathSegment(title);
            var combined = System.IO.Path.Combine(baseDirectory, folderName);
            System.IO.Directory.CreateDirectory(combined);
            return combined;
        }

        private static string NormalizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = new string(value.ToCharArray().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned.Trim();
        }
    }
}
