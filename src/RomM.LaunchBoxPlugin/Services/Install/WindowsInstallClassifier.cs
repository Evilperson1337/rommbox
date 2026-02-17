using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Detects Windows installer types and related metadata.
    /// </summary>
    internal sealed class WindowsInstallClassifier
    {
        private readonly ArchiveService _archiveService;
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates a new Windows install classifier.
        /// </summary>
        /// <param name="archiveService">Archive service used for detection and extraction.</param>
        /// <param name="logger">Logging service.</param>
        public WindowsInstallClassifier(ArchiveService archiveService, LoggingService logger)
        {
            _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
            _logger = logger;
        }

        /// <summary>
        /// Detects the install type based on archive and extracted content.
        /// </summary>
        /// <param name="archivePath">Archive path.</param>
        /// <param name="extractedPath">Extracted path, if any.</param>
        /// <returns>The detected install type.</returns>
        public InstallType DetectInstallType(string archivePath, string extractedPath)
        {
            var installType = _archiveService.DetectInstallType(archivePath, extractedPath);
            _logger?.Debug($"Windows install type detection result: Archive='{archivePath}', Extracted='{extractedPath}', Type={installType}.");
            return installType;
        }

        /// <summary>
        /// Checks whether an extracted directory contains an Inno Setup installer.
        /// </summary>
        /// <param name="extractedPath">Extracted directory path.</param>
        /// <returns><c>true</c> when an Inno installer is found.</returns>
        public bool IsInnoInstaller(string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return false;
            }

            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return false;
            }

            return IsInnoInstallerExe(setupPath);
        }

        /// <summary>
        /// Checks whether a specific executable has an Inno Setup signature.
        /// </summary>
        /// <param name="exePath">Executable path to inspect.</param>
        /// <returns><c>true</c> if the signature is present.</returns>
        public bool IsInnoInstallerExe(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(exePath);
                var marker = Encoding.ASCII.GetBytes("Inno Setup");
                for (var index = 0; index <= bytes.Length - marker.Length; index++)
                {
                    var matched = true;
                    for (var offset = 0; offset < marker.Length; offset++)
                    {
                        if (bytes[index + offset] != marker[offset])
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (matched)
                    {
                        _logger?.Debug($"Detected Inno Setup installer signature in '{exePath}'.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to inspect '{exePath}' for Inno signature. {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Detects install type and falls back to temporary extraction when needed.
        /// </summary>
        /// <param name="archivePath">Archive path to inspect.</param>
        /// <param name="tempRoot">Temporary extraction root.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The detected install type and extracted path.</returns>
        public async Task<(InstallType InstallType, string ExtractedPath)> DetectWithExtractionFallbackAsync(
            string archivePath,
            string tempRoot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return (InstallType.Unknown, string.Empty);
            }

            var initial = DetectInstallType(archivePath, null);
            if (initial != InstallType.Unknown)
            {
                return (initial, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(tempRoot))
            {
                throw new ArgumentException("Temp root is required for fallback detection.", nameof(tempRoot));
            }

            Directory.CreateDirectory(tempRoot);
            var extracted = await _archiveService
                .ExtractAsync(archivePath, tempRoot, ExtractionBehavior.Direct, cancellationToken)
                .ConfigureAwait(false);
            var detected = DetectInstallType(archivePath, extracted);
            return (detected, extracted ?? string.Empty);
        }
    }
}
