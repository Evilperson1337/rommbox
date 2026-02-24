using System;
using RomMbox.Services.Logging;

namespace RomMbox.Services
{
    /// <summary>
    /// Builds URLs for viewing RomM ROM detail pages in a browser.
    /// </summary>
    internal sealed class RommViewUrlService
    {
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates the URL builder service.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        public RommViewUrlService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Constructs the RomM view URL for a given server and ROM id.
        /// </summary>
        public string BuildViewUrl(string serverUrl, string romId)
        {
            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(romId))
            {
                _logger?.Warning("View URL cannot be built: missing server URL or rom ID.");
                return string.Empty;
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var baseUri))
            {
                _logger?.Warning("View URL cannot be built: invalid server URL.");
                return string.Empty;
            }

            var normalized = baseUri.ToString().TrimEnd('/');
            return $"{normalized}/rom/{romId}";
        }
    }
}
