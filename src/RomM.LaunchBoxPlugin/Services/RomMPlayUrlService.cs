using System;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;

namespace RomMbox.Services
{
    /// <summary>
    /// Builds URLs for launching RomM web playback in a browser.
    /// </summary>
    internal sealed class RomMPlayUrlService
    {
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates the URL builder service.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        public RomMPlayUrlService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Constructs the RomM play URL for a given server and ROM id.
        /// </summary>
        public string BuildPlayUrl(string serverUrl, string romId)
        {
            return BuildPlayUrl(serverUrl, romId, new RommPlayEndpointProfile
            {
                IsPlayableOnRomM = true,
                PathSuffix = "ejs"
            });
        }

        public string BuildPlayUrl(string serverUrl, string romId, string platformName)
        {
            return BuildPlayUrl(serverUrl, romId, RommPlayEndpointResolver.Resolve(platformName));
        }

        public string BuildPlayUrl(
            string serverUrl,
            string romId,
            string rommPlatformId,
            string rommPlatformName,
            SettingsManager settingsManager,
            PlatformInstallerRegistry registry)
        {
            return BuildPlayUrl(serverUrl, romId, RommPlayEndpointResolver.Resolve(rommPlatformId, rommPlatformName, settingsManager, registry, _logger));
        }

        private string BuildPlayUrl(string serverUrl, string romId, RommPlayEndpointProfile profile)
        {
            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(romId))
            {
                _logger?.Warning("Play URL cannot be built: missing server URL or rom ID.");
                return string.Empty;
            }

            if (profile == null || !profile.IsPlayableOnRomM)
            {
                _logger?.Warning("Play URL cannot be built: platform is not playable on RomM.");
                return string.Empty;
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var baseUri))
            {
                _logger?.Warning("Play URL cannot be built: invalid server URL.");
                return string.Empty;
            }

            var normalized = baseUri.ToString().TrimEnd('/');
            var path = profile.BuildPath(romId);
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger?.Warning("Play URL cannot be built: platform endpoint profile did not produce a path.");
                return string.Empty;
            }

            return $"{normalized}{path}";
        }
    }
}
