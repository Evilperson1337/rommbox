using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Auth;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Utilities;

namespace RomMbox.Plugin
{
    /// <summary>
    /// Central initialization point for the plugin. Provides shared services and
    /// kicks off background connection checks.
    /// </summary>
    internal static class PluginEntry
    {
        private static readonly object SyncRoot = new object();
        private static readonly object ConnectionSyncRoot = new object();
        private static bool _initialized;
        private static SettingsManager _settingsManager;
        private static InstallStateService _installStateService;
        private static bool _backgroundConnectionStarted;
        private static ConnectionTestResult _backgroundConnectionResult;

        public static LoggingService Logger { get; private set; }
        public static PluginSettings Settings { get; private set; }
        public static SettingsManager SettingsManager => _settingsManager;
        public static InstallStateService InstallStateService => _installStateService;
        public static event EventHandler<ConnectionTestResult> BackgroundConnectionCompleted;

        /// <summary>
        /// Initializes logging, settings, and shared services. Safe to call multiple times.
        /// </summary>
        public static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var sink = FileLogSink.CreateDefault();
                    sink.EnsureInitialized();
                    var settingsManager = new SettingsManager(new LoggingService(LogLevel.Debug, sink));
                    var settings = settingsManager.Load();
                    var logger = new LoggingService(settings.GetLogLevel(), sink);

                    var operationId = Guid.NewGuid().ToString("N");
                    using (logger.BeginOperation(operationId))
                    {
                        logger.Write(LogLevel.Info, "Plugin initialization started.", null, "Subsystem", "PluginLifecycle", "Operation", "Initialize", "OperationId", operationId);
                        logger.Write(LogLevel.Info, "Settings loaded.", null, "LogLevel", settings.LogLevelName, "Subsystem", "Config", "Operation", "LoadSettings");
                    }

                    Settings = settings;
                    Logger = logger;
                    _settingsManager = settingsManager;
                    _installStateService = new InstallStateService(logger, settingsManager);
                    _ = new StubApplicationPathService(logger, _installStateService)
                        .EnsureStubApplicationPathsAsync(System.Threading.CancellationToken.None);
                    // Start a background connection test so UI can reflect status quickly.
                    TryStartBackgroundConnection(logger, settingsManager, settings);
                    _initialized = true;

                    using (logger.BeginOperation(operationId))
                    {
                        logger.Write(LogLevel.Info, "Plugin initialization completed.", null, "Subsystem", "PluginLifecycle", "Operation", "Initialize", "Result", "Success");
                    }
                }
                catch (Exception ex)
                {
                    var fallbackLogger = new LoggingService(LogLevel.Error, FileLogSink.CreateDefault());
                    fallbackLogger.Critical("Initialization failed.", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Ensures initialization is complete before accessing shared services.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Initialize();
        }

        /// <summary>
        /// Factory for a fully wired import service.
        /// </summary>
        public static ImportService CreateImportService()
        {
            EnsureInitialized();
            var settingsManager = _settingsManager ?? new SettingsManager(Logger);
            var client = new RommClient(Logger, settingsManager);
            var mappingService = new PlatformMappingService(Logger, settingsManager, client);
            return new ImportService(Logger, settingsManager, mappingService, client);
        }

        /// <summary>
        /// Factory for a fully wired download service.
        /// </summary>
        public static DownloadService CreateDownloadService()
        {
            EnsureInitialized();
            var settingsManager = _settingsManager ?? new SettingsManager(Logger);
            var client = new RommClient(Logger, settingsManager);
            var archiveService = new ArchiveService(Logger, settingsManager);
            return new DownloadService(Logger, client, archiveService, settingsManager);
        }

        /// <summary>
        /// Returns the cached background connection result, if available.
        /// </summary>
        public static bool TryGetBackgroundConnectionResult(out ConnectionTestResult result)
        {
            lock (ConnectionSyncRoot)
            {
                result = _backgroundConnectionResult;
                return _backgroundConnectionResult != null;
            }
        }

        /// <summary>
        /// Starts a one-time background connection test when credentials are present.
        /// </summary>
        private static void TryStartBackgroundConnection(LoggingService logger, SettingsManager settingsManager, PluginSettings settings)
        {
            if (_backgroundConnectionStarted)
            {
                return;
            }

            if (!settings.UseSavedCredentials || !settings.HasSavedCredentials)
            {
                return;
            }

            var serverUrl = settings.ServerUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                return;
            }

            var credentials = settingsManager.GetSavedCredentials(serverUrl);
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            {
                return;
            }

            _backgroundConnectionStarted = true;
            var operationId = Guid.NewGuid().ToString("N");
            Task.Run(async () =>
            {
                ConnectionTestResult result;
                try
                {
                    using (logger.BeginOperation(operationId))
                    {
                        logger.Write(LogLevel.Info, "Background connection test started.", null, "Subsystem", "Integration", "Operation", "ConnectionTest", "OperationId", operationId);
                    }
                    var timeout = TimeSpan.FromSeconds(Math.Max(5, settings.ConnectionTimeoutSeconds));
                    var authService = new AuthService(logger);
                    result = await authService.TestConnectionAsync(serverUrl, credentials.Username, credentials.Password, timeout, settings.AllowInvalidTls, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    using (logger.BeginOperation(operationId))
                    {
                        logger.Write(LogLevel.Error, "Background connection failed.", ex, "Subsystem", "Integration", "Operation", "ConnectionTest", "Result", "Failure");
                    }
                    result = new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection failed.");
                }

                lock (ConnectionSyncRoot)
                {
                    _backgroundConnectionResult = result;
                }

                using (logger.BeginOperation(operationId))
                {
                    logger.Write(LogLevel.Info, "Background connection test completed.", null, "Subsystem", "Integration", "Operation", "ConnectionTest", "Result", result?.Status.ToString() ?? "Unknown");
                }
                BackgroundConnectionCompleted?.Invoke(null, result);
            });
        }
    }
}
