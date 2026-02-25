using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RomMbox.Models;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    /// <summary>
    /// Stores and validates local install state for RomM-sourced games.
    /// Backed by a lightweight SQLite database in the plugin data folder.
    /// </summary>
    internal sealed class InstallStateService
    {
        private const string DatabaseFileName = "romm.db";
        private const string LegacyDatabaseFileName = "romm_install_state.db";
        private const string MigrationMarkerKey = "romm_identity_backfill_v1";
        private readonly LoggingService _logger;
        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _initSync = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _syncWaitWarningThreshold = TimeSpan.FromMilliseconds(500);
        private readonly TimeSpan _syncWaitTimeout = TimeSpan.FromSeconds(10);
        private readonly string _databasePath;
        private bool _initialized;

        /// <summary>
        /// Creates the install state service.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="settingsManager">Settings provider for server URL resolution.</param>
        public InstallStateService(LoggingService logger, SettingsManager settingsManager)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _databasePath = Path.Combine(PluginPaths.GetPluginDataDirectory(), DatabaseFileName);
        }

        private readonly SettingsManager _settingsManager;

        /// <summary>
        /// Initializes the SQLite schema and applies any missing column migrations.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (!await WaitForSyncAsync(cancellationToken, "InitializeAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                EnsureDatabaseFileNameMigration();
                Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? string.Empty);
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS InstallState (
    LaunchBoxGameId TEXT PRIMARY KEY,
    RommRomId TEXT,
    RommPlatformId TEXT,
    ServerUrl TEXT,
    RemoteMd5 TEXT,
    LocalMd5 TEXT,
    WindowsInstallType TEXT,
    InstalledPath TEXT,
    ArchivePath TEXT,
    InstallRootPath TEXT,
    IsInstalled INTEGER NOT NULL,
    InstalledUtc TEXT,
    LastValidatedUtc TEXT,
    InstallStatus TEXT,
    InstallPhase TEXT,
    LastError TEXT,
    LastAttemptUtc TEXT,
    LastCompletedUtc TEXT,
    LastOperationId TEXT,
    RommAdditionalAppId TEXT,
    RommMergedBaseGameId TEXT,
    RommLaunchPath TEXT,
    RommLaunchArgs TEXT,
    RommAdditionalAppSyncedUtc TEXT
);
CREATE INDEX IF NOT EXISTS IX_InstallState_RommRomId ON InstallState (RommRomId);
CREATE INDEX IF NOT EXISTS IX_InstallState_Platform ON InstallState (RommPlatformId);
CREATE TABLE IF NOT EXISTS InstallStateMetadata (
    Key TEXT PRIMARY KEY,
    Value TEXT
);
 CREATE TABLE IF NOT EXISTS PlatformMappings (
     RommPlatformId TEXT PRIMARY KEY,
     RommPlatformName TEXT,
     LaunchBoxPlatformName TEXT,
     AutoMapped INTEGER NOT NULL,
     DisableAutoImport INTEGER NOT NULL,
     ExtractAfterDownload INTEGER NOT NULL,
     ExtractionBehavior TEXT,
     InstallerMode TEXT,
     MusicRootPath TEXT,
     InstallOst INTEGER NOT NULL,
     BonusRootPath TEXT,
     InstallBonus INTEGER NOT NULL,
     PreReqsRootPath TEXT,
     InstallPreReqs INTEGER NOT NULL,
     CustomInstallDirectory TEXT,
     InstallScenario TEXT,
     TargetImportFile TEXT,
     InstallerSilentArgs TEXT,
     SelfContained INTEGER NOT NULL,
     AssociatedEmulatorId TEXT,
     OstInstallLocation TEXT,
     BonusInstallLocation TEXT
 );
CREATE TABLE IF NOT EXISTS PlatformMappingAliases (
    AliasId TEXT PRIMARY KEY,
    Alias TEXT,
    LaunchBoxPlatformName TEXT
);
CREATE TABLE IF NOT EXISTS ExcludedRommPlatforms (
    RommPlatformId TEXT PRIMARY KEY
);
";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                var migrateCommand = connection.CreateCommand();
                migrateCommand.CommandText = "PRAGMA table_info(InstallState);";
                var hasInstallRoot = false;
                var hasRemoteMd5 = false;
                var hasLocalMd5 = false;
                var hasWindowsInstallType = false;
                var hasServerUrl = false;
                var hasInstallStatus = false;
                var hasInstallPhase = false;
                var hasLastError = false;
                var hasLastAttemptUtc = false;
                var hasLastCompletedUtc = false;
                var hasLastOperationId = false;
                var hasRommAdditionalAppId = false;
                var hasRommMergedBaseGameId = false;
                var hasRommLaunchPath = false;
                var hasRommLaunchArgs = false;
                var hasRommAdditionalAppSyncedUtc = false;
                using (var reader = await migrateCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var columnName = reader.GetString(1);
                        if (string.Equals(columnName, "InstallRootPath", StringComparison.OrdinalIgnoreCase))
                        {
                            hasInstallRoot = true;
                            continue;
                        }

                        if (string.Equals(columnName, "RemoteMd5", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRemoteMd5 = true;
                        }

                        if (string.Equals(columnName, "LocalMd5", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLocalMd5 = true;
                        }

                        if (string.Equals(columnName, "WindowsInstallType", StringComparison.OrdinalIgnoreCase))
                        {
                            hasWindowsInstallType = true;
                        }

                        if (string.Equals(columnName, "ServerUrl", StringComparison.OrdinalIgnoreCase))
                        {
                            hasServerUrl = true;
                        }

                        if (string.Equals(columnName, "InstallStatus", StringComparison.OrdinalIgnoreCase))
                        {
                            hasInstallStatus = true;
                        }

                        if (string.Equals(columnName, "InstallPhase", StringComparison.OrdinalIgnoreCase))
                        {
                            hasInstallPhase = true;
                        }

                        if (string.Equals(columnName, "LastError", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLastError = true;
                        }

                        if (string.Equals(columnName, "LastAttemptUtc", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLastAttemptUtc = true;
                        }

                        if (string.Equals(columnName, "LastCompletedUtc", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLastCompletedUtc = true;
                        }

                        if (string.Equals(columnName, "LastOperationId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLastOperationId = true;
                        }

                        if (string.Equals(columnName, "RommAdditionalAppId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRommAdditionalAppId = true;
                        }

                        if (string.Equals(columnName, "RommMergedBaseGameId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRommMergedBaseGameId = true;
                        }

                        if (string.Equals(columnName, "RommLaunchPath", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRommLaunchPath = true;
                        }

                        if (string.Equals(columnName, "RommLaunchArgs", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRommLaunchArgs = true;
                        }

                        if (string.Equals(columnName, "RommAdditionalAppSyncedUtc", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRommAdditionalAppSyncedUtc = true;
                        }
                    }
                }

                if (!hasInstallRoot)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN InstallRootPath TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRemoteMd5)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RemoteMd5 TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasLocalMd5)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN LocalMd5 TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasWindowsInstallType)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN WindowsInstallType TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasServerUrl)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN ServerUrl TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasInstallStatus)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN InstallStatus TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasInstallPhase)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN InstallPhase TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasLastError)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN LastError TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasLastAttemptUtc)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN LastAttemptUtc TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasLastCompletedUtc)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN LastCompletedUtc TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasLastOperationId)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN LastOperationId TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRommAdditionalAppId)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RommAdditionalAppId TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRommMergedBaseGameId)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RommMergedBaseGameId TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRommLaunchPath)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RommLaunchPath TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRommLaunchArgs)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RommLaunchArgs TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!hasRommAdditionalAppSyncedUtc)
                {
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE InstallState ADD COLUMN RommAdditionalAppSyncedUtc TEXT";
                    await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await EnsurePlatformMappingsSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
                _logger?.Info("InstallState database initialized.");
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to initialize InstallState database.", ex);
                throw;
            }
            finally
            {
                ReleaseSync("InitializeAsync");
            }
        }

    /// <summary>
    /// Ensures initialization is performed once in a thread-safe manner.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
            if (_initialized)
            {
                return;
            }

            await _initSync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return;
                }

                await InitializeAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
            finally
            {
                _initSync.Release();
            }
        }

        /// <summary>
        /// Attempts to recover from stale install state markers (InProgress/Cancelled) on startup.
        /// </summary>
        public async Task RecoverStaleOperationsAsync(TimeSpan? staleThreshold, CancellationToken cancellationToken)
        {
            var threshold = staleThreshold ?? TimeSpan.FromHours(2);
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "RecoverStaleOperationsAsync").ConfigureAwait(false))
            {
                return;
            }

            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var select = connection.CreateCommand();
                select.CommandText = @"
SELECT LaunchBoxGameId, InstallStatus, InstallPhase, LastAttemptUtc, LastError
FROM InstallState
WHERE InstallStatus IN ('InProgress', 'Cancelled') OR InstallPhase IN ('Pending', 'Downloading', 'Extracting', 'Installing', 'Cancelled');
";

                var toUpdate = new List<(string GameId, string InstallStatus, string InstallPhase, DateTimeOffset? LastAttemptUtc)>();
                using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var gameId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        var status = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var phase = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var lastAttempt = reader.IsDBNull(3) ? (DateTimeOffset?)null : FromDbTimestamp(reader.GetValue(3));
                        if (string.IsNullOrWhiteSpace(gameId))
                        {
                            continue;
                        }

                        if (!lastAttempt.HasValue || DateTimeOffset.UtcNow - lastAttempt.Value >= threshold)
                        {
                            toUpdate.Add((gameId, status, phase, lastAttempt));
                        }
                    }
                }

                foreach (var entry in toUpdate)
                {
                    var update = connection.CreateCommand();
                    update.CommandText = @"
UPDATE InstallState
SET InstallStatus = 'Failed',
    InstallPhase = 'Failed',
    LastError = 'Recovered from stale operation',
    LastValidatedUtc = $now
WHERE LaunchBoxGameId = $id;
";
                    update.Parameters.AddWithValue("$id", entry.GameId);
                    update.Parameters.AddWithValue("$now", ToDbTimestamp(DateTimeOffset.UtcNow));
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    _logger?.Warning($"Recovered stale install state. GameId={entry.GameId}, PrevStatus={entry.InstallStatus}, PrevPhase={entry.InstallPhase}.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"InstallState recovery failed. {ex.Message}");
            }
            finally
            {
                ReleaseSync("RecoverStaleOperationsAsync");
            }
        }

        /// <summary>
        /// Retrieves install state for a specific LaunchBox game id.
        /// </summary>
        public async Task<InstallState> GetStateAsync(string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return null;
            }

            var initStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            initStopwatch.Stop();
            if (initStopwatch.ElapsedMilliseconds > 100)
            {
                _logger?.Warning($"InstallState initialization slow. DurationMs={initStopwatch.ElapsedMilliseconds}, GameId={launchBoxGameId}.");
            }
            var waitStopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!await WaitForSyncAsync(cancellationToken, "GetStateAsync").ConfigureAwait(false))
            {
                return null;
            }
            waitStopwatch.Stop();
            if (waitStopwatch.ElapsedMilliseconds > 50)
            {
                _logger?.Warning($"InstallState lock wait slow. WaitMs={waitStopwatch.ElapsedMilliseconds}, GameId={launchBoxGameId}.");
            }
            try
            {
                var queryStopwatch = System.Diagnostics.Stopwatch.StartNew();
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
SELECT LaunchBoxGameId, RommRomId, RommPlatformId, ServerUrl, RemoteMd5, LocalMd5, WindowsInstallType, InstalledPath, ArchivePath, InstallRootPath, IsInstalled, InstalledUtc, LastValidatedUtc,
       InstallStatus, InstallPhase, LastError, LastAttemptUtc, LastCompletedUtc, LastOperationId,
       RommAdditionalAppId, RommMergedBaseGameId, RommLaunchPath, RommLaunchArgs, RommAdditionalAppSyncedUtc
FROM InstallState
WHERE LaunchBoxGameId = $id;
";
                command.Parameters.AddWithValue("$id", launchBoxGameId);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    queryStopwatch.Stop();
                    if (queryStopwatch.ElapsedMilliseconds > 100)
                    {
                        _logger?.Warning($"InstallState query slow (no result). DurationMs={queryStopwatch.ElapsedMilliseconds}, GameId={launchBoxGameId}.");
                    }
                    return null;
                }

                var state = MapState(reader);
                queryStopwatch.Stop();
                if (queryStopwatch.ElapsedMilliseconds > 100)
                {
                    _logger?.Warning($"InstallState query slow. DurationMs={queryStopwatch.ElapsedMilliseconds}, GameId={launchBoxGameId}.");
                }
                return state;
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to read install state.", ex);
                return null;
            }
            finally
            {
                ReleaseSync("GetStateAsync");
            }
        }

        /// <summary>
        /// Retrieves all install states for a RomM platform id.
        /// </summary>
        public async Task<IReadOnlyList<InstallState>> GetStatesByPlatformAsync(string rommPlatformId, CancellationToken cancellationToken)
        {
            var results = new List<InstallState>();
            if (string.IsNullOrWhiteSpace(rommPlatformId))
            {
                return results;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "GetStatesByPlatformAsync").ConfigureAwait(false))
            {
                return results;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
SELECT LaunchBoxGameId, RommRomId, RommPlatformId, ServerUrl, RemoteMd5, LocalMd5, WindowsInstallType, InstalledPath, ArchivePath, InstallRootPath, IsInstalled, InstalledUtc, LastValidatedUtc,
       InstallStatus, InstallPhase, LastError, LastAttemptUtc, LastCompletedUtc, LastOperationId,
       RommAdditionalAppId, RommMergedBaseGameId, RommLaunchPath, RommLaunchArgs, RommAdditionalAppSyncedUtc
FROM InstallState
WHERE RommPlatformId = $platform;
";
                command.Parameters.AddWithValue("$platform", rommPlatformId);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(MapState(reader));
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to read install states by platform.", ex);
                return results;
            }
            finally
            {
                ReleaseSync("GetStatesByPlatformAsync");
            }
        }

        /// <summary>
        /// Validates whether a RomM-sourced game is still installed locally.
        /// Updates persisted state if validation changes.
        /// </summary>
        public async Task<bool> IsGameInstalledAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return false;
            }

            if (!IsRomMSourcedGame(game))
            {
                return false;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var state = await GetStateAsync(game.Id, cancellationToken).ConfigureAwait(false);
            if (state == null)
            {
                return false;
            }

            var validated = ValidateInstallState(state);
            if (validated)
            {
                await UpsertStateAsync(state, cancellationToken).ConfigureAwait(false);
            }

            return state.IsInstalled;
        }

        /// <summary>
        /// Attempts to rename the install state database from a legacy name when needed.
        /// </summary>
        public void EnsureDatabaseFileNameMigration()
        {
            try
            {
                var directory = PluginPaths.GetPluginDataDirectory();
                var legacyPath = Path.Combine(directory, LegacyDatabaseFileName);
                if (string.Equals(legacyPath, _databasePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (File.Exists(legacyPath) && !File.Exists(_databasePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? string.Empty);
                    File.Copy(legacyPath, _databasePath, overwrite: false);
                    _logger?.Info($"Migrated install state DB from '{legacyPath}' to '{_databasePath}'.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to migrate install state DB file name. {ex.Message}");
            }
        }


        /// <summary>
        /// Inserts or updates a single install state record.
        /// </summary>
        public async Task UpsertStateAsync(InstallState state, CancellationToken cancellationToken)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.LaunchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpsertStateAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO InstallState (
    LaunchBoxGameId, RommRomId, RommPlatformId, ServerUrl, RemoteMd5, LocalMd5, WindowsInstallType, InstalledPath, ArchivePath, InstallRootPath, IsInstalled, InstalledUtc, LastValidatedUtc,
    InstallStatus, InstallPhase, LastError, LastAttemptUtc, LastCompletedUtc, LastOperationId,
    RommAdditionalAppId, RommMergedBaseGameId, RommLaunchPath, RommLaunchArgs, RommAdditionalAppSyncedUtc
) VALUES (
    $gameId, $romId, $platformId, $serverUrl, $remoteMd5, $localMd5, $windowsInstallType, $installedPath, $archivePath, $installRootPath, $isInstalled, $installedUtc, $lastValidatedUtc,
    $installStatus, $installPhase, $lastError, $lastAttemptUtc, $lastCompletedUtc, $lastOperationId,
    $rommAdditionalAppId, $rommMergedBaseGameId, $rommLaunchPath, $rommLaunchArgs, $rommAdditionalAppSyncedUtc
)
ON CONFLICT(LaunchBoxGameId) DO UPDATE SET
    RommRomId = excluded.RommRomId,
    RommPlatformId = excluded.RommPlatformId,
    ServerUrl = excluded.ServerUrl,
    RemoteMd5 = excluded.RemoteMd5,
    LocalMd5 = excluded.LocalMd5,
    WindowsInstallType = excluded.WindowsInstallType,
    InstalledPath = excluded.InstalledPath,
    ArchivePath = excluded.ArchivePath,
    InstallRootPath = excluded.InstallRootPath,
    IsInstalled = excluded.IsInstalled,
    InstalledUtc = excluded.InstalledUtc,
    LastValidatedUtc = excluded.LastValidatedUtc,
    InstallStatus = excluded.InstallStatus,
    InstallPhase = excluded.InstallPhase,
    LastError = excluded.LastError,
    LastAttemptUtc = excluded.LastAttemptUtc,
    LastCompletedUtc = excluded.LastCompletedUtc,
    LastOperationId = excluded.LastOperationId,
    RommAdditionalAppId = excluded.RommAdditionalAppId,
    RommMergedBaseGameId = excluded.RommMergedBaseGameId,
    RommLaunchPath = excluded.RommLaunchPath,
    RommLaunchArgs = excluded.RommLaunchArgs,
    RommAdditionalAppSyncedUtc = excluded.RommAdditionalAppSyncedUtc;
";
                command.Parameters.AddWithValue("$gameId", state.LaunchBoxGameId);
                command.Parameters.AddWithValue("$romId", state.RommRomId ?? string.Empty);
                command.Parameters.AddWithValue("$platformId", state.RommPlatformId ?? string.Empty);
                command.Parameters.AddWithValue("$serverUrl", state.ServerUrl ?? string.Empty);
                command.Parameters.AddWithValue("$remoteMd5", state.RemoteMd5 ?? string.Empty);
                command.Parameters.AddWithValue("$localMd5", state.LocalMd5 ?? string.Empty);
                command.Parameters.AddWithValue("$windowsInstallType", state.WindowsInstallType ?? string.Empty);
                command.Parameters.AddWithValue("$installedPath", state.InstalledPath ?? string.Empty);
                command.Parameters.AddWithValue("$archivePath", state.ArchivePath ?? string.Empty);
                command.Parameters.AddWithValue("$installRootPath", state.InstallRootPath ?? string.Empty);
                command.Parameters.AddWithValue("$isInstalled", state.IsInstalled ? 1 : 0);
                command.Parameters.AddWithValue("$installedUtc", ToDbTimestamp(state.InstalledUtc));
                command.Parameters.AddWithValue("$lastValidatedUtc", ToDbTimestamp(state.LastValidatedUtc));
                command.Parameters.AddWithValue("$installStatus", state.InstallStatus ?? string.Empty);
                command.Parameters.AddWithValue("$installPhase", state.InstallPhase ?? string.Empty);
                command.Parameters.AddWithValue("$lastError", state.LastError ?? string.Empty);
                command.Parameters.AddWithValue("$lastAttemptUtc", ToDbTimestamp(state.LastAttemptUtc));
                command.Parameters.AddWithValue("$lastCompletedUtc", ToDbTimestamp(state.LastCompletedUtc));
                command.Parameters.AddWithValue("$lastOperationId", state.LastOperationId ?? string.Empty);
                command.Parameters.AddWithValue("$rommAdditionalAppId", state.RommAdditionalAppId ?? string.Empty);
                command.Parameters.AddWithValue("$rommMergedBaseGameId", state.RommMergedBaseGameId ?? string.Empty);
                command.Parameters.AddWithValue("$rommLaunchPath", state.RommLaunchPath ?? string.Empty);
                command.Parameters.AddWithValue("$rommLaunchArgs", state.RommLaunchArgs ?? string.Empty);
                command.Parameters.AddWithValue("$rommAdditionalAppSyncedUtc", ToDbTimestamp(state.RommAdditionalAppSyncedUtc));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to write install state.", ex);
            }
            finally
            {
                ReleaseSync("UpsertStateAsync");
            }
        }

        /// <summary>
        /// Deletes install state for a given LaunchBox game id.
        /// </summary>
        public async Task DeleteStateAsync(string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            InstallState existing = null;
            try
            {
                existing = await GetStateAsync(launchBoxGameId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read install state before delete. {ex.Message}");
            }
            if (!await WaitForSyncAsync(cancellationToken, "DeleteStateAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM InstallState WHERE LaunchBoxGameId = $id";
                command.Parameters.AddWithValue("$id", launchBoxGameId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to delete install state.", ex);
            }
            finally
            {
                ReleaseSync("DeleteStateAsync");
            }

            if (existing != null)
            {
                await UpsertIdentityAsync(
                        launchBoxGameId,
                        existing.RommRomId,
                        existing.RommPlatformId,
                        existing.RemoteMd5,
                        existing.LocalMd5,
                        existing.WindowsInstallType,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deletes install state for a given LaunchBox game id but preserves merge metadata.
        /// </summary>
        public async Task DeleteStatePreserveMergeAsync(string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            InstallState existing = null;
            try
            {
                existing = await GetStateAsync(launchBoxGameId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read install state before delete. {ex.Message}");
            }

            if (!await WaitForSyncAsync(cancellationToken, "DeleteStatePreserveMergeAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                if (existing != null)
                {
                    using var connection = new SqliteConnection(BuildConnectionString());
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    var command = connection.CreateCommand();
                    command.CommandText = @"
UPDATE InstallState
SET InstalledPath = '',
    ArchivePath = '',
    InstallRootPath = '',
    IsInstalled = 0,
    InstalledUtc = '',
    LastValidatedUtc = '',
    InstallStatus = 'NotInstalled',
    InstallPhase = '',
    LastError = '',
    LastAttemptUtc = '',
    LastCompletedUtc = '',
    RommLaunchPath = '',
    RommLaunchArgs = ''
WHERE LaunchBoxGameId = $id;
";
                    command.Parameters.AddWithValue("$id", launchBoxGameId);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to delete install state.", ex);
            }
            finally
            {
                ReleaseSync("DeleteStatePreserveMergeAsync");
            }
        }

        /// <summary>
        /// Returns true when the LaunchBox game is sourced from RomM.
        /// </summary>
        public bool IsRomMSourcedGame(IGame game)
        {
            if (game == null)
            {
                return false;
            }

            // Check the built-in Source field instead of custom field
            return string.Equals(game.Source, "RomM", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads RomM identifiers from the database, falling back to custom fields when needed.
        /// </summary>
        public (string RommRomId, string RommPlatformId, string ServerUrl) GetRomMDetails(IGame game)
        {
            return GetRomMDetailsAsync(game, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Reads RomM identifiers from the database, falling back to custom fields when needed.
        /// </summary>
        public async Task<(string RommRomId, string RommPlatformId, string ServerUrl)> GetRomMDetailsAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return (null, null, null);
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var state = await GetStateAsync(game.Id, cancellationToken).ConfigureAwait(false);
            if (state != null && !string.IsNullOrWhiteSpace(state.RommRomId) && !string.IsNullOrWhiteSpace(state.RommPlatformId))
            {
                var serverUrl = ResolveServerUrl(null);
                return (state.RommRomId, state.RommPlatformId, serverUrl);
            }

            return (null, null, ResolveServerUrl(null));
        }

        /// <summary>
        /// Reads install metadata from the database, falling back to custom fields when needed.
        /// </summary>
        public (string RommRomId, string RommPlatformId, string RemoteMd5, string LocalMd5, string WindowsInstallType) GetIdentity(IGame game)
        {
            return GetIdentityAsync(game, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Reads install metadata from the database, falling back to custom fields when needed.
        /// </summary>
        public async Task<(string RommRomId, string RommPlatformId, string RemoteMd5, string LocalMd5, string WindowsInstallType)> GetIdentityAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return (null, null, null, null, null);
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var state = await GetStateAsync(game.Id, cancellationToken).ConfigureAwait(false);
            if (state != null)
            {
                return (state.RommRomId, state.RommPlatformId, state.RemoteMd5, state.LocalMd5, state.WindowsInstallType);
            }

            return (null, null, null, null, null);
        }

        /// <summary>
        /// Upserts identity metadata for a LaunchBox game without altering install state.
        /// </summary>
        public async Task UpsertIdentityAsync(string launchBoxGameId, string rommRomId, string rommPlatformId, string remoteMd5, string localMd5, string windowsInstallType, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpsertIdentityAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO InstallState (
    LaunchBoxGameId, RommRomId, RommPlatformId, ServerUrl, RemoteMd5, LocalMd5, WindowsInstallType, IsInstalled, InstalledUtc, LastValidatedUtc,
    InstallStatus, InstallPhase, LastError, LastAttemptUtc, LastCompletedUtc, LastOperationId
) VALUES (
    $gameId, $romId, $platformId, $serverUrl, $remoteMd5, $localMd5, $windowsInstallType, 0, '', '',
    '', '', '', '', '', ''
)
ON CONFLICT(LaunchBoxGameId) DO UPDATE SET
    RommRomId = excluded.RommRomId,
    RommPlatformId = excluded.RommPlatformId,
    ServerUrl = excluded.ServerUrl,
    RemoteMd5 = excluded.RemoteMd5,
    LocalMd5 = excluded.LocalMd5,
    WindowsInstallType = excluded.WindowsInstallType,
    InstallStatus = excluded.InstallStatus,
    InstallPhase = excluded.InstallPhase,
    LastError = excluded.LastError,
    LastAttemptUtc = excluded.LastAttemptUtc,
    LastCompletedUtc = excluded.LastCompletedUtc,
    LastOperationId = excluded.LastOperationId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$romId", rommRomId ?? string.Empty);
                command.Parameters.AddWithValue("$platformId", rommPlatformId ?? string.Empty);
                command.Parameters.AddWithValue("$serverUrl", ResolveServerUrl(null));
                command.Parameters.AddWithValue("$remoteMd5", remoteMd5 ?? string.Empty);
                command.Parameters.AddWithValue("$localMd5", localMd5 ?? string.Empty);
                command.Parameters.AddWithValue("$windowsInstallType", windowsInstallType ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to upsert RomM identity metadata.", ex);
            }
            finally
            {
                ReleaseSync("UpsertIdentityAsync");
            }
        }

        private string ResolveServerUrl(string stateServerUrl)
        {
            if (!string.IsNullOrWhiteSpace(stateServerUrl))
            {
                return stateServerUrl;
            }

            return _settingsManager?.Load()?.ServerUrl ?? string.Empty;
        }

        /// <summary>
        /// Updates only the local MD5 for a game (used after computing hashes).
        /// </summary>
        public async Task UpdateLocalMd5Async(string launchBoxGameId, string localMd5, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpdateLocalMd5Async").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE InstallState
SET LocalMd5 = $localMd5
WHERE LaunchBoxGameId = $gameId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$localMd5", localMd5 ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to update local MD5.", ex);
            }
            finally
            {
                ReleaseSync("UpdateLocalMd5Async");
            }
        }

        /// <summary>
        /// Updates only the installed path for a game (used during custom-field backfill).
        /// </summary>
        public async Task UpdateInstalledPathAsync(string launchBoxGameId, string installedPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpdateInstalledPathAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE InstallState
SET InstalledPath = $installedPath,
    IsInstalled = $isInstalled,
    InstalledUtc = $installedUtc,
    LastValidatedUtc = $lastValidatedUtc
WHERE LaunchBoxGameId = $gameId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$installedPath", installedPath ?? string.Empty);
                var isInstalled = !string.IsNullOrWhiteSpace(installedPath)
                    && (File.Exists(installedPath) || Directory.Exists(installedPath));
                command.Parameters.AddWithValue("$isInstalled", isInstalled ? 1 : 0);
                command.Parameters.AddWithValue("$installedUtc", ToDbTimestamp(isInstalled ? DateTimeOffset.UtcNow : (DateTimeOffset?)null));
                command.Parameters.AddWithValue("$lastValidatedUtc", ToDbTimestamp(DateTimeOffset.UtcNow));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to update installed path.", ex);
            }
            finally
            {
                ReleaseSync("UpdateInstalledPathAsync");
            }
        }

        /// <summary>
        /// Retrieves only the RomM additional application identifier for a game.
        /// </summary>
        public async Task<string> GetRommAdditionalAppIdAsync(string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return null;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "GetRommAdditionalAppIdAsync").ConfigureAwait(false))
            {
                return null;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = "SELECT RommAdditionalAppId FROM InstallState WHERE LaunchBoxGameId = $id;";
                command.Parameters.AddWithValue("$id", launchBoxGameId);
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return value?.ToString();
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to read RomM additional app id.", ex);
                return null;
            }
            finally
            {
                ReleaseSync("GetRommAdditionalAppIdAsync");
            }
        }

        /// <summary>
        /// Updates the RomM additional application identifier for a game.
        /// </summary>
        public async Task UpdateRommAdditionalAppIdAsync(string launchBoxGameId, string additionalAppId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpdateRommAdditionalAppIdAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE InstallState
SET RommAdditionalAppId = $rommAdditionalAppId
WHERE LaunchBoxGameId = $gameId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$rommAdditionalAppId", additionalAppId ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to update RomM additional app id.", ex);
            }
            finally
            {
                ReleaseSync("UpdateRommAdditionalAppIdAsync");
            }
        }

        /// <summary>
        /// Updates RomM merged additional application fields (base game, launch path, args, sync timestamp).
        /// </summary>
        public async Task UpdateRommAdditionalAppStateAsync(
            string launchBoxGameId,
            string mergedBaseGameId,
            string launchPath,
            string launchArgs,
            DateTimeOffset? syncedUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpdateRommAdditionalAppStateAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE InstallState
SET RommMergedBaseGameId = $mergedBaseGameId,
    RommLaunchPath = $launchPath,
    RommLaunchArgs = $launchArgs,
    RommAdditionalAppSyncedUtc = $syncedUtc
WHERE LaunchBoxGameId = $gameId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$mergedBaseGameId", mergedBaseGameId ?? string.Empty);
                command.Parameters.AddWithValue("$launchPath", launchPath ?? string.Empty);
                command.Parameters.AddWithValue("$launchArgs", launchArgs ?? string.Empty);
                command.Parameters.AddWithValue("$syncedUtc", ToDbTimestamp(syncedUtc));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to update RomM additional app state.", ex);
            }
            finally
            {
                ReleaseSync("UpdateRommAdditionalAppStateAsync");
            }
        }

        /// <summary>
        /// Reads merged additional application state for a base game id.
        /// </summary>
        public async Task<InstallState> GetMergedStateForBaseGameAsync(string baseGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseGameId))
            {
                return null;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "GetMergedStateForBaseGameAsync").ConfigureAwait(false))
            {
                return null;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
SELECT LaunchBoxGameId, RommRomId, RommPlatformId, ServerUrl, RemoteMd5, LocalMd5, WindowsInstallType, InstalledPath, ArchivePath, InstallRootPath, IsInstalled, InstalledUtc, LastValidatedUtc,
       InstallStatus, InstallPhase, LastError, LastAttemptUtc, LastCompletedUtc, LastOperationId,
       RommAdditionalAppId, RommMergedBaseGameId, RommLaunchPath, RommLaunchArgs, RommAdditionalAppSyncedUtc
FROM InstallState
WHERE RommMergedBaseGameId = $id
LIMIT 1;
";
                command.Parameters.AddWithValue("$id", baseGameId);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return MapState(reader);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to read merged RomM install state.", ex);
                return null;
            }
            finally
            {
                ReleaseSync("GetMergedStateForBaseGameAsync");
            }
        }

        /// <summary>
        /// Updates only the server URL for a game (used during backfill from custom fields).
        /// </summary>
        public async Task UpdateServerUrlAsync(string launchBoxGameId, string serverUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "UpdateServerUrlAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE InstallState
SET ServerUrl = $serverUrl
WHERE LaunchBoxGameId = $gameId;
";
                command.Parameters.AddWithValue("$gameId", launchBoxGameId);
                command.Parameters.AddWithValue("$serverUrl", serverUrl ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to update server URL.", ex);
            }
            finally
            {
                ReleaseSync("UpdateServerUrlAsync");
            }
        }

        /// <summary>
        /// Ensures a RomM additional application id exists for a base game.
        /// </summary>
        public async Task<string> EnsureRommAdditionalAppIdAsync(string launchBoxGameId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return null;
            }

            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!await WaitForSyncAsync(cancellationToken, "EnsureRommAdditionalAppIdAsync").ConfigureAwait(false))
            {
                return null;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var select = connection.CreateCommand();
                select.CommandText = "SELECT RommAdditionalAppId FROM InstallState WHERE LaunchBoxGameId = $id;";
                select.Parameters.AddWithValue("$id", launchBoxGameId);
                var value = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var existing = value?.ToString();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }

                var newId = Guid.NewGuid().ToString();
                var insert = connection.CreateCommand();
                insert.CommandText = @"
INSERT INTO InstallState (
    LaunchBoxGameId, RommAdditionalAppId, IsInstalled, InstalledUtc, LastValidatedUtc
) VALUES (
    $gameId, $rommAdditionalAppId, 0, '', ''
)
ON CONFLICT(LaunchBoxGameId) DO UPDATE SET
    RommAdditionalAppId = excluded.RommAdditionalAppId;
";
                insert.Parameters.AddWithValue("$gameId", launchBoxGameId);
                insert.Parameters.AddWithValue("$rommAdditionalAppId", newId);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return newId;
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to ensure RomM additional app id.", ex);
                return null;
            }
            finally
            {
                ReleaseSync("EnsureRommAdditionalAppIdAsync");
            }
        }


        private async Task<bool> IsMigrationCompleteAsync(CancellationToken cancellationToken)
        {
            if (!await WaitForSyncAsync(cancellationToken, "IsMigrationCompleteAsync").ConfigureAwait(false))
            {
                return false;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM InstallStateMetadata WHERE Key = $key";
                command.Parameters.AddWithValue("$key", MigrationMarkerKey);
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read migration marker. {ex.Message}");
                return false;
            }
            finally
            {
                ReleaseSync("IsMigrationCompleteAsync");
            }
        }

        private async Task MarkMigrationCompleteAsync(CancellationToken cancellationToken)
        {
            if (!await WaitForSyncAsync(cancellationToken, "MarkMigrationCompleteAsync").ConfigureAwait(false))
            {
                return;
            }
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO InstallStateMetadata (Key, Value)
VALUES ($key, $value)
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
";
                command.Parameters.AddWithValue("$key", MigrationMarkerKey);
                command.Parameters.AddWithValue("$value", "true");
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to write migration marker. {ex.Message}");
            }
            finally
            {
                ReleaseSync("MarkMigrationCompleteAsync");
            }
        }

        private async Task<bool> WaitForSyncAsync(CancellationToken cancellationToken, string operation)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var acquired = await _sync.WaitAsync(_syncWaitTimeout, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                if (!acquired)
                {
                    _logger?.Warning($"InstallState lock timeout. Operation={operation}, WaitMs={sw.ElapsedMilliseconds}.");
                    return false;
                }
                if (sw.Elapsed > _syncWaitWarningThreshold)
                {
                    _logger?.Warning($"InstallState lock wait slow. Operation={operation}, WaitMs={sw.ElapsedMilliseconds}.");
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _logger?.Warning($"InstallState lock wait cancelled. Operation={operation}, WaitMs={sw.ElapsedMilliseconds}.");
                return false;
            }
        }

        private void ReleaseSync(string operation)
        {
            try
            {
                _sync.Release();
            }
            catch (SemaphoreFullException)
            {
                _logger?.Warning($"InstallState lock released too many times. Operation={operation}.");
            }
        }

        /// <summary>
        /// Returns true when a RomM game has enough metadata to install locally.
        /// </summary>
        public bool IsGameInstallable(IGame game)
        {
            if (game == null)
            {
                return false;
            }

            var (romId, platformId, serverUrl) = GetRomMDetails(game);
            return !string.IsNullOrWhiteSpace(romId)
                && !string.IsNullOrWhiteSpace(platformId)
                && !string.IsNullOrWhiteSpace(serverUrl);
        }


        /// <summary>
        /// Checks the file system for installed content and corrects state if needed.
        /// </summary>
        private bool ValidateInstallState(InstallState state)
        {
            if (state == null)
            {
                return false;
            }

            var wasInstalled = state.IsInstalled;
            var installed = !string.IsNullOrWhiteSpace(state.InstalledPath)
                && (File.Exists(state.InstalledPath) || Directory.Exists(state.InstalledPath));
            if (installed != state.IsInstalled)
            {
                state.IsInstalled = installed;
                state.LastValidatedUtc = DateTimeOffset.UtcNow;
                if (!installed)
                {
                    state.InstalledUtc = null;
                }
                _logger?.Info($"Install state corrected for {state.LaunchBoxGameId}. WasInstalled={wasInstalled}, IsInstalled={installed}.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds the SQLite connection string for the install state database.
        /// </summary>
        private string BuildConnectionString()
        {
            return new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString();
        }

        /// <summary>
        /// Serializes a timestamp to a stable string for SQLite storage.
        /// </summary>
        private static string ToDbTimestamp(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.ToString("O") : string.Empty;
        }

        /// <summary>
        /// Parses a timestamp from SQLite storage.
        /// </summary>
        private static DateTimeOffset? FromDbTimestamp(object value)
        {
            if (value == null)
            {
                return null;
            }

            var text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(text, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>
        /// Maps a SQLite row into an <see cref="InstallState"/> instance.
        /// </summary>
        private static InstallState MapState(SqliteDataReader reader)
        {
            return new InstallState
            {
                LaunchBoxGameId = reader.GetString(0),
                RommRomId = reader.IsDBNull(1) ? null : reader.GetString(1),
                RommPlatformId = reader.IsDBNull(2) ? null : reader.GetString(2),
                ServerUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                RemoteMd5 = reader.IsDBNull(4) ? null : reader.GetString(4),
                LocalMd5 = reader.IsDBNull(5) ? null : reader.GetString(5),
                WindowsInstallType = reader.IsDBNull(6) ? null : reader.GetString(6),
                InstalledPath = reader.IsDBNull(7) ? null : reader.GetString(7),
                ArchivePath = reader.IsDBNull(8) ? null : reader.GetString(8),
                InstallRootPath = reader.IsDBNull(9) ? null : reader.GetString(9),
                IsInstalled = reader.GetInt32(10) == 1,
                InstalledUtc = reader.IsDBNull(11) ? null : FromDbTimestamp(reader.GetValue(11)),
                LastValidatedUtc = reader.IsDBNull(12) ? null : FromDbTimestamp(reader.GetValue(12)),
                InstallStatus = reader.IsDBNull(13) ? null : reader.GetString(13),
                InstallPhase = reader.IsDBNull(14) ? null : reader.GetString(14),
                LastError = reader.IsDBNull(15) ? null : reader.GetString(15),
                LastAttemptUtc = reader.IsDBNull(16) ? null : FromDbTimestamp(reader.GetValue(16)),
                LastCompletedUtc = reader.IsDBNull(17) ? null : FromDbTimestamp(reader.GetValue(17)),
                LastOperationId = reader.IsDBNull(18) ? null : reader.GetString(18),
                RommAdditionalAppId = reader.IsDBNull(19) ? null : reader.GetString(19),
                RommMergedBaseGameId = reader.IsDBNull(20) ? null : reader.GetString(20),
                RommLaunchPath = reader.IsDBNull(21) ? null : reader.GetString(21),
                RommLaunchArgs = reader.IsDBNull(22) ? null : reader.GetString(22),
                RommAdditionalAppSyncedUtc = reader.IsDBNull(23) ? null : FromDbTimestamp(reader.GetValue(23))
            };
        }

        private static async Task EnsurePlatformMappingsSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "PRAGMA table_info(PlatformMappings);";

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!reader.IsDBNull(1))
                    {
                        columns.Add(reader.GetString(1));
                    }
                }
            }

            await AddColumnIfMissingAsync(connection, columns, "CustomInstallDirectory", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "InstallScenario", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "TargetImportFile", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "InstallerSilentArgs", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "SelfContained", "INTEGER NOT NULL DEFAULT 1", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "AssociatedEmulatorId", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "OstInstallLocation", "TEXT", cancellationToken).ConfigureAwait(false);
            await AddColumnIfMissingAsync(connection, columns, "BonusInstallLocation", "TEXT", cancellationToken).ConfigureAwait(false);
        }

        private static async Task AddColumnIfMissingAsync(SqliteConnection connection, HashSet<string> columns, string columnName, string columnDefinition, CancellationToken cancellationToken)
        {
            if (columns.Contains(columnName))
            {
                return;
            }

            var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE PlatformMappings ADD COLUMN {columnName} {columnDefinition}";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
