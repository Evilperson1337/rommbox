using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    /// <summary>
    /// Manages optional stub ApplicationPath entries for RomM games.
    /// Currently disabled to keep RomM games launching via RomM instead of locally.
    /// </summary>
    internal sealed class StubApplicationPathService
    {
        private readonly LoggingService _logger;
        private readonly InstallStateService _installStateService;

        /// <summary>
        /// Creates the stub path service.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="installStateService">Install state helper for RomM games.</param>
        public StubApplicationPathService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
        }

        /// <summary>
        /// Scans RomM games and (if enabled) ensures stub paths are present.
        /// This is currently a no-op by design.
        /// </summary>
        public async Task EnsureStubApplicationPathsAsync(CancellationToken cancellationToken)
        {
            var dataManager = PluginHelper.DataManager;
            if (dataManager == null)
            {
                _logger?.Warning("Stub path scan skipped: DataManager unavailable.");
                return;
            }

            var games = dataManager.GetAllGames() ?? Array.Empty<IGame>();
            var updated = 0;
            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (game == null || !_installStateService.IsRomMSourcedGame(game))
                {
                    continue;
                }

                // Skip creating stub files for RomM games - they should have empty ApplicationPath
                // and be played through the RomM interface instead of locally.
                _logger?.Debug($"Skipping stub creation for RomM game '{game.Title}' - ApplicationPath will remain empty for RomM interface.");
                continue;

                // The following code is now disabled for RomM games:
                /*
                if (!string.IsNullOrWhiteSpace(game.ApplicationPath))
                {
                    continue;
                }

                if (!HasEmulatorAssigned(dataManager, game.Platform))
                {
                    _logger?.Warning($"Stub ApplicationPath skipped for '{game.Title}': no emulator configured for platform '{game.Platform ?? "Unknown"}'.");
                    continue;
                }

                var stubPath = BuildStubPath(game);
                if (string.IsNullOrWhiteSpace(stubPath))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stubPath) ?? string.Empty);
                    if (!File.Exists(stubPath))
                    {
                        File.WriteAllText(stubPath, string.Empty);
                    }
                    game.ApplicationPath = stubPath;
                    updated++;
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to create stub path for '{game.Title}': {ex.Message}");
                }
                */
            }

            if (updated > 0)
            {
                _logger?.Info($"Updated stub ApplicationPath for {updated} RomM game(s).");
                dataManager.Save(true);
                dataManager.ReloadIfNeeded();
                dataManager.ForceReload();
            }
        }

        /// <summary>
        /// Attempts to create a stub ApplicationPath for a single game.
        /// Currently returns false because stub creation is disabled.
        /// </summary>
        public bool TryCreateStubForGame(IDataManager dataManager, IGame game)
        {
            if (dataManager == null || game == null)
            {
                return false;
            }

            if (!_installStateService.IsRomMSourcedGame(game))
            {
                return false;
            }

            // Skip creating stub files for RomM games - they should have empty ApplicationPath
            // and be played through the RomM interface instead of locally.
            _logger?.Debug($"Skipping stub creation for RomM game '{game.Title}' - ApplicationPath will remain empty for RomM interface.");
            return false;

            // The following code is now disabled for RomM games:
            /*
            if (!string.IsNullOrWhiteSpace(game.ApplicationPath))
            {
                return false;
            }

            if (!HasEmulatorAssigned(dataManager, game.Platform))
            {
                _logger?.Warning($"Stub ApplicationPath skipped for '{game.Title}': no emulator configured for platform '{game.Platform ?? "Unknown"}'.");
                return false;
            }

            var stubPath = BuildStubPath(game);
            if (string.IsNullOrWhiteSpace(stubPath))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stubPath) ?? string.Empty);
                if (!File.Exists(stubPath))
                {
                    File.WriteAllText(stubPath, string.Empty);
                }
                game.ApplicationPath = stubPath;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to create stub path for '{game.Title}': {ex.Message}");
                return false;
            }
            */
        }

        /// <summary>
        /// Builds a stub file path for a game based on title/platform metadata.
        /// </summary>
        private string BuildStubPath(IGame game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            var fileName = "romm.stub";

            var normalizedTitle = NormalizePathSegment(game.Title);
            var normalizedPlatform = NormalizePathSegment(game.Platform);
            var launchBoxRoot = Paths.PluginPaths.GetLaunchBoxRootDirectory();
            var gamesRoot = Path.Combine(launchBoxRoot, "Games", normalizedPlatform, normalizedTitle);
            return Path.Combine(gamesRoot, SanitizeFileName(fileName));
        }

        /// <summary>
        /// Checks whether any emulator is assigned to a platform.
        /// </summary>
        private static bool HasEmulatorAssigned(IDataManager dataManager, string platformName)
        {
            if (dataManager == null || string.IsNullOrWhiteSpace(platformName))
            {
                return false;
            }

            var emulators = dataManager.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Normalizes a path segment to be filesystem-safe.
        /// </summary>
        private static string NormalizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned.Trim();
        }

        /// <summary>
        /// Sanitizes a file name, falling back to a default stub name.
        /// </summary>
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "romm.stub";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "romm.stub" : cleaned.Trim();
        }

        /// <summary>
        /// Ensures the extension begins with a dot.
        /// </summary>
        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }
    }
}

