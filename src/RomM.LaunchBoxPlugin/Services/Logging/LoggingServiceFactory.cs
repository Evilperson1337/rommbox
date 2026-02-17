using RomMbox.Services.Settings;

namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Creates configured instances of <see cref="LoggingService"/>.
    /// </summary>
    internal static class LoggingServiceFactory
    {
        /// <summary>
        /// Creates a logging service using settings from the plugin configuration.
        /// </summary>
        /// <returns>A configured logging service instance.</returns>
        public static LoggingService Create()
        {
            var sink = FileLogSink.CreateDefault();
            sink.EnsureInitialized();

            var bootstrapLogger = new LoggingService(LogLevel.Warning, sink);
            var settingsManager = new SettingsManager(bootstrapLogger);
            var settings = settingsManager.Load();

            return new LoggingService(settings.GetLogLevel(), sink);
        }
    }
}
