using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;

namespace RomMbox.Services
{
    internal sealed class PlatformMappingStore
    {
        private const string DatabaseFileName = "romm.db";
        private readonly LoggingService _logger;
        private readonly string _databasePath;
        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);

        public PlatformMappingStore(LoggingService logger)
        {
            _logger = logger;
            _databasePath = System.IO.Path.Combine(PluginPaths.GetPluginDataDirectory(), DatabaseFileName);
        }

        public async Task<PlatformMapping> GetPlatformMappingAsync(string rommPlatformId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformId))
            {
                return null;
            }

            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
 SELECT RommPlatformId, RommPlatformName, LaunchBoxPlatformName, AutoMapped, DisableAutoImport, ExtractAfterDownload,
        ExtractionBehavior, InstallerMode, MusicRootPath, InstallOst, BonusRootPath, InstallBonus, PreReqsRootPath, InstallPreReqs,
        CustomInstallDirectory, InstallScenario, TargetImportFile, InstallerSilentArgs, SelfContained, AssociatedEmulatorId,
        OstInstallLocation, BonusInstallLocation, EmulatorCoreId, EmulatorCoreName, EmulatorCorePath, EmulatorLaunchArgs,
        RomInstallRoot, RomArchivePolicy, Ps3GameDirectory, Rpcs3ExecutablePath, InstallDlcAutomatically, InstallUpdatesAutomatically,
        Rpcs3LicenseDirectory, SkipRegionMismatchedDlc, SkipUnmatchedRapFiles, PreferMetadataBasedPackageMatching,
        SupportedFileTypes, PreferredLaunchExtensions, UseGameSubdirectory, InstallAllMatchingFiles, InstallFromArchiveDirectly,
        UseGeneralFallbackInstaller, PluginKey, PluginSettings, ArchiveHandlingMode, InstallLayoutMode, ArtifactSelectionMode,
        Ps4GamesDirectory, ShadPs4ExecutablePath, Ps4ExternalPkgExtractorPath, Ps4FailIfDirectPkgExtractorMissing
 FROM PlatformMappings
 WHERE RommPlatformId = $rommPlatformId;
 ";
                command.Parameters.AddWithValue("$rommPlatformId", rommPlatformId);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return MapMapping(reader);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform mapping. {ex.Message}");
                return null;
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task<PlatformMapping[]> GetPlatformMappingsAsync(CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
 SELECT RommPlatformId, RommPlatformName, LaunchBoxPlatformName, AutoMapped, DisableAutoImport, ExtractAfterDownload,
        ExtractionBehavior, InstallerMode, MusicRootPath, InstallOst, BonusRootPath, InstallBonus, PreReqsRootPath, InstallPreReqs,
        CustomInstallDirectory, InstallScenario, TargetImportFile, InstallerSilentArgs, SelfContained, AssociatedEmulatorId,
        OstInstallLocation, BonusInstallLocation, EmulatorCoreId, EmulatorCoreName, EmulatorCorePath, EmulatorLaunchArgs,
        RomInstallRoot, RomArchivePolicy, Ps3GameDirectory, Rpcs3ExecutablePath, InstallDlcAutomatically, InstallUpdatesAutomatically,
        Rpcs3LicenseDirectory, SkipRegionMismatchedDlc, SkipUnmatchedRapFiles, PreferMetadataBasedPackageMatching,
        SupportedFileTypes, PreferredLaunchExtensions, UseGameSubdirectory, InstallAllMatchingFiles, InstallFromArchiveDirectly,
        UseGeneralFallbackInstaller, PluginKey, PluginSettings, ArchiveHandlingMode, InstallLayoutMode, ArtifactSelectionMode,
        Ps4GamesDirectory, ShadPs4ExecutablePath, Ps4ExternalPkgExtractorPath, Ps4FailIfDirectPkgExtractorMissing
 FROM PlatformMappings;
 ";
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var results = new List<PlatformMapping>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(MapMapping(reader));
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform mappings. {ex.Message}");
                return Array.Empty<PlatformMapping>();
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task SavePlatformMappingAsync(PlatformMapping mapping, CancellationToken cancellationToken)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.RommPlatformId))
            {
                return;
            }

            await SavePlatformMappingsAsync(new[] { mapping }, cancellationToken).ConfigureAwait(false);
        }

        public async Task SavePlatformMappingsAsync(IEnumerable<PlatformMapping> mappings, CancellationToken cancellationToken)
        {
            if (mappings == null)
            {
                return;
            }

            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();
                foreach (var mapping in mappings)
                {
                    if (mapping == null || string.IsNullOrWhiteSpace(mapping.RommPlatformId))
                    {
                        continue;
                    }

                    var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
 INSERT INTO PlatformMappings (
     RommPlatformId, RommPlatformName, LaunchBoxPlatformName, AutoMapped, DisableAutoImport, ExtractAfterDownload,
     ExtractionBehavior, InstallerMode, MusicRootPath, InstallOst, BonusRootPath, InstallBonus, PreReqsRootPath, InstallPreReqs,
     CustomInstallDirectory, InstallScenario, TargetImportFile, InstallerSilentArgs, SelfContained, AssociatedEmulatorId,
     OstInstallLocation, BonusInstallLocation, EmulatorCoreId, EmulatorCoreName, EmulatorCorePath, EmulatorLaunchArgs,
     RomInstallRoot, RomArchivePolicy, Ps3GameDirectory, Rpcs3ExecutablePath, InstallDlcAutomatically, InstallUpdatesAutomatically,
     Rpcs3LicenseDirectory, SkipRegionMismatchedDlc, SkipUnmatchedRapFiles, PreferMetadataBasedPackageMatching,
     SupportedFileTypes, PreferredLaunchExtensions, UseGameSubdirectory, InstallAllMatchingFiles, InstallFromArchiveDirectly,
     UseGeneralFallbackInstaller, PluginKey, PluginSettings, ArchiveHandlingMode, InstallLayoutMode, ArtifactSelectionMode,
     Ps4GamesDirectory, ShadPs4ExecutablePath, Ps4ExternalPkgExtractorPath, Ps4FailIfDirectPkgExtractorMissing
  ) VALUES (
     $rommPlatformId, $rommPlatformName, $launchBoxPlatformName, $autoMapped, $disableAutoImport, $extractAfterDownload,
     $extractionBehavior, $installerMode, $musicRootPath, $installOst, $bonusRootPath, $installBonus, $preReqsRootPath, $installPreReqs,
     $customInstallDirectory, $installScenario, $targetImportFile, $installerSilentArgs, $selfContained, $associatedEmulatorId,
     $ostInstallLocation, $bonusInstallLocation, $emulatorCoreId, $emulatorCoreName, $emulatorCorePath, $emulatorLaunchArgs,
     $romInstallRoot, $romArchivePolicy, $ps3GameDirectory, $rpcs3ExecutablePath, $installDlcAutomatically, $installUpdatesAutomatically,
     $rpcs3LicenseDirectory, $skipRegionMismatchedDlc, $skipUnmatchedRapFiles, $preferMetadataBasedPackageMatching,
     $supportedFileTypes, $preferredLaunchExtensions, $useGameSubdirectory, $installAllMatchingFiles, $installFromArchiveDirectly,
     $useGeneralFallbackInstaller, $pluginKey, $pluginSettings, $archiveHandlingMode, $installLayoutMode, $artifactSelectionMode,
     $ps4GamesDirectory, $shadPs4ExecutablePath, $ps4ExternalPkgExtractorPath, $ps4FailIfDirectPkgExtractorMissing
  )
  ON CONFLICT(RommPlatformId) DO UPDATE SET
     RommPlatformName = excluded.RommPlatformName,
     LaunchBoxPlatformName = excluded.LaunchBoxPlatformName,
     AutoMapped = excluded.AutoMapped,
     DisableAutoImport = excluded.DisableAutoImport,
     ExtractAfterDownload = excluded.ExtractAfterDownload,
     ExtractionBehavior = excluded.ExtractionBehavior,
     InstallerMode = excluded.InstallerMode,
     MusicRootPath = excluded.MusicRootPath,
     InstallOst = excluded.InstallOst,
     BonusRootPath = excluded.BonusRootPath,
     InstallBonus = excluded.InstallBonus,
     PreReqsRootPath = excluded.PreReqsRootPath,
     InstallPreReqs = excluded.InstallPreReqs,
     CustomInstallDirectory = excluded.CustomInstallDirectory,
     InstallScenario = excluded.InstallScenario,
     TargetImportFile = excluded.TargetImportFile,
     InstallerSilentArgs = excluded.InstallerSilentArgs,
     SelfContained = excluded.SelfContained,
     AssociatedEmulatorId = excluded.AssociatedEmulatorId,
     OstInstallLocation = excluded.OstInstallLocation,
     BonusInstallLocation = excluded.BonusInstallLocation,
     EmulatorCoreId = excluded.EmulatorCoreId,
     EmulatorCoreName = excluded.EmulatorCoreName,
     EmulatorCorePath = excluded.EmulatorCorePath,
     EmulatorLaunchArgs = excluded.EmulatorLaunchArgs,
     RomInstallRoot = excluded.RomInstallRoot,
     RomArchivePolicy = excluded.RomArchivePolicy,
     Ps3GameDirectory = excluded.Ps3GameDirectory,
     Rpcs3ExecutablePath = excluded.Rpcs3ExecutablePath,
     InstallDlcAutomatically = excluded.InstallDlcAutomatically,
     InstallUpdatesAutomatically = excluded.InstallUpdatesAutomatically,
     Rpcs3LicenseDirectory = excluded.Rpcs3LicenseDirectory,
     SkipRegionMismatchedDlc = excluded.SkipRegionMismatchedDlc,
     SkipUnmatchedRapFiles = excluded.SkipUnmatchedRapFiles,
     PreferMetadataBasedPackageMatching = excluded.PreferMetadataBasedPackageMatching,
     SupportedFileTypes = excluded.SupportedFileTypes,
     PreferredLaunchExtensions = excluded.PreferredLaunchExtensions,
     UseGameSubdirectory = excluded.UseGameSubdirectory,
     InstallAllMatchingFiles = excluded.InstallAllMatchingFiles,
     InstallFromArchiveDirectly = excluded.InstallFromArchiveDirectly,
     UseGeneralFallbackInstaller = excluded.UseGeneralFallbackInstaller,
     PluginKey = excluded.PluginKey,
     PluginSettings = excluded.PluginSettings,
     ArchiveHandlingMode = excluded.ArchiveHandlingMode,
     InstallLayoutMode = excluded.InstallLayoutMode,
     ArtifactSelectionMode = excluded.ArtifactSelectionMode,
     Ps4GamesDirectory = excluded.Ps4GamesDirectory,
     ShadPs4ExecutablePath = excluded.ShadPs4ExecutablePath,
     Ps4ExternalPkgExtractorPath = excluded.Ps4ExternalPkgExtractorPath,
     Ps4FailIfDirectPkgExtractorMissing = excluded.Ps4FailIfDirectPkgExtractorMissing;
  ";
                    command.Parameters.AddWithValue("$rommPlatformId", mapping.RommPlatformId ?? string.Empty);
                    command.Parameters.AddWithValue("$rommPlatformName", mapping.RommPlatformName ?? string.Empty);
                    command.Parameters.AddWithValue("$launchBoxPlatformName", mapping.LaunchBoxPlatformName ?? string.Empty);
                    command.Parameters.AddWithValue("$autoMapped", mapping.AutoMapped ? 1 : 0);
                    command.Parameters.AddWithValue("$disableAutoImport", mapping.DisableAutoImport ? 1 : 0);
                    command.Parameters.AddWithValue("$extractAfterDownload", mapping.ExtractAfterDownload ? 1 : 0);
                    command.Parameters.AddWithValue("$extractionBehavior", mapping.ExtractionBehavior.ToString());
                    command.Parameters.AddWithValue("$installerMode", mapping.InstallerMode.ToString());
                    command.Parameters.AddWithValue("$musicRootPath", mapping.MusicRootPath ?? string.Empty);
                    command.Parameters.AddWithValue("$installOst", mapping.InstallOst ? 1 : 0);
                    command.Parameters.AddWithValue("$bonusRootPath", mapping.BonusRootPath ?? string.Empty);
                    command.Parameters.AddWithValue("$installBonus", mapping.InstallBonus ? 1 : 0);
                    command.Parameters.AddWithValue("$preReqsRootPath", mapping.PreReqsRootPath ?? string.Empty);
                    command.Parameters.AddWithValue("$installPreReqs", mapping.InstallPreReqs ? 1 : 0);
                    command.Parameters.AddWithValue("$customInstallDirectory", mapping.CustomInstallDirectory ?? string.Empty);
                    command.Parameters.AddWithValue("$installScenario", mapping.InstallScenario.ToString());
                    command.Parameters.AddWithValue("$targetImportFile", mapping.TargetImportFile ?? string.Empty);
                    command.Parameters.AddWithValue("$installerSilentArgs", mapping.InstallerSilentArgs ?? string.Empty);
                    command.Parameters.AddWithValue("$selfContained", mapping.SelfContained ? 1 : 0);
                    command.Parameters.AddWithValue("$associatedEmulatorId", mapping.AssociatedEmulatorId ?? string.Empty);
                    command.Parameters.AddWithValue("$ostInstallLocation", mapping.OstInstallLocation.ToString());
                    command.Parameters.AddWithValue("$bonusInstallLocation", mapping.BonusInstallLocation.ToString());
                    command.Parameters.AddWithValue("$emulatorCoreId", mapping.EmulatorCoreId ?? string.Empty);
                    command.Parameters.AddWithValue("$emulatorCoreName", mapping.EmulatorCoreName ?? string.Empty);
                    command.Parameters.AddWithValue("$emulatorCorePath", mapping.EmulatorCorePath ?? string.Empty);
                    command.Parameters.AddWithValue("$emulatorLaunchArgs", mapping.EmulatorLaunchArgs ?? string.Empty);
                    command.Parameters.AddWithValue("$romInstallRoot", mapping.RomInstallRoot ?? string.Empty);
                    command.Parameters.AddWithValue("$romArchivePolicy", mapping.RomArchivePolicy ?? string.Empty);
                    command.Parameters.AddWithValue("$ps3GameDirectory", mapping.Ps3GameDirectory ?? string.Empty);
                    command.Parameters.AddWithValue("$rpcs3ExecutablePath", mapping.Rpcs3ExecutablePath ?? string.Empty);
                    command.Parameters.AddWithValue("$installDlcAutomatically", mapping.InstallDlcAutomatically ? 1 : 0);
                    command.Parameters.AddWithValue("$installUpdatesAutomatically", mapping.InstallUpdatesAutomatically ? 1 : 0);
                    command.Parameters.AddWithValue("$rpcs3LicenseDirectory", mapping.Rpcs3LicenseDirectory ?? string.Empty);
                    command.Parameters.AddWithValue("$skipRegionMismatchedDlc", mapping.SkipRegionMismatchedDlc ? 1 : 0);
                    command.Parameters.AddWithValue("$skipUnmatchedRapFiles", mapping.SkipUnmatchedRapFiles ? 1 : 0);
                    command.Parameters.AddWithValue("$preferMetadataBasedPackageMatching", mapping.PreferMetadataBasedPackageMatching ? 1 : 0);
                    command.Parameters.AddWithValue("$supportedFileTypes", mapping.SupportedFileTypes ?? string.Empty);
                    command.Parameters.AddWithValue("$preferredLaunchExtensions", mapping.PreferredLaunchExtensions ?? string.Empty);
                    command.Parameters.AddWithValue("$useGameSubdirectory", mapping.UseGameSubdirectory ? 1 : 0);
                    command.Parameters.AddWithValue("$installAllMatchingFiles", mapping.InstallAllMatchingFiles ? 1 : 0);
                    command.Parameters.AddWithValue("$installFromArchiveDirectly", mapping.InstallFromArchiveDirectly ? 1 : 0);
                    command.Parameters.AddWithValue("$useGeneralFallbackInstaller", mapping.UseGeneralFallbackInstaller ? 1 : 0);
                    command.Parameters.AddWithValue("$pluginKey", mapping.PluginKey ?? string.Empty);
                    command.Parameters.AddWithValue("$pluginSettings", mapping.PluginSettings ?? string.Empty);
                    command.Parameters.AddWithValue("$archiveHandlingMode", mapping.ArchiveHandlingMode ?? string.Empty);
                    command.Parameters.AddWithValue("$installLayoutMode", mapping.InstallLayoutMode ?? string.Empty);
                    command.Parameters.AddWithValue("$artifactSelectionMode", mapping.ArtifactSelectionMode ?? string.Empty);
                    command.Parameters.AddWithValue("$ps4GamesDirectory", mapping.Ps4GamesDirectory ?? string.Empty);
                    command.Parameters.AddWithValue("$shadPs4ExecutablePath", mapping.ShadPs4ExecutablePath ?? string.Empty);
                    command.Parameters.AddWithValue("$ps4ExternalPkgExtractorPath", mapping.Ps4ExternalPkgExtractorPath ?? string.Empty);
                    command.Parameters.AddWithValue("$ps4FailIfDirectPkgExtractorMissing", mapping.Ps4FailIfDirectPkgExtractorMissing ? 1 : 0);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to save platform mappings. {ex.Message}");
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task<PlatformAlias[]> GetPlatformAliasesAsync(CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
SELECT AliasId, Alias, LaunchBoxPlatformName
FROM PlatformMappingAliases;
";
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var results = new List<PlatformAlias>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new PlatformAlias
                    {
                        Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        Alias = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        LaunchBoxPlatformName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                    });
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform aliases. {ex.Message}");
                return Array.Empty<PlatformAlias>();
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task SavePlatformAliasAsync(PlatformAlias alias, CancellationToken cancellationToken)
        {
            if (alias == null || string.IsNullOrWhiteSpace(alias.Id))
            {
                return;
            }

            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO PlatformMappingAliases (AliasId, Alias, LaunchBoxPlatformName)
VALUES ($id, $alias, $launchBoxPlatformName)
ON CONFLICT(AliasId) DO UPDATE SET
    Alias = excluded.Alias,
    LaunchBoxPlatformName = excluded.LaunchBoxPlatformName;
";
                command.Parameters.AddWithValue("$id", alias.Id ?? string.Empty);
                command.Parameters.AddWithValue("$alias", alias.Alias ?? string.Empty);
                command.Parameters.AddWithValue("$launchBoxPlatformName", alias.LaunchBoxPlatformName ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to save platform alias. {ex.Message}");
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task DeletePlatformAliasAsync(string aliasId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(aliasId))
            {
                return;
            }

            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM PlatformMappingAliases WHERE AliasId = $id";
                command.Parameters.AddWithValue("$id", aliasId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to delete platform alias. {ex.Message}");
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task<string[]> GetExcludedRommPlatformIdsAsync(CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.CommandText = "SELECT RommPlatformId FROM ExcludedRommPlatforms";
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var results = new List<string>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!reader.IsDBNull(0))
                    {
                        results.Add(reader.GetString(0));
                    }
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read excluded RomM platforms. {ex.Message}");
                return Array.Empty<string>();
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task SaveExcludedRommPlatformIdsAsync(IEnumerable<string> platformIds, CancellationToken cancellationToken)
        {
            if (platformIds == null)
            {
                return;
            }

            await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(BuildConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();

                var clear = connection.CreateCommand();
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM ExcludedRommPlatforms";
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                foreach (var platformId in platformIds)
                {
                    if (string.IsNullOrWhiteSpace(platformId))
                    {
                        continue;
                    }

                    var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO ExcludedRommPlatforms (RommPlatformId) VALUES ($id)";
                    insert.Parameters.AddWithValue("$id", platformId);
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to save excluded RomM platforms. {ex.Message}");
            }
            finally
            {
                _sync.Release();
            }
        }

        private string BuildConnectionString()
        {
            return new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString();
        }

        private static PlatformMapping MapMapping(SqliteDataReader reader)
        {
            var mapping = new PlatformMapping
            {
                RommPlatformId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                RommPlatformName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                LaunchBoxPlatformName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                AutoMapped = !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                DisableAutoImport = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                ExtractAfterDownload = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                MusicRootPath = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                InstallOst = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                BonusRootPath = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                InstallBonus = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                PreReqsRootPath = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                InstallPreReqs = !reader.IsDBNull(13) && reader.GetInt32(13) == 1,
                CustomInstallDirectory = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                TargetImportFile = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                InstallerSilentArgs = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                SelfContained = reader.IsDBNull(18) || reader.GetInt32(18) == 1,
                AssociatedEmulatorId = reader.IsDBNull(19) ? string.Empty : reader.GetString(19)
            };

            if (reader.FieldCount > 22)
            {
                mapping.EmulatorCoreId = reader.IsDBNull(22) ? string.Empty : reader.GetString(22);
                mapping.EmulatorCoreName = reader.IsDBNull(23) ? string.Empty : reader.GetString(23);
                mapping.EmulatorCorePath = reader.IsDBNull(24) ? string.Empty : reader.GetString(24);
                mapping.EmulatorLaunchArgs = reader.IsDBNull(25) ? string.Empty : reader.GetString(25);
                mapping.RomInstallRoot = reader.IsDBNull(26) ? string.Empty : reader.GetString(26);
                mapping.RomArchivePolicy = reader.IsDBNull(27) ? string.Empty : reader.GetString(27);
            }

            if (reader.FieldCount > 35)
            {
                mapping.Ps3GameDirectory = reader.IsDBNull(28) ? string.Empty : reader.GetString(28);
                mapping.Rpcs3ExecutablePath = reader.IsDBNull(29) ? string.Empty : reader.GetString(29);
                mapping.InstallDlcAutomatically = !reader.IsDBNull(30) && reader.GetInt32(30) == 1;
                mapping.InstallUpdatesAutomatically = !reader.IsDBNull(31) && reader.GetInt32(31) == 1;
                mapping.Rpcs3LicenseDirectory = reader.IsDBNull(32) ? string.Empty : reader.GetString(32);
                mapping.SkipRegionMismatchedDlc = !reader.IsDBNull(33) && reader.GetInt32(33) == 1;
                mapping.SkipUnmatchedRapFiles = !reader.IsDBNull(34) && reader.GetInt32(34) == 1;
                mapping.PreferMetadataBasedPackageMatching = !reader.IsDBNull(35) && reader.GetInt32(35) == 1;
            }

            if (reader.FieldCount > 40)
            {
                mapping.SupportedFileTypes = reader.IsDBNull(36) ? string.Empty : reader.GetString(36);
                mapping.PreferredLaunchExtensions = reader.IsDBNull(37) ? string.Empty : reader.GetString(37);
                mapping.UseGameSubdirectory = !reader.IsDBNull(38) && reader.GetInt32(38) == 1;
                mapping.InstallAllMatchingFiles = !reader.IsDBNull(39) && reader.GetInt32(39) == 1;
                mapping.InstallFromArchiveDirectly = !reader.IsDBNull(40) && reader.GetInt32(40) == 1;
            }

            if (reader.FieldCount > 41)
            {
                mapping.UseGeneralFallbackInstaller = !reader.IsDBNull(41) && reader.GetInt32(41) == 1;
            }

            if (reader.FieldCount > 46)
            {
                mapping.PluginKey = reader.IsDBNull(42) ? string.Empty : reader.GetString(42);
                mapping.PluginSettings = reader.IsDBNull(43) ? string.Empty : reader.GetString(43);
                mapping.ArchiveHandlingMode = reader.IsDBNull(44) ? string.Empty : reader.GetString(44);
                mapping.InstallLayoutMode = reader.IsDBNull(45) ? string.Empty : reader.GetString(45);
                mapping.ArtifactSelectionMode = reader.IsDBNull(46) ? string.Empty : reader.GetString(46);
            }

            if (reader.FieldCount > 50)
            {
                mapping.Ps4GamesDirectory = reader.IsDBNull(47) ? string.Empty : reader.GetString(47);
                mapping.ShadPs4ExecutablePath = reader.IsDBNull(48) ? string.Empty : reader.GetString(48);
                mapping.Ps4ExternalPkgExtractorPath = reader.IsDBNull(49) ? string.Empty : reader.GetString(49);
                mapping.Ps4FailIfDirectPkgExtractorMissing = !reader.IsDBNull(50) && reader.GetInt32(50) == 1;
            }

            var extractionBehaviorText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            if (!Enum.TryParse(extractionBehaviorText, out ExtractionBehavior extractionBehavior))
            {
                extractionBehavior = ExtractionBehavior.Subfolder;
            }
            mapping.ExtractionBehavior = extractionBehavior;

            var installerModeText = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            if (!Enum.TryParse(installerModeText, out InstallerMode installerMode))
            {
                installerMode = InstallerMode.Manual;
            }
            mapping.InstallerMode = installerMode;

            var installScenarioText = reader.IsDBNull(15) ? string.Empty : reader.GetString(15);
            if (!Enum.TryParse(installScenarioText, out InstallScenario installScenario))
            {
                installScenario = InstallScenario.Basic;
            }
            mapping.InstallScenario = installScenario;

            var ostLocationText = reader.IsDBNull(20) ? string.Empty : reader.GetString(20);
            if (!Enum.TryParse(ostLocationText, out OptionalContentLocation ostLocation))
            {
                ostLocation = OptionalContentLocation.Centralized;
            }
            mapping.OstInstallLocation = ostLocation;

            var bonusLocationText = reader.IsDBNull(21) ? string.Empty : reader.GetString(21);
            if (!Enum.TryParse(bonusLocationText, out OptionalContentLocation bonusLocation))
            {
                bonusLocation = OptionalContentLocation.Centralized;
            }
            mapping.BonusInstallLocation = bonusLocation;

            return mapping;
        }
    }
}
