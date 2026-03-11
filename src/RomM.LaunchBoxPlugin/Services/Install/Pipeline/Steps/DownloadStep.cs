using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomMbox.Models.Download;
using RomMbox.Models.Install;
using RomMbox.Services.Install;
using RomMbox.Services.PlatformInstallers;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class DownloadStep : IInstallStep
    {
        private readonly DownloadService _downloadService;
        private readonly PlatformInstallerRegistry _platformInstallers;

        public DownloadStep(DownloadService downloadService, PlatformInstallerRegistry platformInstallers)
        {
            _downloadService = downloadService;
            _platformInstallers = platformInstallers;
        }

        public InstallPhase Phase => InstallPhase.Downloading;

        public async Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.RommDetails == null)
            {
                return InstallResult.Failed(Phase, "RomM details missing.");
            }

            if (string.IsNullOrWhiteSpace(context.DownloadDirectory))
            {
                return InstallResult.Failed(Phase, "Download directory missing.");
            }

            var mapping = context.PlatformMapping;
            var extractAfterDownload = mapping?.ExtractAfterDownload ?? false;
            var extractionBehavior = mapping?.ExtractionBehavior ?? Models.PlatformMapping.ExtractionBehavior.Subfolder;
            var installScenario = mapping?.InstallScenario ?? InstallScenario.Basic;
            var detectInstallType = installScenario != InstallScenario.Basic;
            var serverUrl = context.SettingsManager.Load().ServerUrl;
            var isWindowsPlatform = InstallDestinationService.IsWindowsPlatform(context.Game?.Platform);
            var platformId = context.RommDetails?.PlatformId ?? string.Empty;
            var platformDisplayName = context.RommDetails?.PlatformDisplayName ?? string.Empty;
            var launchBoxPlatformName = context.Game?.Platform ?? string.Empty;

            var capabilities = ResolveCapabilities(context);
            if (capabilities.RequiresStagingInspection)
            {
                extractAfterDownload = true;
                context.Logger?.Info("Extraction required by plugin capabilities (RequiresStagingInspection=true).");
            }
            else if (isWindowsPlatform && !extractAfterDownload)
            {
                context.Logger?.Info("Extraction forced for Windows install pipeline.");
                extractAfterDownload = true;
            }
            else if (!isWindowsPlatform && string.Equals(mapping?.RomArchivePolicy, "Preserve", StringComparison.OrdinalIgnoreCase))
            {
                context.Logger?.Info("Extraction disabled for ROM platform policy (Preserve)." );
                extractAfterDownload = false;
            }

            if (IsArchivePreservePlatform(platformId, platformDisplayName, launchBoxPlatformName))
            {
                context.Logger?.Info("Extraction disabled for ROM platform (archives must be preserved)." );
                extractAfterDownload = false;
            }

            context.Logger?.Info($"ExtractionDecision | ExtractAfterDownload={extractAfterDownload}, Behavior={extractionBehavior}, IsWindows={isWindowsPlatform}, InstallScenario={installScenario}.");
            context.Logger?.Info($"Archive download requested. RomId={context.RommDetails.Id ?? string.Empty}, PlatformId={context.RommDetails.PlatformId ?? string.Empty}.");
            var shouldReportExtraction = extractAfterDownload;

            var downloadProgress = new Progress<DownloadProgress>(update =>
            {
                if (!context.DownloadStartedUtc.HasValue)
                {
                    context.DownloadStartedUtc = DateTimeOffset.UtcNow;
                }
                if (update.TotalBytes.HasValue && update.TotalBytes.Value > 0)
                {
                    var percent = Math.Clamp((update.BytesReceived / (double)update.TotalBytes.Value) * 100d, 0, 100);
                    var detail = $"Downloading... {FormatBytes(update.BytesReceived)} / {FormatBytes(update.TotalBytes.Value)}";
                    progress?.Report(new InstallProgressEvent(Phase, detail, percent));
                }
                else
                {
                    var detail = $"Downloading... {FormatBytes(update.BytesReceived)}";
                    progress?.Report(new InstallProgressEvent(Phase, detail));
                }
            });

            var extractionProgress = new Progress<DownloadProgress>(update =>
            {
                if (!shouldReportExtraction)
                {
                    return;
                }

                if (!context.ExtractionStartedUtc.HasValue)
                {
                    context.ExtractionStartedUtc = DateTimeOffset.UtcNow;
                }

                if (update.TotalBytes.HasValue && update.TotalBytes.Value > 0)
                {
                    var percent = Math.Clamp((update.BytesReceived / (double)update.TotalBytes.Value) * 100d, 0, 100);
                    progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extracting...", percent));
                }
                else
                {
                    progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extracting..."));
                }
            });

            var result = await _downloadService.DownloadRomAsync(
                    context.RommDetails,
                    context.DownloadDirectory,
                    serverUrl,
                    extractionBehavior,
                    extractAfterDownload,
                    cancellationToken,
                    downloadProgress,
                    extractionProgress,
                    detectInstallType)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return InstallResult.Failed(Phase, result.ErrorMessage ?? "Download failed.");
            }

            context.DownloadCompletedUtc = context.DownloadCompletedUtc ?? DateTimeOffset.UtcNow;
            if (shouldReportExtraction && !string.IsNullOrWhiteSpace(result.ExtractedPath))
            {
                context.ExtractionCompletedUtc = context.ExtractionCompletedUtc ?? DateTimeOffset.UtcNow;
            }

            if (shouldReportExtraction && string.IsNullOrWhiteSpace(result.ExtractedPath))
            {
                progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extraction skipped (no supported archive)."));
            }

            if (installScenario == InstallScenario.Basic)
            {
                if (!string.IsNullOrWhiteSpace(result.ExtractedPath))
                {
                    result.ExtractedPath = InstallContentRelocator.RelocateExtractedContent(result.ExtractedPath, context.DownloadDirectory, context.Logger);
                    context.Logger?.Info($"Archive extracted to: '{result.ExtractedPath}'.");
                }
                else if (!string.IsNullOrWhiteSpace(result.ArchivePath))
                {
                    result.ArchivePath = InstallContentRelocator.RelocateArchive(result.ArchivePath, context.DownloadDirectory, context.Logger);
                    context.Logger?.Info($"Archive downloaded: '{result.ArchivePath}'.");
                }
            }

            context.ArchivePath = result.ArchivePath;
            context.ExtractedPath = result.ExtractedPath;
            context.TempRoot = result.TempRoot;
            if (!string.IsNullOrWhiteSpace(result.ExtractedPath))
            {
                context.ArchivePath = result.ArchivePath ?? context.ArchivePath;
            }
            return InstallResult.Successful();
        }

        private PlatformInstallerCapabilities ResolveCapabilities(InstallContext context)
        {
            if (_platformInstallers == null || context?.RommDetails == null)
            {
                return new PlatformInstallerCapabilities();
            }

            var platformKey = context.RommDetails.PlatformId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(platformKey)
                && InstallDestinationService.IsWindowsPlatform(context?.Game?.Platform))
            {
                platformKey = "windows";
            }

            return _platformInstallers.GetCapabilities(platformKey);
        }

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

        private static bool IsArchivePreservePlatform(string platformId, string platformDisplayName, string launchBoxPlatformName)
        {
            if (string.Equals(platformId, "snes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(platformId, "n64", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedDisplay = NormalizePlatformToken(platformDisplayName);
            var normalizedLaunchBox = NormalizePlatformToken(launchBoxPlatformName);

            return normalizedDisplay.Contains("supernintendo", StringComparison.OrdinalIgnoreCase)
                   || normalizedDisplay.Contains("nintendo64", StringComparison.OrdinalIgnoreCase)
                   || normalizedLaunchBox.Contains("supernintendo", StringComparison.OrdinalIgnoreCase)
                   || normalizedLaunchBox.Contains("nintendo64", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePlatformToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
