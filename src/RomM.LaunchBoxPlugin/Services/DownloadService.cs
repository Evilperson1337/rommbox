using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Download;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services
{
    /// <summary>
    /// Handles downloading ROM files from RomM and optionally extracting them.
    /// Manages temporary storage and emits progress for UI updates.
    /// </summary>
    internal sealed class DownloadService
    {
        private readonly LoggingService _logger;
        private readonly IRommClient _rommClient;
        private readonly ArchiveService _archiveService;
        private readonly SettingsManager _settingsManager;

        /// <summary>
        /// Creates the download service with required dependencies.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="rommClient">RomM API client used to fetch content.</param>
        /// <param name="archiveService">Archive helper for extraction.</param>
        /// <param name="settingsManager">Settings provider for options like server URL.</param>
        public DownloadService(LoggingService logger, IRommClient rommClient, ArchiveService archiveService, SettingsManager settingsManager)
        {
            _logger = logger;
            _rommClient = rommClient;
            _archiveService = archiveService;
            _settingsManager = settingsManager;
        }

        /// <summary>
        /// Downloads a ROM archive (or file) to a temp location, optionally extracts it,
        /// and returns paths and metadata in a <see cref="DownloadResult"/>.
        /// </summary>
        public async Task<DownloadResult> DownloadRomAsync(
            RommRom rom,
            string downloadDirectory,
            string serverUrl,
            ExtractionBehavior behavior,
            bool extractAfterDownload,
            CancellationToken cancellationToken,
            IProgress<Models.Download.DownloadProgress> progress = null,
            IProgress<Models.Download.DownloadProgress> extractionProgress = null)
        {
            if (rom == null)
            {
                throw new ArgumentNullException(nameof(rom));
            }

            if (string.IsNullOrWhiteSpace(downloadDirectory))
            {
                throw new ArgumentException("Download directory is required.", nameof(downloadDirectory));
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new InvalidOperationException("Server URL is required to download.");
            }

            var result = new DownloadResult();
            var tempRoot = string.Empty;
            try
            {
                _logger?.Info($"Download requested for RomM rom {rom.Id} ({rom.Name}).");
                Directory.CreateDirectory(downloadDirectory);

                // Prefer explicit filename from payload, then filesystem name, then fallback.
                var payload = rom.Payload;
                var fileName = !string.IsNullOrWhiteSpace(payload?.FileName)
                    ? payload.FileName
                    : !string.IsNullOrWhiteSpace(rom.FsName)
                        ? rom.FsName
                        : BuildFallbackFileName(payload, rom);

                var fileIds = rom?.FileIds != null && rom.FileIds.Count > 0
                    ? string.Join(",", rom.FileIds)
                    : null;
                _logger?.Info($"Downloading ROM content via /api/roms/{{id}}/content/{{file_name}}. RomId={rom.Id}, FileName={fileName}, FileIds={fileIds ?? "<none>"}.");
                progress?.Report(new Models.Download.DownloadProgress(0, null));

                // Create a unique temp root for this download to avoid collisions.
                fileName = SanitizeFileName(fileName);
                tempRoot = Path.Combine(Paths.PluginPaths.GetPluginRootDirectory(), "temp", "downloads", Guid.NewGuid().ToString("N"));
                var tempDownloadDir = Path.Combine(tempRoot, "downloads");
                var tempExtractDir = Path.Combine(tempRoot, "extracted");
                Directory.CreateDirectory(tempDownloadDir);
                Directory.CreateDirectory(tempExtractDir);

                var archivePath = Path.Combine(tempDownloadDir, fileName);

                var totalBytes = await _rommClient
                    .DownloadRomContentToFileAsync(rom.Id, fileName, fileIds, archivePath, cancellationToken, progress)
                    .ConfigureAwait(false);
                var fileInfo = new FileInfo(archivePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new InvalidOperationException("Downloaded payload was empty.");
                }

                _logger?.Info($"RomM download saved to {LoggingService.SanitizePath(archivePath)}.");

                var extractedArchive = false;
                if (extractAfterDownload && _archiveService.IsSupportedArchive(archivePath))
                {
                    extractedArchive = true;
                    extractionProgress?.Report(new Models.Download.DownloadProgress(0, 1));
                    var extracted = await _archiveService.ExtractAsync(archivePath, tempExtractDir, behavior, cancellationToken, extractionProgress).ConfigureAwait(false);
                    result.ExtractedPath = extracted;
                    result.InstallType = _archiveService.DetectInstallType(archivePath, extracted);
                    _logger?.Info($"RomM extraction completed. InstallType={result.InstallType}.");
                    extractionProgress?.Report(new Models.Download.DownloadProgress(1, 1));

                    _archiveService.TryDeleteArchive(archivePath);
                }
                else
                {
                    // No extraction requested or archive not supported.
                    if (extractAfterDownload)
                    {
                        _logger?.Info("Extraction skipped for RomM download: archive not supported.");
                    }

                    result.ArchivePath = archivePath;
                    result.ExtractedPath = null;
                }

                result.Success = true;
                if (extractedArchive)
                {
                    result.ArchivePath = null;
                }
                result.TempRoot = tempRoot;
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger?.Warning("RomM download canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error("RomM download failed.", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.TempRoot = tempRoot;
                return result;
            }
        }

        /// <summary>
        /// Attempts to delete the temporary download root created by <see cref="DownloadRomAsync"/>.
        /// </summary>
        public void TryCleanupTempRoot(string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(tempRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                    _logger?.Info($"Temp download root cleaned up: '{LoggingService.SanitizePath(tempRoot)}'.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to clean up temp download root '{LoggingService.SanitizePath(tempRoot)}': {ex.Message}");
            }
        }



        /// <summary>
        /// Sanitizes a file name so it can be used safely on disk.
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? "rom" : fileName;
        }

        /// <summary>
        /// Builds a fallback filename from ROM metadata when no explicit name is available.
        /// </summary>
        private static string BuildFallbackFileName(RommPayload payload, RommRom rom)
        {
            var baseName = rom?.Title ?? rom?.Name ?? rom?.Id ?? "rom";
            var extension = payload?.Extension;
            if (!string.IsNullOrWhiteSpace(extension))
            {
                if (!extension.StartsWith("."))
                {
                    extension = "." + extension;
                }
                return baseName + extension;
            }

            return baseName;
        }
    }
}
