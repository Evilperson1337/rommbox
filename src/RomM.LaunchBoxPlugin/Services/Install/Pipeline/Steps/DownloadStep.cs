using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Download;
using RomMbox.Models.Install;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class DownloadStep : IInstallStep
    {
        private readonly DownloadService _downloadService;

        public DownloadStep(DownloadService downloadService)
        {
            _downloadService = downloadService;
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
            var shouldReportExtraction = extractAfterDownload;

            var downloadProgress = new Progress<DownloadProgress>(update =>
            {
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

            if (shouldReportExtraction && string.IsNullOrWhiteSpace(result.ExtractedPath))
            {
                progress?.Report(new InstallProgressEvent(InstallPhase.Extracting, "Extraction skipped (no supported archive)."));
            }

            if (installScenario == InstallScenario.Basic)
            {
                if (!string.IsNullOrWhiteSpace(result.ExtractedPath))
                {
                    result.ExtractedPath = InstallContentRelocator.RelocateExtractedContent(result.ExtractedPath, context.DownloadDirectory, context.Logger);
                }
                else if (!string.IsNullOrWhiteSpace(result.ArchivePath))
                {
                    result.ArchivePath = InstallContentRelocator.RelocateArchive(result.ArchivePath, context.DownloadDirectory, context.Logger);
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
    }
}
