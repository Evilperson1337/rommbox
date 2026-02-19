using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Compression;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Services.Paths;

namespace RomMbox.Services
{
    /// <summary>
    /// Handles archive detection and extraction for ROM downloads.
    /// Uses managed ZIP support first, then falls back to 7-Zip when enabled.
    /// </summary>
    internal sealed class ArchiveService
    {
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly Lazy<PluginSettings> _settings;

        /// <summary>
        /// Creates the archive service with access to settings and logging.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="settingsManager">Settings provider for extraction preferences.</param>
        public ArchiveService(LoggingService logger, SettingsManager settingsManager)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _settings = new Lazy<PluginSettings>(() => _settingsManager?.Load() ?? new PluginSettings());
        }

        /// <summary>
        /// Returns true if the file extension is a supported archive type.
        /// </summary>
        public bool IsSupportedArchive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path) ?? string.Empty;
            return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets whether archives should be kept after extraction.
        /// </summary>
        public bool KeepArchivesAfterExtraction => _settings.Value.GetKeepArchivesAfterExtraction();

        /// <summary>
        /// Extracts an archive into a target directory based on the requested behavior.
        /// Returns the extraction folder or an empty string when extraction is skipped.
        /// </summary>
        public async Task<string> ExtractAsync(string archivePath, string targetDirectory, ExtractionBehavior behavior, CancellationToken cancellationToken, IProgress<RomMbox.Models.Download.DownloadProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                throw new ArgumentException("Archive path is required.", nameof(archivePath));
            }

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("Archive file not found.", archivePath);
            }

            if (!IsSupportedArchive(archivePath))
            {
                throw new NotSupportedException("Unsupported archive format.");
            }

            if (behavior == ExtractionBehavior.None)
            {
                _logger?.Info("Extraction disabled; returning archive path only.");
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("Target directory is required.", nameof(targetDirectory));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetDirectory);

            var destination = ResolveExtractionDirectory(archivePath, targetDirectory, behavior);
            Directory.CreateDirectory(destination);

            var extension = Path.GetExtension(archivePath) ?? string.Empty;
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Managed ZIP extraction avoids the 7-Zip dependency for .zip files.
                    _logger?.Info($"Starting managed ZIP extraction: {LoggingService.SanitizePath(archivePath)} -> {LoggingService.SanitizePath(destination)}");
                    await Task.Run(() =>
                    {
                        using var archive = ZipFile.OpenRead(archivePath);
                        var totalEntries = archive.Entries.Count;
                        var processed = 0;
                        progress?.Report(new RomMbox.Models.Download.DownloadProgress(0, Math.Max(1, totalEntries)));

                        foreach (var entry in archive.Entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var targetPath = Path.Combine(destination, entry.FullName);
                            if (string.IsNullOrWhiteSpace(entry.Name))
                            {
                                Directory.CreateDirectory(targetPath);
                            }
                            else
                            {
                                var targetDir = Path.GetDirectoryName(targetPath);
                                if (!string.IsNullOrWhiteSpace(targetDir))
                                {
                                    Directory.CreateDirectory(targetDir);
                                }
                                entry.ExtractToFile(targetPath, overwrite: true);
                            }

                            processed++;
                            progress?.Report(new RomMbox.Models.Download.DownloadProgress(processed, Math.Max(1, totalEntries)));
                        }

                        progress?.Report(new RomMbox.Models.Download.DownloadProgress(Math.Max(1, totalEntries), Math.Max(1, totalEntries)));
                    }, cancellationToken).ConfigureAwait(false);
                    _logger?.Info("Managed ZIP extraction completed successfully.");
                    return destination;
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Managed ZIP extraction failed, falling back to 7-Zip. {ex.Message}");
                }
            }

            if (!_settings.Value.GetUseSevenZipFallback())
            {
                _logger?.Warning("7-Zip extraction disabled. Skipping extraction.");
                return string.Empty;
            }

            var sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                _logger?.Error("7-Zip extraction enabled but 7z.exe not found.");
                throw new FileNotFoundException("7-Zip executable not found.");
            }

            _logger?.Info($"Starting 7-Zip extraction: {LoggingService.SanitizePath(archivePath)} -> {LoggingService.SanitizePath(destination)}");
            await Task.Run(() => ExtractWithSevenZip(sevenZipPath, archivePath, destination, cancellationToken, progress), cancellationToken)
                .ConfigureAwait(false);
            _logger?.Info("7-Zip extraction completed successfully.");
            return destination;
        }

        /// <summary>
        /// Attempts to infer install type from file name conventions and extracted contents.
        /// </summary>
        public InstallType DetectInstallType(string archivePath, string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                _logger?.Debug("Install type detection skipped: archive path missing.");
                return InstallType.Unknown;
            }

            var fileName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
            if (fileName.IndexOf("(installer)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Debug($"Install type detection matched installer marker for '{fileName}'.");
                return InstallType.Installer;
            }

            if (fileName.IndexOf("(portable)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Debug($"Install type detection matched portable marker for '{fileName}'.");
                return InstallType.Portable;
            }

            if (!string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath))
            {
                var setupExe = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(setupExe))
                {
                    _logger?.Debug($"Install type detection found setup.exe at '{setupExe}'.");
                    return InstallType.Installer;
                }

                _logger?.Debug($"Install type detection defaulted to portable for '{archivePath}'.");
                return InstallType.Portable;
            }

            _logger?.Debug($"Install type detection returned unknown for '{archivePath}'.");
            return InstallType.Unknown;
        }

        /// <summary>
        /// Determines the extraction directory based on behavior (direct vs. subfolder).
        /// </summary>
        private static string ResolveExtractionDirectory(string archivePath, string targetDirectory, ExtractionBehavior behavior)
        {
            if (behavior == ExtractionBehavior.Direct)
            {
                return targetDirectory;
            }

            var baseName = Path.GetFileNameWithoutExtension(archivePath) ?? "Archive";
            return Path.Combine(targetDirectory, SanitizePathSegment(baseName));
        }

        /// <summary>
        /// Produces a filesystem-safe directory name segment.
        /// </summary>
        private static string SanitizePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(invalid.Contains(ch) ? '_' : ch);
            }

            var result = builder.ToString();
            return string.IsNullOrWhiteSpace(result) ? "Archive" : result;
        }

        /// <summary>
        /// Executes 7-Zip to extract non-zip archives or when managed extraction fails.
        /// </summary>
        private void ExtractWithSevenZip(string sevenZipPath, string archivePath, string destination, CancellationToken cancellationToken, IProgress<RomMbox.Models.Download.DownloadProgress> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x \"{archivePath}\" -o\"{destination}\" -y",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start 7-Zip process.");
                }

                cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                    }
                });

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger?.Error($"7-Zip extraction failed. ExitCode={process.ExitCode}. Output={output}. Error={error}");
                    throw new InvalidOperationException("7-Zip extraction failed.");
                }
            }
        }

        /// <summary>
        /// Resolves a usable 7z.exe path from settings, LaunchBox bundle, or system install.
        /// </summary>
        private string ResolveSevenZipPath()
        {
            var configured = _settings.Value.GetSevenZipPath();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            if (!string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                var bundled = Path.Combine(launchBoxRoot, "ThirdParty", "7-Zip", "7z.exe");
                if (File.Exists(bundled))
                {
                    return bundled;
                }
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var candidates = new List<string>
            {
                Path.Combine(programFiles, "7-Zip", "7z.exe"),
                Path.Combine(programFilesX86, "7-Zip", "7z.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Logs a note about archive retention behavior for the current version.
        /// </summary>
        public void LogArchiveRetentionNote()
        {
            var settings = _settings.Value;
            _logger?.Info(settings.GetKeepArchivesAfterExtraction()
                ? "Archive retention enabled; archives will be kept after extraction."
                : "Archive retention disabled; archives will be removed after extraction.");
        }

        /// <summary>
        /// Attempts to delete a downloaded archive once extraction is complete.
        /// </summary>
        public bool TryDeleteArchive(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return false;
            }

            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                    _logger?.Info($"Archive removed after extraction: '{LoggingService.SanitizePath(archivePath)}'.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to delete archive '{LoggingService.SanitizePath(archivePath)}': {ex.Message}");
            }

            return false;
        }
    }
}
