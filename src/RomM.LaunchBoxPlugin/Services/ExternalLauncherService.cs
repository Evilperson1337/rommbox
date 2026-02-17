using System;
using System.Diagnostics;
using RomMbox.Services.Logging;

namespace RomMbox.Services
{
    /// <summary>
    /// Opens external URLs (for example, RomM play links) using the OS shell.
    /// </summary>
    internal sealed class ExternalLauncherService
    {
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates the launcher service.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        public ExternalLauncherService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Attempts to open a URL in the user's default browser.
        /// </summary>
        public bool TryOpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger?.Warning("Browser launch skipped: URL is empty.");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to open browser for URL.", ex);
                return false;
            }
        }
    }
}
