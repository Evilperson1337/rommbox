using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RomMbox.Models;
using RomMbox.Models.Import;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using RomMbox.Services.Install;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    /// <summary>
    /// Coordinates importing RomM data into LaunchBox by
    /// 1) fetching ROM lists from the RomM server,
    /// 2) matching or creating LaunchBox games,
    /// 3) applying metadata/custom fields, and
    /// 4) optionally downloading media, ROMs, and saves.
    /// </summary>
    internal sealed class ImportService
    {
        private const int PageSize = 50;
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly PlatformMappingService _mappingService;
        private readonly IRommClient _client;
        private readonly IDataManager _dataManager;
        private readonly DownloadService _downloadService;
        private readonly MatchIgnoreStore _ignoreStore;
        private readonly InstallStateService _installStateService;
        private readonly HashSet<string> _loggedPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Creates an import service with the core dependencies required to talk to RomM
        /// and manipulate LaunchBox data.
        /// </summary>
        /// <param name="logger">Logger used for operational and diagnostic messages.</param>
        /// <param name="settingsManager">Loads plugin settings such as server URL.</param>
        /// <param name="mappingService">Resolves RomM platform IDs to LaunchBox platform names.</param>
        /// <param name="client">HTTP client wrapper for RomM API calls.</param>
        /// <param name="dataManager">Optional LaunchBox data manager; if null, PluginHelper is used.</param>
        public ImportService(LoggingService logger, SettingsManager settingsManager, PlatformMappingService mappingService, IRommClient client, IDataManager dataManager = null)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _mappingService = mappingService;
            _client = client;
            _dataManager = dataManager;
            var archiveService = new ArchiveService(logger, settingsManager);
            _downloadService = new DownloadService(logger, client, archiveService, settingsManager);
            _ignoreStore = new MatchIgnoreStore(logger);
            _installStateService = new InstallStateService(logger, settingsManager);
        }

        /// <summary>
        /// Imports an entire platform catalog using the default matching rules and
        /// with download + metadata enabled.
        /// </summary>
        /// <param name="platformId">RomM platform id to import.</param>
        /// <param name="cancellationToken">Cancellation token for the import loop.</param>
        public async Task<ImportResult> ImportPlatformCatalogAsync(
            string platformId,
            CancellationToken cancellationToken)
        {
            var dataManager = _dataManager ?? PluginHelper.DataManager;

            return await ImportPlatformCatalogAsync(platformId, true, true, true, true, true, true, cancellationToken, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Imports every ROM from the RomM platform catalog into LaunchBox, optionally
        /// downloading files and matching duplicates based on multiple strategies.
        /// </summary>
        /// <param name="platformId">RomM platform id to import.</param>
        /// <param name="downloadDuringImport">True to fetch media during import.</param>
        /// <param name="allowDuplicates">True to allow new games even if a match exists.</param>
        /// <param name="matchByRomId">Try matching by RomM id first.</param>
        /// <param name="matchByMd5">Try matching by MD5 if available.</param>
        /// <param name="matchByTitle">Try matching by normalized title.</param>
        /// <param name="matchByFileName">Try matching by ROM filename.</param>
        /// <param name="cancellationToken">Cancellation token for the import loop.</param>
        /// <param name="progress">Optional progress reporter for UI feedback.</param>
        /// <returns>Aggregated import results.</returns>
        public async Task<ImportResult> ImportPlatformCatalogAsync(
            string platformId,
            bool downloadDuringImport,
            bool allowDuplicates,
            bool matchByRomId,
            bool matchByMd5,
            bool matchByTitle,
            bool matchByFileName,
            CancellationToken cancellationToken,
            IProgress<ImportProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("PlatformId is required.", nameof(platformId));
            }

            var result = new ImportResult();
            var start = DateTimeOffset.UtcNow;
            var operationId = Guid.NewGuid().ToString("N");
            using (_logger.BeginOperation(operationId))
            {
                _logger.Write(LogLevel.Info, "Import platform catalog started.", null,
                    "Subsystem", "Import",
                    "Operation", "ImportPlatformCatalog",
                    "PlatformId", platformId);
            }
            var dataManager = _dataManager ?? PluginHelper.DataManager;
            if (dataManager == null)
            {
                throw new InvalidOperationException("LaunchBox DataManager is unavailable.");
            }

            // Resolve the LaunchBox platform once and build a fast lookup index
            // so the inner import loop stays O(1) for common match strategies.
            var launchBoxPlatform = await ResolveLaunchBoxPlatformAsync(dataManager, platformId, cancellationToken)
                .ConfigureAwait(false);
            var matchIndex = BuildMatchIndex(dataManager, launchBoxPlatform, matchByMd5);

            try
            {
                var page = 1;
                var totalKnown = false;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Pull one page at a time to avoid large memory spikes on huge catalogs.
                    var pageResult = await _client.ListRomsByPlatformAsync(platformId, page, PageSize, null, cancellationToken).ConfigureAwait(false);
                    var items = pageResult?.Items ?? new List<RommRom>();
                    if (page == 1 && pageResult?.Total.HasValue == true)
                    {
                        result.TotalRoms = pageResult.Total.Value;
                        totalKnown = true;
                    }

                    if (items.Count == 0)
                    {
                        break;
                    }

                    if (!totalKnown)
                    {
                        result.TotalRoms += items.Count;
                    }

                    foreach (var rom in items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IGame game = null;
                        try
                        {
                            // Determine if this ROM already exists in LaunchBox and track why.
                            var match = FindExistingGameMatch(matchIndex, launchBoxPlatform, rom, matchByRomId, matchByMd5, matchByTitle, matchByFileName);
                            using (_logger.BeginOperation(operationId))
                            {
                                LogMatchDecision(rom, match, launchBoxPlatform?.Name, matchByRomId, matchByMd5, matchByTitle, matchByFileName);
                            }
                            var existingGame = match?.Game;
                            if (!allowDuplicates && existingGame != null)
                            {
                                _logger?.Debug($"Skipping duplicate RomM game {rom?.Id ?? "unknown"} ({rom?.DisplayTitle ?? rom?.Title}).");
                                result.SkippedDuplicates++;
                                TrackMatchCandidate(result, rom, existingGame, match);
                                result.ReportItems.Add(new ImportReportItem
                                {
                                    GameTitle = rom?.DisplayTitle ?? rom?.Title ?? "Unknown",
                                    Status = ImportReportStatus.Skipped,
                                    Details = "Skipped duplicate"
                                });
                                ReportProgress(progress, result, totalKnown ? result.TotalRoms : 0);
                                continue;
                            }

                            var romDetails = await ResolveRomDetailsAsync(rom, downloadDuringImport, cancellationToken).ConfigureAwait(false);
                            var displayTitle = romDetails?.DisplayTitle ?? string.Empty;
                            _logger?.Debug($"Importing Game ID: {romDetails?.Id ?? "unknown"} - {displayTitle} for {launchBoxPlatform.Name}");
                            if (existingGame != null)
                            {
                                _logger?.Debug($"Soft-syncing existing game for RomM ID {romDetails?.Id ?? rom?.Id ?? "unknown"}.");
                                game = existingGame;
                                ApplyRomMIdentity(game, romDetails ?? rom, match);
                                TrackMatchCandidate(result, romDetails ?? rom, existingGame, match);
                                result.SuccessfulImports++;
                                result.ReportItems.Add(new ImportReportItem
                                {
                                    GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                        ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                        : displayTitle,
                                    Status = ImportReportStatus.Success,
                                    Details = "Soft-synced existing game"
                                });
                            }
                            else
                            {
                                // Create a fresh LaunchBox game entry, then populate metadata.
                                game = dataManager.AddNewGame(displayTitle);
                                if (game == null)
                                {
                                    result.FailedImports++;
                                    result.ReportItems.Add(new ImportReportItem
                                    {
                                        GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                            ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                            : displayTitle,
                                        Status = ImportReportStatus.Failed,
                                        Details = "Failed to create LaunchBox game"
                                    });
                                    ReportProgress(progress, result, totalKnown ? result.TotalRoms : 0);
                                    continue;
                                }

                                game.Platform = launchBoxPlatform.Name;
                                game.Source = "RomM";
                                if (romDetails?.CreatedAt.HasValue == true)
                                {
                                    game.DateAdded = romDetails.CreatedAt.Value.DateTime;
                                }
                                ApplyLaunchBoxMetadataIfAvailable(game, romDetails ?? rom, dataManager);
                                ApplyMetadataToGame(game, romDetails ?? rom, installed: false);
                                ApplyRomMIdentity(game, romDetails ?? rom, match);

                                if (downloadDuringImport)
                                {
                                    await DownloadMediaAsync(launchBoxPlatform, game, romDetails ?? rom, cancellationToken).ConfigureAwait(false);
                                }

                                _logger?.Debug($"Imported new game for RomM ID {romDetails?.Id ?? rom?.Id ?? "unknown"}.");
                                result.SuccessfulImports++;
                                result.ReportItems.Add(new ImportReportItem
                                {
                                    GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                        ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                        : displayTitle,
                                    Status = ImportReportStatus.Success,
                                    Details = "Successfully imported"
                                });
                            }
                            ReportProgress(progress, result, totalKnown ? result.TotalRoms : 0);
                        }
                        catch (Exception ex)
                        {
                            using (_logger.BeginOperation(operationId))
                            {
                                _logger.Write(LogLevel.Error, "Failed to import RomM ROM.", ex,
                                    "Subsystem", "Import",
                                    "Operation", "ImportPlatformCatalog",
                                    "RomMId", rom?.Id ?? "unknown");
                            }
                            if (game != null)
                            {
                                try
                                {
                                    dataManager.TryRemoveGame(game);
                                }
                                catch
                                {
                                }
                            }
                            result.FailedImports++;
                            result.ReportItems.Add(new ImportReportItem
                            {
                                GameTitle = rom?.DisplayTitle ?? rom?.Title ?? "Unknown",
                                Status = ImportReportStatus.Failed,
                                Details = ex.Message
                            });
                            ReportProgress(progress, result, totalKnown ? result.TotalRoms : 0);
                        }
                    }

                    if (items.Count < PageSize)
                    {
                        break;
                    }

                    page++;
                }
            }
            finally
            {
                result.Duration = DateTimeOffset.UtcNow - start;
                // Persist LaunchBox changes even if a transient file lock occurs.
                ExecuteWithRetry(() =>
                {
                    SaveAndReloadDataManager(dataManager);
                }, "ImportPlatformCatalogAsync Save/Reload");
            }

            using (_logger.BeginOperation(operationId))
            {
                var summary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Subsystem"] = "Import",
                    ["Operation"] = "ImportPlatformCatalog",
                    ["PlatformId"] = platformId,
                    ["Imported"] = result.SuccessfulImports,
                    ["Skipped"] = result.SkippedDuplicates,
                    ["Failed"] = result.FailedImports,
                    ["DurationMs"] = (long)result.Duration.TotalMilliseconds,
                    ["Result"] = result.FailedImports > 0 ? "Partial" : "Success"
                };
                _logger.Write(LogLevel.Info, "Import platform catalog completed.", null, summary);
            }

            return result;
        }

        /// <summary>
        /// Lists all ROMs for a platform with optional progress reporting. This is used
        /// by the UI to show a selectable list before import.
        /// </summary>
        /// <param name="platformId">RomM platform id to query.</param>
        /// <param name="cancellationToken">Cancellation token for pagination.</param>
        public async Task<IReadOnlyList<RommRom>> ListPlatformRomsAsync(string platformId, CancellationToken cancellationToken)
        {
            return await ListPlatformRomsAsync(platformId, cancellationToken, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists all ROMs for a platform and updates progress as pages are received.
        /// </summary>
        /// <param name="platformId">RomM platform id to query.</param>
        /// <param name="cancellationToken">Cancellation token for pagination.</param>
        /// <param name="progress">Optional progress reporter for UI feedback.</param>
        public async Task<IReadOnlyList<RommRom>> ListPlatformRomsAsync(string platformId, CancellationToken cancellationToken, IProgress<ImportProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("PlatformId is required.", nameof(platformId));
            }

            var roms = new List<RommRom>();
            var page = 1;
            var totalKnown = false;
            var total = 0;
            _logger?.Debug($"ListPlatformRomsAsync start: platformId={platformId}, pageSize={PageSize}.");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.Debug($"ListPlatformRomsAsync requesting page {page} for platformId={platformId}.");
                var pageResult = await _client.ListRomsByPlatformAsync(platformId, page, PageSize, null, cancellationToken)
                    .ConfigureAwait(false);
                var items = pageResult?.Items ?? new List<RommRom>();
                if (!totalKnown && pageResult?.Total.HasValue == true)
                {
                    totalKnown = true;
                    total = pageResult.Total.Value;
                    _logger?.Debug($"ListPlatformRomsAsync total known: total={total} for platformId={platformId}.");
                }

                _logger?.Debug($"ListPlatformRomsAsync received page {page}: items={items.Count}, total={(pageResult?.Total.HasValue == true ? pageResult.Total.Value.ToString() : "<null>")}, apiOffset={pageResult?.Offset}.");
                if (items.Count == 0)
                {
                    _logger?.Debug($"ListPlatformRomsAsync stopping: page {page} returned 0 items for platformId={platformId}.");
                    break;
                }

                roms.AddRange(items);
                progress?.Report(new ImportProgress
                {
                    Total = totalKnown ? total : 0,
                    Processed = roms.Count
                });

                if (totalKnown && roms.Count >= total)
                {
                    _logger?.Debug($"ListPlatformRomsAsync stopping: reached total {total} with {roms.Count} items for platformId={platformId}.");
                    break;
                }

                if (items.Count < PageSize)
                {
                    _logger?.Debug($"ListPlatformRomsAsync stopping: page {page} returned {items.Count} < pageSize {PageSize} for platformId={platformId}.");
                    break;
                }

                page++;
            }

            _logger?.Debug($"ListPlatformRomsAsync complete: platformId={platformId}, totalItems={roms.Count}, pagesFetched={page}, totalKnown={totalKnown}, total={total}.");
            return roms;
        }

        /// <summary>
        /// Imports an explicit selection of ROMs from the UI using default match settings.
        /// </summary>
        public async Task<ImportResult> ImportSelectedRomsAsync(
            string platformId,
            IReadOnlyList<RomMbox.Models.Import.RomImportSelection> romSelections,
            CancellationToken cancellationToken)
        {
            return await ImportSelectedRomsAsync(platformId, romSelections, true, true, true, true, true, cancellationToken, null).ConfigureAwait(false);
        }

        public async Task<ImportResult> ImportSelectedRomsAsync(
            string platformId,
            IReadOnlyList<RomMbox.Models.Import.RomImportSelection> romSelections,
            bool allowDuplicates,
            bool matchByRomId,
            bool matchByMd5,
            bool matchByTitle,
            DuplicateMatchOption duplicateMatchOption,
            CancellationToken cancellationToken,
            IProgress<ImportProgress> progress)
        {
            return await ImportSelectedRomsAsync(platformId, romSelections, allowDuplicates, matchByRomId,
                duplicateMatchOption == DuplicateMatchOption.Md5,
                duplicateMatchOption == DuplicateMatchOption.GameName,
                duplicateMatchOption == DuplicateMatchOption.FileName,
                cancellationToken, progress).ConfigureAwait(false);
        }

        public async Task<ImportResult> ImportSelectedRomsAsync(
            string platformId,
            IReadOnlyList<RomMbox.Models.Import.RomImportSelection> romSelections,
            bool allowDuplicates,
            bool matchByRomId,
            bool matchByMd5,
            bool matchByTitle,
            bool matchByFileName,
            CancellationToken cancellationToken,
            IProgress<ImportProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("PlatformId is required.", nameof(platformId));
            }

            if (romSelections == null)
            {
                throw new ArgumentNullException(nameof(romSelections));
            }

            var result = new ImportResult { TotalRoms = romSelections.Count };
            var start = DateTimeOffset.UtcNow;
            var operationId = Guid.NewGuid().ToString("N");
            _logger?.Debug($"ImportSelectedRomsAsync start: PlatformId={platformId}, Selections={romSelections.Count}, AllowDuplicates={allowDuplicates}, MatchByRomId={matchByRomId}, MatchByMd5={matchByMd5}, MatchByTitle={matchByTitle}, MatchByFileName={matchByFileName}.");
            using (_logger.BeginOperation(operationId))
            {
                var startProps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Subsystem"] = "Import",
                    ["Operation"] = "ImportSelected",
                    ["PlatformId"] = platformId,
                    ["SelectedCount"] = romSelections.Count
                };
                _logger.Write(LogLevel.Info, "Import selected ROMs started.", null, startProps);
            }
            var dataManager = _dataManager ?? PluginHelper.DataManager;
            if (dataManager == null)
            {
                throw new InvalidOperationException("LaunchBox DataManager is unavailable.");
            }

            var launchBoxPlatform = await ResolveLaunchBoxPlatformAsync(dataManager, platformId, cancellationToken)
                .ConfigureAwait(false);
            var matchIndex = BuildMatchIndex(dataManager, launchBoxPlatform, matchByMd5);

            try
            {
                foreach (var selection in romSelections)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rom = selection?.Rom;
                    if (rom == null)
                    {
                        result.FailedImports++;
                        result.ReportItems.Add(new ImportReportItem
                        {
                            GameTitle = "Unknown",
                            Status = ImportReportStatus.Failed,
                            Details = "Missing ROM selection"
                        });
                        ReportProgress(progress, result, result.TotalRoms);
                        continue;
                    }

                    IGame game = null;
                    try
                    {
                        var match = FindExistingGameMatch(matchIndex, launchBoxPlatform, rom, matchByRomId, matchByMd5, matchByTitle, matchByFileName);
                        using (_logger.BeginOperation(operationId))
                        {
                            LogMatchDecision(rom, match, launchBoxPlatform?.Name, matchByRomId, matchByMd5, matchByTitle, matchByFileName);
                        }
                        var existingGame = match?.Game;
                        if (!allowDuplicates && existingGame != null)
                        {
                            _logger?.Debug($"Skipping duplicate RomM game {rom?.Id ?? "unknown"} ({rom?.DisplayTitle ?? rom?.Title}).");
                            result.SkippedDuplicates++;
                            TrackMatchCandidate(result, rom, existingGame, match);
                            result.ReportItems.Add(new ImportReportItem
                            {
                                GameTitle = rom?.DisplayTitle ?? rom?.Title ?? "Unknown",
                                Status = ImportReportStatus.Skipped,
                                Details = "Skipped duplicate"
                            });
                            ReportProgress(progress, result, result.TotalRoms);
                            continue;
                        }

                        _logger?.Debug($"Import selection for {rom?.Id ?? "unknown"}: InstallLocally={selection.InstallLocally}, DownloadSaves={selection.DownloadSaves}");
                        var romDetails = await ResolveRomDetailsAsync(rom, includeMedia: true, cancellationToken).ConfigureAwait(false);
                        var displayTitle = romDetails?.DisplayTitle ?? string.Empty;
                        _logger?.Debug($"Importing Game ID: {romDetails?.Id ?? "unknown"} - {displayTitle} for {launchBoxPlatform.Name}");
                        if (existingGame != null)
                        {
                            _logger?.Debug($"Soft-syncing existing game for RomM ID {romDetails?.Id ?? rom?.Id ?? "unknown"}.");
                            game = existingGame;
                            ApplyRomMIdentity(game, romDetails ?? rom, match);
                            TrackMatchCandidate(result, romDetails ?? rom, existingGame, match);
                            if (selection.DownloadSaves)
                            {
                                await ImportSavesForRomAsync(romDetails ?? rom, launchBoxPlatform, game, cancellationToken).ConfigureAwait(false);
                            }
                            result.SuccessfulImports++;
                            result.ReportItems.Add(new ImportReportItem
                            {
                                GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                    ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                    : displayTitle,
                                Status = ImportReportStatus.Success,
                                Details = "Soft-synced existing game"
                            });
                        }
                        else
                        {
                            game = dataManager.AddNewGame(displayTitle);
                            if (game == null)
                            {
                                result.FailedImports++;
                                result.ReportItems.Add(new ImportReportItem
                                {
                                    GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                        ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                        : displayTitle,
                                    Status = ImportReportStatus.Failed,
                                    Details = "Failed to create LaunchBox game"
                                });
                                ReportProgress(progress, result, result.TotalRoms);
                                continue;
                            }

                            game.Platform = launchBoxPlatform.Name;
                            game.Source = "RomM";
                            if (romDetails?.CreatedAt.HasValue == true)
                            {
                                game.DateAdded = romDetails.CreatedAt.Value.DateTime;
                            }
                            var installOutcome = await InstallLocalRomIfRequestedAsync(romDetails ?? rom, launchBoxPlatform, game, selection.InstallLocally, cancellationToken)
                                .ConfigureAwait(false);
                            if (!installOutcome.Success)
                            {
                                EnsureStubApplicationPath(dataManager, game, romDetails ?? rom, launchBoxPlatform.Name, displayTitle);
                                throw new InvalidOperationException(installOutcome.Message ?? "ROM download failed.");
                            }
                            if (!installOutcome.Installed)
                            {
                                game.Installed = false;
                                if (!string.IsNullOrWhiteSpace(game.ApplicationPath))
                                {
                                    _logger?.Debug($"Clearing ApplicationPath for non-installed import '{game.Title}'.");
                                    game.ApplicationPath = string.Empty;
                                }
                                EnsureStubApplicationPath(dataManager, game, romDetails ?? rom, launchBoxPlatform.Name, displayTitle);
                            }
                            ApplyLaunchBoxMetadataIfAvailable(game, romDetails ?? rom, dataManager);
                            ApplyMetadataToGame(game, romDetails ?? rom, installOutcome.Installed);
                            if (!installOutcome.Installed)
                            {
                                EnsureEmulatorId(dataManager, game, launchBoxPlatform.Name);
                            }
                            ApplyRomMIdentity(game, romDetails ?? rom, match);

                            await DownloadMediaAsync(launchBoxPlatform, game, romDetails ?? rom, cancellationToken).ConfigureAwait(false);
                            if (selection.DownloadSaves)
                            {
                                await ImportSavesForRomAsync(romDetails ?? rom, launchBoxPlatform, game, cancellationToken).ConfigureAwait(false);
                            }

                            _logger?.Debug($"Imported new game for RomM ID {romDetails?.Id ?? rom?.Id ?? "unknown"}.");
                            result.SuccessfulImports++;
                            result.ReportItems.Add(new ImportReportItem
                            {
                                GameTitle = string.IsNullOrWhiteSpace(displayTitle)
                                    ? (rom?.DisplayTitle ?? rom?.Title ?? "Unknown")
                                    : displayTitle,
                                Status = ImportReportStatus.Success,
                                Details = "Successfully imported"
                            });
                        }
                        ReportProgress(progress, result, result.TotalRoms);
                    }
                    catch (Exception ex)
                    {
                        using (_logger.BeginOperation(operationId))
                        {
                            _logger.Write(LogLevel.Error, "Failed to import RomM ROM.", ex,
                                "Subsystem", "Import",
                                "Operation", "ImportSelected",
                                "RomMId", rom?.Id ?? "unknown");
                        }
                        if (game != null)
                        {
                            try
                            {
                                dataManager.TryRemoveGame(game);
                            }
                            catch
                            {
                            }
                        }
                        result.FailedImports++;
                        result.ReportItems.Add(new ImportReportItem
                        {
                            GameTitle = rom?.DisplayTitle ?? rom?.Title ?? "Unknown",
                            Status = ImportReportStatus.Failed,
                            Details = ex.Message
                        });
                        ReportProgress(progress, result, result.TotalRoms);
                    }
                }
            }
            finally
            {
                result.Duration = DateTimeOffset.UtcNow - start;
                ExecuteWithRetry(() =>
                {
                    SaveAndReloadDataManager(dataManager);
                }, "ImportSelectedRomsAsync Save/Reload");
            }

            using (_logger.BeginOperation(operationId))
            {
                var summary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Subsystem"] = "Import",
                    ["Operation"] = "ImportSelected",
                    ["PlatformId"] = platformId,
                    ["Imported"] = result.SuccessfulImports,
                    ["Skipped"] = result.SkippedDuplicates,
                    ["Failed"] = result.FailedImports,
                    ["DurationMs"] = (long)result.Duration.TotalMilliseconds,
                    ["Result"] = result.FailedImports > 0 ? "Partial" : "Success"
                };
                _logger.Write(LogLevel.Info, "Import selected ROMs completed.", null, summary);
            }

            return result;
        }

        private static void ReportProgress(IProgress<ImportProgress> progress, ImportResult result, int total)
        {
            if (progress == null || result == null)
            {
                return;
            }

            progress.Report(new ImportProgress
            {
                Total = total > 0 ? total : result.TotalRoms,
                Processed = result.Processed,
                Successful = result.SuccessfulImports,
                Skipped = result.SkippedDuplicates,
                Failed = result.FailedImports
            });
        }

        private async Task<IPlatform> ResolveLaunchBoxPlatformAsync(IDataManager dataManager, string platformId, CancellationToken cancellationToken)
        {
            var mappingResult = await _mappingService.DiscoverPlatformsAsync(cancellationToken).ConfigureAwait(false);
            var mapping = mappingResult.Mappings.FirstOrDefault(entry =>
                string.Equals(entry.RommPlatformId, platformId, StringComparison.OrdinalIgnoreCase));
            if (mapping == null)
            {
                throw new InvalidOperationException("No mapping found for the selected RomM platform.");
            }

            if (string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatformName))
            {
                throw new InvalidOperationException("No LaunchBox platform mapping available for the selected RomM platform.");
            }

            var launchBoxPlatform = dataManager.GetPlatformByName(mapping.LaunchBoxPlatformName);
            if (launchBoxPlatform == null)
            {
                launchBoxPlatform = dataManager.AddNewPlatform(mapping.LaunchBoxPlatformName);
                if (launchBoxPlatform == null)
                {
                    throw new InvalidOperationException("LaunchBox platform not found and could not be created.");
                }
            }

            dataManager.ReloadIfNeeded();

            return launchBoxPlatform;
        }

        private void ApplyLaunchBoxMetadataIfAvailable(IGame targetGame, RommRom rom, IDataManager dataManager)
        {
            if (targetGame == null || rom == null || dataManager == null)
            {
                return;
            }

            var launchBoxId = rom.Metadatum?.LaunchBoxId;
            if (!launchBoxId.HasValue)
            {
                return;
            }

            var lookupId = launchBoxId.Value.ToString();
            var sourceGame = dataManager.GetGameById(lookupId);
            if (sourceGame == null)
            {
                _logger?.Debug($"LaunchBox metadata lookup failed for LaunchBoxId={lookupId} (RomM Id={rom.Id ?? "<null>"}).");
                return;
            }

            _logger?.Info($"Applying LaunchBox metadata from GameId={lookupId} to '{targetGame.Title}' (RomM Id={rom.Id ?? "<null>"}).");
            CopyLaunchBoxMetadata(sourceGame, targetGame);
        }

        private static void CopyLaunchBoxMetadata(IGame source, IGame target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.Title = source.Title;
            target.SortTitle = source.SortTitle;
            target.Version = source.Version;
            target.ReleaseDate = source.ReleaseDate;
            target.ReleaseYear = source.ReleaseYear;
            target.Region = source.Region;
            target.GenresString = source.GenresString;
            target.Developer = source.Developer;
            target.Publisher = source.Publisher;
            target.Series = source.Series;
            target.Notes = source.Notes;
            target.Rating = source.Rating;
            target.StarRating = source.StarRating;
            target.StarRatingFloat = source.StarRatingFloat;
            target.MaxPlayers = source.MaxPlayers;
            target.PlayMode = source.PlayMode;
            target.VideoUrl = source.VideoUrl;
        }

        private static void ApplyMetadataToGame(IGame game, RommRom rom, bool installed)
        {
            if (game == null || rom == null)
            {
                return;
            }

            var title = rom.DisplayTitle;
            if (!string.IsNullOrWhiteSpace(title))
            {
                game.Title = title;
            }

            if (!string.IsNullOrWhiteSpace(rom.Revision))
            {
                game.Version = rom.Revision;
            }

            if (rom.ReleaseDate.HasValue)
            {
                game.ReleaseDate = rom.ReleaseDate.Value.DateTime;
                var year = ExtractYearFromReleaseDate(rom.ReleaseDate);
                if (year.HasValue)
                {
                    game.ReleaseYear = year.Value;
                }
            }
            else if (rom.Metadatum?.FirstReleaseDate.HasValue == true)
            {
                try
                {
                    var releaseDate = FromUnixTimeSmart(rom.Metadatum.FirstReleaseDate.Value);
                    game.ReleaseDate = releaseDate.DateTime;
                    var year = ExtractYearFromReleaseDate(releaseDate);
                    if (year.HasValue)
                    {
                        game.ReleaseYear = year.Value;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(rom.Region))
            {
                game.Region = rom.Region;
            }
            else
            {
                game.Region = "North America";
            }

            var genres = rom.Genres ?? rom.Metadatum?.Genres;
            if (genres != null && genres.Count > 0)
            {
                var sanitized = genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToList();
                if (sanitized.Count > 0)
                {
                    game.GenresString = string.Join("; ", sanitized);
                }
            }

            if (!string.IsNullOrWhiteSpace(rom.Developer))
            {
                game.Developer = rom.Developer;
            }
            else if (rom.Metadatum?.Companies != null && rom.Metadatum.Companies.Count > 0)
            {
                var company = rom.Metadatum.Companies.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(company))
                {
                    game.Developer = company;
                }
            }

            if (!string.IsNullOrWhiteSpace(rom.Publisher))
            {
                game.Publisher = rom.Publisher;
            }
            else if (rom.Metadatum?.Companies != null && rom.Metadatum.Companies.Count > 0)
            {
                var company = rom.Metadatum.Companies.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(company))
                {
                    game.Publisher = company;
                }
            }

            var notes = !string.IsNullOrWhiteSpace(rom.Summary) ? rom.Summary : rom.Description;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                game.Notes = notes;
            }

            if (rom.Metadatum?.AgeRatings != null && rom.Metadatum.AgeRatings.Count > 0)
            {
                var rating = rom.Metadatum.AgeRatings.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                game.Rating = NormalizeAgeRating(rating);
            }
            else if (!string.IsNullOrWhiteSpace(rom.Rating))
            {
                game.Rating = NormalizeAgeRating(rom.Rating);
            }
            else
            {
                game.Rating = "Not Rated";
            }

            if (rom.Metadatum?.AverageRating.HasValue == true)
            {
                var clamped = Math.Max(0, Math.Min(100, rom.Metadatum.AverageRating.Value));
                var stars = (int)Math.Round(clamped / 20.0, MidpointRounding.AwayFromZero);
                game.StarRating = stars;
                game.StarRatingFloat = (float)stars;
            }

            if (!string.IsNullOrWhiteSpace(rom.YoutubeVideoId))
            {
                game.VideoUrl = "https://www.youtube.com/watch?v=" + rom.YoutubeVideoId;
            }

            if (rom.Metadatum?.PlayerCount != null && int.TryParse(rom.Metadatum.PlayerCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxPlayers))
            {
                game.MaxPlayers = maxPlayers;
            }

            if (rom.Metadatum?.GameModes != null && rom.Metadatum.GameModes.Count > 0)
            {
                var modes = rom.Metadatum.GameModes.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                if (modes.Length > 0)
                {
                    game.PlayMode = NormalizePlayMode(modes);
                }
            }

            if (rom.Metadatum?.Franchises != null && rom.Metadatum.Franchises.Count > 0)
            {
                var series = rom.Metadatum.Franchises.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(series))
                {
                    game.Series = series;
                }
            }

            game.ReleaseType = "Released";
            game.Status = installed ? "Installed" : "Available Remotely";
        }

        private void ApplyRomMIdentity(IGame game, RommRom rom, GameMatch match)
        {
            if (game == null || rom == null)
            {
                return;
            }

            // Always stamp the Source field to identify RomM games.
            game.Source = "RomM";

            _logger?.Debug($"ApplyRomMIdentity for '{game.Title}': RomId={rom.Id ?? "<null>"}, PlatformId={rom.PlatformId ?? "<null>"}, Md5={rom.Md5 ?? "<null>"}.");

            var localMd5 = GetOrComputeLocalMd5(game);
            if (string.IsNullOrWhiteSpace(localMd5) && !string.IsNullOrWhiteSpace(rom.Md5))
            {
                localMd5 = rom.Md5;
            }

            QueueIdentityWrite(game.Id, rom.Id, rom.PlatformId, rom.Md5, localMd5);

            _logger?.Debug($"ApplyRomMIdentity local MD5 for '{game.Title}': LocalMd5={(string.IsNullOrWhiteSpace(localMd5) ? "<empty>" : localMd5)}.");
        }

        private void QueueIdentityWrite(string gameId, string romId, string platformId, string remoteMd5, string localMd5)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _installStateService.UpsertIdentityAsync(
                            gameId,
                            romId,
                            platformId,
                            remoteMd5,
                            localMd5,
                            windowsInstallType: null,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to persist RomM identity for '{gameId ?? "<unknown>"}': {ex.Message}");
                }
            });
        }

        private void ApplyMatchMetadata(IGame game, RommRom rom, GameMatch match)
        {
            if (game == null || rom == null)
            {
                return;
            }

            if (match == null || match.Strategy == MatchStrategy.RommId || match.Strategy == MatchStrategy.Md5)
            {
                ApplyMetadataToGame(game, rom, installed: false);
                return;
            }

            ApplyMetadataMissingOnly(game, rom, match);
        }

        internal async Task<RommRom> ResolveRomDetailsForReview(string romId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(romId))
            {
                return null;
            }

            return await _client.GetRomDetailsAsync(romId, cancellationToken).ConfigureAwait(false);
        }

        internal void ApplyMatchForReview(IGame game, RommRom rom, string strategy, string confidence)
        {
            if (game == null || rom == null)
            {
                return;
            }

            _logger?.Debug($"ApplyMatchForReview start for '{game.Title}': ApplicationPath='{game.ApplicationPath}', Installed={game.Installed == true}.");

            var match = new GameMatch
            {
                Game = game,
                Strategy = Enum.TryParse(strategy, true, out MatchStrategy parsed) ? parsed : MatchStrategy.None,
                ConfidenceLabel = confidence ?? string.Empty,
                ConfidenceScore = 0
            };

            ApplyRomMIdentity(game, rom, match);
            _logger?.Debug($"ApplyMatchForReview end for '{game.Title}': ApplicationPath='{game.ApplicationPath}', Installed={game.Installed == true}.");
        }

        private void TrackMatchCandidate(ImportResult result, RommRom rom, IGame existingGame, GameMatch match)
        {
            if (result == null || rom == null || existingGame == null || match == null)
            {
                return;
            }

            if (match.Strategy == MatchStrategy.RommId || match.Strategy == MatchStrategy.Md5)
            {
                return;
            }

            if (_ignoreStore.IsIgnored(rom.PlatformId ?? string.Empty, rom.Id ?? string.Empty, existingGame.Id ?? string.Empty))
            {
                return;
            }

            result.MatchCandidates.Add(new RommMatchCandidate
            {
                PlatformId = rom.PlatformId,
                RommId = rom.Id,
                RommTitle = rom.DisplayTitle ?? rom.Title,
                LaunchBoxGameId = existingGame.Id,
                LaunchBoxTitle = existingGame.Title,
                Strategy = match.Strategy.ToString(),
                Confidence = match.ConfidenceLabel
            });
        }

        private void LogMatchDecision(RommRom rom, GameMatch match, string platformName, bool matchByRomId, bool matchByMd5, bool matchByTitle, bool matchByFileName)
        {
            if (rom == null)
            {
                return;
            }

            var romTitle = rom.DisplayTitle ?? rom.Title ?? string.Empty;
            var romFile = ResolveRomFileName(rom);
            var normalizedRomTitle = NormalizeTitleForMatch(romTitle);
            if (match == null || match.Game == null)
            {
                _logger?.Debug($"No match found for RomM '{romTitle}' (Id={rom.Id ?? "unknown"}, Platform={platformName ?? "Unknown"}, File='{romFile}', NormalizedTitle='{normalizedRomTitle}', MatchByRomId={matchByRomId}, MatchByMd5={matchByMd5}, MatchByTitle={matchByTitle}, MatchByFileName={matchByFileName}).");
                return;
            }

            _logger?.Debug($"Match found for RomM '{romTitle}' (Id={rom.Id ?? "unknown"}, Platform={platformName ?? "Unknown"}, File='{romFile}', NormalizedTitle='{normalizedRomTitle}', MatchByRomId={matchByRomId}, MatchByMd5={matchByMd5}, MatchByTitle={matchByTitle}, MatchByFileName={matchByFileName}): LaunchBox '{match.Game.Title}' (Id={match.Game.Id ?? "unknown"}) via {match.Strategy} (Confidence={match.ConfidenceLabel}).");
        }

        private static void ApplyMetadataMissingOnly(IGame game, RommRom rom, GameMatch match)
        {
            if (game == null || rom == null)
            {
                return;
            }

            CompareAndSet(match, "Title", game.Title, rom.DisplayTitle, value => game.Title = value);
            CompareAndSet(match, "Version", game.Version, rom.Revision, value => game.Version = value);

            if (!game.ReleaseDate.HasValue)
            {
                if (rom.ReleaseDate.HasValue)
                {
                    var releaseDate = rom.ReleaseDate.Value.DateTime;
                    CompareAndSet(match, "ReleaseDate", game.ReleaseDate?.ToString("o"), releaseDate.ToString("o"), _ =>
                    {
                        game.ReleaseDate = releaseDate;
                        var year = ExtractYearFromReleaseDate(rom.ReleaseDate);
                        if (year.HasValue && !game.ReleaseYear.HasValue)
                        {
                            game.ReleaseYear = year.Value;
                        }
                    });
                }
                else if (rom.Metadatum?.FirstReleaseDate.HasValue == true)
                {
                    try
                    {
                        var releaseDate = FromUnixTimeSmart(rom.Metadatum.FirstReleaseDate.Value).DateTime;
                        CompareAndSet(match, "ReleaseDate", game.ReleaseDate?.ToString("o"), releaseDate.ToString("o"), _ =>
                        {
                            game.ReleaseDate = releaseDate;
                            var year = ExtractYearFromReleaseDate(new DateTimeOffset(releaseDate));
                            if (year.HasValue && !game.ReleaseYear.HasValue)
                            {
                                game.ReleaseYear = year.Value;
                            }
                        });
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
            }

            CompareAndSet(match, "Region", game.Region, rom.Region, value => game.Region = value);

            var genres = rom.Genres ?? rom.Metadatum?.Genres;
            if (string.IsNullOrWhiteSpace(game.GenresString) && genres != null && genres.Count > 0)
            {
                var sanitized = genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToList();
                if (sanitized.Count > 0)
                {
                    CompareAndSet(match, "Genres", game.GenresString, string.Join("; ", sanitized), value => game.GenresString = value);
                }
            }

            var developer = rom.Developer;
            if (string.IsNullOrWhiteSpace(developer) && rom.Metadatum?.Companies != null && rom.Metadatum.Companies.Count > 0)
            {
                developer = rom.Metadatum.Companies.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
            CompareAndSet(match, "Developer", game.Developer, developer, value => game.Developer = value);

            var publisher = rom.Publisher;
            if (string.IsNullOrWhiteSpace(publisher) && rom.Metadatum?.Companies != null && rom.Metadatum.Companies.Count > 0)
            {
                publisher = rom.Metadatum.Companies.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
            CompareAndSet(match, "Publisher", game.Publisher, publisher, value => game.Publisher = value);

            var notes = !string.IsNullOrWhiteSpace(rom.Summary) ? rom.Summary : rom.Description;
            CompareAndSet(match, "Notes", game.Notes, notes, value => game.Notes = value);

            var rating = !string.IsNullOrWhiteSpace(rom.Rating)
                ? NormalizeAgeRating(rom.Rating)
                : rom.Metadatum?.AgeRatings != null && rom.Metadatum.AgeRatings.Count > 0
                    ? NormalizeAgeRating(rom.Metadatum.AgeRatings.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)))
                    : null;
            CompareAndSet(match, "Rating", game.Rating, rating, value => game.Rating = value);

            if (!game.MaxPlayers.HasValue && rom.Metadatum?.PlayerCount != null &&
                int.TryParse(rom.Metadatum.PlayerCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxPlayers))
            {
                CompareAndSet(match, "MaxPlayers", game.MaxPlayers?.ToString(CultureInfo.InvariantCulture), maxPlayers.ToString(CultureInfo.InvariantCulture), _ => game.MaxPlayers = maxPlayers);
            }

            if (string.IsNullOrWhiteSpace(game.PlayMode) && rom.Metadatum?.GameModes != null && rom.Metadatum.GameModes.Count > 0)
            {
                var modes = rom.Metadatum.GameModes.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                if (modes.Length > 0)
                {
                    CompareAndSet(match, "PlayMode", game.PlayMode, NormalizePlayMode(modes), value => game.PlayMode = value);
                }
            }

            var series = rom.Metadatum?.Franchises?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            CompareAndSet(match, "Series", game.Series, series, value => game.Series = value);

            if (string.IsNullOrWhiteSpace(game.VideoUrl) && !string.IsNullOrWhiteSpace(rom.YoutubeVideoId))
            {
                CompareAndSet(match, "VideoUrl", game.VideoUrl, "https://www.youtube.com/watch?v=" + rom.YoutubeVideoId, value => game.VideoUrl = value);
            }

            if (string.IsNullOrWhiteSpace(game.ReleaseType))
            {
                CompareAndSet(match, "ReleaseType", game.ReleaseType, "Released", value => game.ReleaseType = value);
            }

            if (string.IsNullOrWhiteSpace(game.Status))
            {
                CompareAndSet(match, "Status", game.Status, "Available Remotely", value => game.Status = value);
            }
        }

        private static void CompareAndSet(GameMatch match, string fieldName, string currentValue, string incomingValue, Action<string> setter)
        {
            if (string.IsNullOrWhiteSpace(incomingValue))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                setter(incomingValue);
                return;
            }

            if (!string.Equals(currentValue, incomingValue, StringComparison.OrdinalIgnoreCase))
            {
                match?.Disparities.Add(fieldName);
            }
        }

        private async Task<RommRom> ResolveRomDetailsAsync(RommRom rom, bool includeMedia, CancellationToken cancellationToken)
        {
            if (rom == null || string.IsNullOrWhiteSpace(rom.Id))
            {
                return rom;
            }

            if (includeMedia || string.IsNullOrWhiteSpace(rom.Description))
            {
                try
                {
                    var details = await _client.GetRomDetailsAsync(rom.Id, cancellationToken).ConfigureAwait(false);
                    if (details != null)
                    {
                        return details;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to fetch RomM details for {rom.Id}: {ex.Message}");
                }
            }

            return rom;
        }

        private async Task<InstallOutcome> InstallLocalRomIfRequestedAsync(RommRom rom, IPlatform launchBoxPlatform, IGame game, bool installLocally, CancellationToken cancellationToken)
        {
            if (!installLocally)
            {
                _logger?.Debug($"Install skipped for '{game?.Title}': InstallLocally=false.");
                return InstallOutcome.Skipped();
            }

            _logger?.Debug($"Install requested for '{game?.Title}'. Resolving download destination and fetching ROM.");
            var downloadOutcome = await TryDownloadRomAsync(rom, launchBoxPlatform, game, cancellationToken).ConfigureAwait(false);
            if (!downloadOutcome.Success)
            {
                return InstallOutcome.Failed(downloadOutcome.Message);
            }

            if (!downloadOutcome.Installed)
            {
                _logger?.Info($"Install deferred for '{game?.Title}': {downloadOutcome.Message}");
                return InstallOutcome.Skipped();
            }

            _logger?.Debug($"Install completed for '{game?.Title}'. ApplicationPath='{game?.ApplicationPath}'.");
            EnsureEmulatorId(_dataManager ?? PluginHelper.DataManager, game, launchBoxPlatform?.Name);
            game.Installed = true;
            return InstallOutcome.InstalledResult();
        }

        private sealed class InstallOutcome
        {
            public bool Success { get; private set; }
            public bool Installed { get; private set; }
            public string Message { get; private set; }

            public static InstallOutcome Skipped()
            {
                return new InstallOutcome { Success = true, Installed = false };
            }

            public static InstallOutcome InstalledResult()
            {
                return new InstallOutcome { Success = true, Installed = true };
            }

            public static InstallOutcome Failed(string message)
            {
                return new InstallOutcome { Success = false, Installed = false, Message = message };
            }
        }

        private void EnsureStubApplicationPath(IDataManager dataManager, IGame game, RommRom rom, string platformName, string displayTitle)
        {
            // Stub file creation removed - ApplicationPath will remain empty for RomM games
            // This prevents showing Install/Play buttons while keeping the game entry
            _logger?.Debug($"ApplicationPath left empty for RomM game '{displayTitle ?? game.Title}' - game will be played through RomM interface.");
        }

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

        private static string ResolveEmulatorId(IDataManager dataManager, string platformName)
        {
            if (dataManager == null || string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            var emulators = dataManager.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true)
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        private void EnsureEmulatorId(IDataManager dataManager, IGame game, string platformName)
        {
            if (game == null)
            {
                return;
            }

            var emulatorId = ResolveEmulatorId(dataManager, platformName);
            if (string.IsNullOrWhiteSpace(emulatorId))
            {
                _logger?.Warning($"No emulator ID resolved for platform '{platformName ?? "Unknown"}' while importing '{game.Title}'.");
                return;
            }

            if (!string.Equals(game.EmulatorId, emulatorId, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info($"Assigning emulator '{emulatorId}' for '{game.Title}' on platform '{platformName}'.");
                game.EmulatorId = emulatorId;
            }
        }

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

        public async Task<(int Downloaded, int Skipped, int Failed, int Total)> ImportSavesForGameAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return (0, 0, 1, 0);
            }

            var (romId, platformId, _) = _installStateService.GetRomMDetails(game);
            if (string.IsNullOrWhiteSpace(romId))
            {
                _logger?.Warning($"Save import skipped for '{game.Title}': missing RomM ID.");
                return (0, 0, 1, 0);
            }

            var platformName = string.IsNullOrWhiteSpace(game.Platform) ? "Unknown" : game.Platform;
            return await ImportSavesAsync(romId, platformId, platformName, game.Title, game, cancellationToken).ConfigureAwait(false);
        }

        private string EnsureGameSubfolder(string baseDirectory, string title)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var folderName = NormalizePathSegment(title);
            var combined = Path.Combine(baseDirectory, folderName);
            Directory.CreateDirectory(combined);
            return combined;
        }

        private string ToLaunchBoxRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                var root = PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(root))
                {
                    return absolutePath;
                }

                var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = absolutePath.Substring(normalizedRoot.Length);
                    return relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
            }
            catch
            {
            }

            return absolutePath;
        }

        private async Task<(bool Success, bool Installed, string Message)> TryDownloadRomAsync(RommRom rom, IPlatform launchBoxPlatform, IGame game, CancellationToken cancellationToken)
        {
            if (rom == null || launchBoxPlatform == null || game == null)
            {
                return (false, false, "Invalid download request.");
            }

            try
            {
                var mapping = _mappingService.GetMapping(rom.PlatformId ?? string.Empty);
                var extractAfterDownload = mapping?.ExtractAfterDownload ?? false;
                var extractionBehavior = mapping?.ExtractionBehavior ?? ExtractionBehavior.Subfolder;
                var installScenario = mapping?.InstallScenario ?? InstallScenario.Basic;
                var targetImportFile = mapping?.TargetImportFile ?? string.Empty;
                var targetImportCandidates = ParseTargetImportFiles(targetImportFile);
                var installerSilentArgs = mapping?.InstallerSilentArgs ?? string.Empty;
                var installDestinationService = new InstallDestinationService(_logger, _settingsManager);
                var installerMode = mapping?.InstallerMode ?? InstallerMode.Manual;
                var installLocation = await installDestinationService
                    .ResolveInstallLocationAsync(game, installerMode, cancellationToken)
                    .ConfigureAwait(false);
                if (!installLocation.Success || string.IsNullOrWhiteSpace(installLocation.InstallDirectory))
                {
                    var message = $"Download skipped for '{game.Title}': {installLocation.Message ?? "install directory unavailable"}.";
                    _logger?.Warning(message);
                    return (false, false, message);
                }

                var downloadDirectory = InstallDestinationService.IsWindowsPlatform(launchBoxPlatform.Name)
                    ? installLocation.InstallDirectory
                    : EnsureGameSubfolder(installLocation.InstallDirectory, game.Title);
                _logger?.Info($"Downloading ROM for '{game.Title}' to '{downloadDirectory}'. Scenario={installScenario}, Extract={extractAfterDownload}, Behavior={extractionBehavior}.");
                var serverUrl = _settingsManager.Load().ServerUrl;
                RomMbox.Models.Download.DownloadResult result = null;
                try
                {
                    var detectInstallType = installScenario != InstallScenario.Basic;
                    result = await _downloadService
                        .DownloadRomAsync(rom, downloadDirectory, serverUrl, extractionBehavior, extractAfterDownload, cancellationToken, null, null, detectInstallType)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _downloadService.TryCleanupTempRoot(result?.TempRoot);
                }

                if (!result.Success)
                {
                    var message = $"Download failed for '{game.Title}': {result.ErrorMessage}";
                    _logger?.Warning(message);
                    return (false, false, message);
                }

                var finalPath = !string.IsNullOrWhiteSpace(result.ExtractedPath)
                    ? result.ExtractedPath
                    : result.ArchivePath;
                if (InstallDestinationService.IsWindowsPlatform(launchBoxPlatform.Name))
                {
                    var installSubsystem = new WindowsInstallSubsystem(_logger, new ArchiveService(_logger, _settingsManager));
                    var installResult = await installSubsystem
                        .InstallAsync(result.ArchivePath, result.ExtractedPath, downloadDirectory, mapping, game.Title, cancellationToken)
                        .ConfigureAwait(false);
                    if (!installResult.Success)
                    {
                        return (false, false, installResult.Message ?? "Windows install failed.");
                    }

                    if (!string.IsNullOrWhiteSpace(installResult.ExecutablePath))
                    {
                        game.ApplicationPath = ToLaunchBoxRelativePath(installResult.ExecutablePath);
                        if (installResult.Arguments?.Count > 0)
                        {
                            game.CommandLine = string.Join(" ", installResult.Arguments);
                        }
                    }

                    if (installResult.InstallType.HasValue)
                    {
                        await _installStateService.UpsertIdentityAsync(
                                game.Id,
                                rom?.Id,
                                rom?.PlatformId,
                                rom?.Md5,
                                localMd5: null,
                                windowsInstallType: installResult.InstallType.Value.ToString(),
                                cancellationToken)
                            .ConfigureAwait(false);
                        _logger?.Info($"Set RomM.WindowsInstallType='{installResult.InstallType.Value}' for '{game.Title}' (DB).");
                    }

                    SaveAndReloadDataManager(_dataManager ?? PluginHelper.DataManager);

                    var installState = new InstallState
                    {
                        LaunchBoxGameId = game.Id,
                        RommRomId = rom?.Id,
                        RommPlatformId = rom?.PlatformId,
                        ServerUrl = _settingsManager.Load().ServerUrl,
                        WindowsInstallType = installResult.InstallType?.ToString(),
                        InstalledPath = installResult.ExecutablePath ?? finalPath,
                        ArchivePath = result.ArchivePath,
                        InstallRootPath = Path.Combine(installLocation.InstallDirectory, NormalizePathSegment(game.Title)),
                        IsInstalled = true,
                        InstalledUtc = DateTimeOffset.UtcNow,
                        LastValidatedUtc = DateTimeOffset.UtcNow
                    };
                    await _installStateService.UpsertStateAsync(installState, cancellationToken).ConfigureAwait(false);

                    return (true, true, string.Empty);
                }

                if (installScenario == InstallScenario.Basic)
                {
                    if (!string.IsNullOrWhiteSpace(result.ExtractedPath))
                    {
                        result.ExtractedPath = InstallContentRelocator.RelocateExtractedContent(result.ExtractedPath, downloadDirectory, _logger);
                        finalPath = result.ExtractedPath;
                    }
                    else if (!string.IsNullOrWhiteSpace(result.ArchivePath))
                    {
                        result.ArchivePath = InstallContentRelocator.RelocateArchive(result.ArchivePath, downloadDirectory, _logger);
                        finalPath = result.ArchivePath;
                    }
                }

                if (installScenario == InstallScenario.Enhanced)
                {
                    finalPath = ResolveTargetFile(result.ExtractedPath, targetImportCandidates, finalPath);
                    if (string.IsNullOrWhiteSpace(finalPath))
                    {
                        return (false, false, "Target import file not found in extracted content.");
                    }
                }
                else if (installScenario == InstallScenario.Installer)
                {
                    var installPath = ExecuteInstaller(result.ExtractedPath, downloadDirectory, targetImportCandidates, installerSilentArgs);
                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        return (false, false, "Installer did not produce a valid install path.");
                    }
                    finalPath = installPath;
                }

                if (!string.IsNullOrWhiteSpace(finalPath))
                {
                    game.ApplicationPath = ToLaunchBoxRelativePath(finalPath);
                }

                return (true, true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Download failed for '{game.Title}'.", ex);
                return (false, false, ex.Message);
            }
        }

        private string ResolveTargetFile(string extractedPath, IReadOnlyList<string> targetImportFiles, string fallbackPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return fallbackPath;
            }

            if (targetImportFiles == null || targetImportFiles.Count == 0)
            {
                return extractedPath;
            }

            try
            {
                var index = BuildFileLookup(extractedPath, targetImportFiles);
                foreach (var candidate in targetImportFiles)
                {
                    if (index.TryGetValue(candidate, out var match))
                    {
                        return match;
                    }
                }
                return fallbackPath;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to locate target import file(s) '{string.Join(", ", targetImportFiles ?? Array.Empty<string>())}'. {ex.Message}");
                return fallbackPath;
            }
        }

        private string ExecuteInstaller(string extractedPath, string installDirectory, IReadOnlyList<string> targetImportFiles, string installerSilentArgs)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return string.Empty;
            }

            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return string.Empty;
            }

            var silentArgs = string.IsNullOrWhiteSpace(installerSilentArgs)
                ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
                : installerSilentArgs;
            var quotedInstall = QuoteArgument(installDirectory);
            var arguments = $"{silentArgs} /DIR={quotedInstall}";

            _logger?.Info($"Launching installer {setupPath} with args: {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    _logger?.Warning("Failed to start installer process.");
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger?.Error($"Installer failed. ExitCode={process.ExitCode}. Output={output}. Error={error}");
                    return string.Empty;
                }
            }

            if (!Directory.Exists(installDirectory))
            {
                return string.Empty;
            }

            if (targetImportFiles != null && targetImportFiles.Count > 0)
            {
                var index = BuildFileLookup(installDirectory, targetImportFiles);
                foreach (var candidate in targetImportFiles)
                {
                    if (index.TryGetValue(candidate, out var targetMatch))
                    {
                        return targetMatch;
                    }
                }
                return installDirectory;
            }

            return installDirectory;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static DateTimeOffset FromUnixTimeSmart(long value)
        {
            if (value > 253402300799)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value);
            }

            return DateTimeOffset.FromUnixTimeSeconds(value);
        }

        private static string NormalizePlayMode(string[] modes)
        {
            if (modes == null || modes.Length == 0)
            {
                return "Single Player";
            }

            foreach (var mode in modes)
            {
                if (mode == null)
                {
                    continue;
                }

                if (mode.IndexOf("co-op", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mode.IndexOf("cooperative", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Cooperative";
                }
            }

            foreach (var mode in modes)
            {
                if (mode == null)
                {
                    continue;
                }

                if (mode.IndexOf("multi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Multiplayer";
                }
            }

            return "Single Player";
        }

        private static string NormalizeAgeRating(string rating)
        {
            if (string.IsNullOrWhiteSpace(rating))
            {
                return "Not Rated";
            }

            var normalized = rating.Trim();
            if (normalized.Equals("E", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
            {
                return "E - Everyone";
            }

            if (normalized.Equals("E10+", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Everyone 10+", StringComparison.OrdinalIgnoreCase))
            {
                return "E10+ - Everyone 10+";
            }

            if (normalized.Equals("T", StringComparison.OrdinalIgnoreCase) ||
                normalized.IndexOf("teen", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "T - Teen";
            }

            if (normalized.Equals("M", StringComparison.OrdinalIgnoreCase) ||
                normalized.IndexOf("mature", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "M - Mature";
            }

            return "Not Rated";
        }

        private static IReadOnlyList<string> ParseTargetImportFiles(string targetImportFile)
        {
            if (string.IsNullOrWhiteSpace(targetImportFile))
            {
                return Array.Empty<string>();
            }

            var separators = new[] { ',', ';', '|' };
            var values = targetImportFile
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            return values.Count == 0 ? Array.Empty<string>() : values;
        }

        internal static int? ExtractYearFromReleaseDate(DateTimeOffset? releaseDate)
        {
            if (!releaseDate.HasValue)
            {
                return null;
            }

            try
            {
                return releaseDate.Value.Year;
            }
            catch
            {
                return null;
            }
        }

        private sealed class GameMatch
        {
            public IGame Game { get; init; }
            public MatchStrategy Strategy { get; init; }
            public double ConfidenceScore { get; init; }
            public string ConfidenceLabel { get; init; }
            public HashSet<string> Disparities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private enum MatchStrategy
        {
            None,
            RommId,
            Md5,
            FileName,
            NormalizedTitle
        }

        internal IGame FindExistingGameForUi(IDataManager dataManager, IPlatform platform, RommRom rom, bool matchByMd5, bool matchByTitle, bool matchByFileName)
        {
            var matchIndex = BuildMatchIndex(dataManager, platform, matchByMd5);
            var match = FindExistingGameMatch(matchIndex, platform, rom, matchByRomId: true, matchByMd5, matchByTitle, matchByFileName);
            return match?.Game;
        }

        internal bool HasRomMTag(IGame game, RommRom rom)
        {
            if (game == null || rom == null)
            {
                return false;
            }

            var (romId, platformId, _, _, _) = _installStateService.GetIdentity(game);

            return !string.IsNullOrWhiteSpace(romId)
                && !string.IsNullOrWhiteSpace(platformId)
                && string.Equals(romId, rom.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(platformId, rom.PlatformId, StringComparison.OrdinalIgnoreCase);
        }

        internal void ApplyMergeTag(IGame game, RommRom rom)
        {
            if (game == null || rom == null)
            {
                return;
            }

            ApplyRomMIdentity(game, rom, match: null);
        }

        private void LogInstalledGamesForPlatform(IReadOnlyCollection<IGame> games, string platformName)
        {
            if (_logger == null || games == null || string.IsNullOrWhiteSpace(platformName))
            {
                return;
            }

            if (_loggedPlatforms.Contains(platformName))
            {
                return;
            }

            _loggedPlatforms.Add(platformName);

            var installedGames = games
                .Where(game => game != null
                    && string.Equals(game.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                    && (game.Installed == true || !string.IsNullOrWhiteSpace(game.ApplicationPath)))
                .Select(game => game.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(title => title)
                .ToList();

            _logger.Debug($"Installed games for platform '{platformName}': {installedGames.Count} [" + string.Join(" | ", installedGames) + "]");
        }

        private static void SaveAndReloadDataManager(IDataManager dataManager)
        {
            if (dataManager == null)
            {
                return;
            }
            dataManager.Save(true);
            dataManager.ReloadIfNeeded();
            dataManager.ForceReload();
        }

        internal MatchIndex BuildMatchIndexForUi(IDataManager dataManager, IPlatform platform, bool includeMd5)
        {
            return BuildMatchIndex(dataManager, platform, includeMd5);
        }

        internal (IGame Game, string Strategy, string Confidence) EvaluateMatchForUi(MatchIndex index, IPlatform platform, RommRom rom, bool matchByRomId, bool matchByMd5, bool matchByTitle, bool matchByFileName)
        {
            var match = FindExistingGameMatch(index, platform, rom, matchByRomId, matchByMd5, matchByTitle, matchByFileName);
            return match == null
                ? (null, string.Empty, string.Empty)
                : (match.Game, match.Strategy.ToString(), match.ConfidenceLabel ?? string.Empty);
        }

        private MatchIndex BuildMatchIndex(IDataManager dataManager, IPlatform platform, bool includeMd5)
        {
            var index = new MatchIndex();
            if (dataManager == null || platform == null)
            {
                return index;
            }

            var platformName = platform.Name ?? string.Empty;
            var games = dataManager.GetAllGames() ?? Array.Empty<IGame>();
            LogInstalledGamesForPlatform(games, platformName);
            foreach (var game in games)
            {
                if (game == null || !string.Equals(game.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var identity = _installStateService.GetIdentity(game);
                if (!string.IsNullOrWhiteSpace(identity.RommRomId))
                {
                    index.ByRomId[identity.RommRomId] = game;
                }

                var localMd5 = identity.LocalMd5;
                if (string.IsNullOrWhiteSpace(localMd5) && includeMd5)
                {
                    localMd5 = GetOrComputeLocalMd5(game);
                }
                if (!string.IsNullOrWhiteSpace(localMd5))
                {
                    index.ByMd5[localMd5] = game;
                }

                if (!string.IsNullOrWhiteSpace(game.Title))
                {
                    var normalized = NormalizeTitleForMatch(game.Title);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        if (!index.ByNormalizedTitle.TryGetValue(normalized, out var list))
                        {
                            list = new List<IGame>();
                            index.ByNormalizedTitle[normalized] = list;
                        }
                        list.Add(game);
                    }
                }

                var applicationFile = ResolveFileNameFromPath(game.ApplicationPath);
                if (!string.IsNullOrWhiteSpace(applicationFile))
                {
                    index.ByFileName[applicationFile] = game;
                }

                index.TitleCandidates.Add(game);
            }

            _logger?.Debug($"Match index built for '{platformName}': RomId={index.ByRomId.Count}, Md5={index.ByMd5.Count}, FileName={index.ByFileName.Count}, Title={index.ByNormalizedTitle.Count}.");
            return index;
        }

        private GameMatch FindExistingGameMatch(MatchIndex index, IPlatform platform, RommRom rom, bool matchByRomId, bool matchByMd5, bool matchByTitle, bool matchByFileName)
        {
            if (index == null || platform == null || rom == null)
            {
                return null;
            }

            var normalizedRomTitle = NormalizeTitleForMatch(rom.DisplayTitle ?? rom.Title ?? string.Empty);
            var romFileName = ResolveRomFileName(rom);

            if (matchByRomId && !string.IsNullOrWhiteSpace(rom.Id) && index.ByRomId.TryGetValue(rom.Id, out var romIdGame))
            {
                return new GameMatch
                {
                    Game = romIdGame,
                    Strategy = MatchStrategy.RommId,
                    ConfidenceScore = 1.0,
                    ConfidenceLabel = "Exact"
                };
            }

            if (matchByMd5 && !string.IsNullOrWhiteSpace(rom.Md5) && index.ByMd5.TryGetValue(rom.Md5, out var md5Game))
            {
                return new GameMatch
                {
                    Game = md5Game,
                    Strategy = MatchStrategy.Md5,
                    ConfidenceScore = 0.95,
                    ConfidenceLabel = "High"
                };
            }

            if (matchByFileName && !string.IsNullOrWhiteSpace(romFileName) && index.ByFileName.TryGetValue(romFileName, out var fileGame))
            {
                return new GameMatch
                {
                    Game = fileGame,
                    Strategy = MatchStrategy.FileName,
                    ConfidenceScore = 0.85,
                    ConfidenceLabel = "High"
                };
            }

            if (matchByTitle && !string.IsNullOrWhiteSpace(normalizedRomTitle))
            {
                if (index.ByNormalizedTitle.TryGetValue(normalizedRomTitle, out var exactGames) && exactGames.Count > 0)
                {
                    return new GameMatch
                    {
                        Game = exactGames[0],
                        Strategy = MatchStrategy.NormalizedTitle,
                        ConfidenceScore = 0.7,
                        ConfidenceLabel = "Medium"
                    };
                }

                GameMatch bestTitleMatch = null;
                var bestTitleScore = 0.0;
                foreach (var candidate in index.TitleCandidates)
                {
                    var normalizedGameTitle = NormalizeTitleForMatch(candidate.Title ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(normalizedGameTitle))
                    {
                        continue;
                    }

                    var score = ComputeTitleSimilarity(normalizedGameTitle, normalizedRomTitle);
                    if (score > bestTitleScore)
                    {
                        bestTitleScore = score;
                        bestTitleMatch = new GameMatch
                        {
                            Game = candidate,
                            Strategy = MatchStrategy.NormalizedTitle,
                            ConfidenceScore = score,
                            ConfidenceLabel = score >= 0.8 ? "High" : score >= 0.7 ? "Medium" : "Low"
                        };
                    }
                }

                if (bestTitleMatch != null && bestTitleScore >= 0.72)
                {
                    return bestTitleMatch;
                }
            }

            return null;
        }

        private static Dictionary<string, string> BuildFileLookup(string rootPath, IReadOnlyList<string> targetImportFiles)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rootPath) || targetImportFiles == null || targetImportFiles.Count == 0)
            {
                return index;
            }

            var targets = new HashSet<string>(targetImportFiles.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            if (targets.Count == 0)
            {
                return index;
            }

            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName) || !targets.Contains(fileName))
                {
                    continue;
                }

                if (!index.TryGetValue(fileName, out var existing) || path.Length < existing.Length)
                {
                    index[fileName] = path;
                }
            }

            return index;
        }

        internal sealed class MatchIndex
        {
            public Dictionary<string, IGame> ByRomId { get; } = new Dictionary<string, IGame>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, IGame> ByMd5 { get; } = new Dictionary<string, IGame>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, IGame> ByFileName { get; } = new Dictionary<string, IGame>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<IGame>> ByNormalizedTitle { get; } = new Dictionary<string, List<IGame>>(StringComparer.OrdinalIgnoreCase);
            public List<IGame> TitleCandidates { get; } = new List<IGame>();
        }

        private static string ResolveRomFileName(RommRom rom)
        {
            if (rom == null)
            {
                return string.Empty;
            }

            var name = rom.Payload?.FileName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = rom.FsName;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = rom.Title;
            }

            return ResolveFileNameFromPath(name);
        }

        private static string ResolveFileNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(path.Trim());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeTitleForMatch(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var value = title.Trim();
            value = StripBracketedSegments(value);
            value = value.Replace("&", "and", StringComparison.OrdinalIgnoreCase);
            value = value.Normalize(NormalizationForm.FormD);
            var normalized = new string(value
                .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                .Select(ch => char.ToLowerInvariant(ch))
                .ToArray());
            normalized = string.Join(" ", normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return normalized;
        }

        private static string StripBracketedSegments(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            Span<char> buffer = stackalloc char[value.Length];
            var index = 0;
            var depth = 0;
            foreach (var ch in value)
            {
                if (ch == '(' || ch == '[' || ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch == ')' || ch == ']' || ch == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    continue;
                }

                if (depth == 0)
                {
                    buffer[index++] = ch;
                }
            }

            return new string(buffer.Slice(0, index));
        }

        private static double ComputeTitleSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0.0;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (leftTokens.Length == 0 || rightTokens.Length == 0)
            {
                return 0.0;
            }

            var leftSet = new HashSet<string>(leftTokens, StringComparer.OrdinalIgnoreCase);
            var rightSet = new HashSet<string>(rightTokens, StringComparer.OrdinalIgnoreCase);
            var intersection = leftSet.Intersect(rightSet, StringComparer.OrdinalIgnoreCase).Count();
            var union = leftSet.Union(rightSet, StringComparer.OrdinalIgnoreCase).Count();
            if (union == 0)
            {
                return 0.0;
            }

            return (double)intersection / union;
        }

        private string GetOrComputeLocalMd5(IGame game)
        {
            if (game == null)
            {
                return string.Empty;
            }

            var existing = _installStateService.GetIdentity(game).LocalMd5;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(game.ApplicationPath))
            {
                _logger?.Warning($"MD5 skipped for '{game.Title}': ApplicationPath is missing or invalid. Path='<null>'.");
                return string.Empty;
            }

            var resolvedPath = game.ApplicationPath;
            try
            {
                if (!Path.IsPathRooted(resolvedPath))
                {
                    var root = PluginPaths.GetLaunchBoxRootDirectory();
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        resolvedPath = Path.Combine(root, resolvedPath);
                    }
                }
            }
            catch
            {
            }

            if (!File.Exists(resolvedPath))
            {
                _logger?.Warning($"MD5 skipped for '{game.Title}': ApplicationPath is missing or invalid. Path='{game.ApplicationPath}', Resolved='{resolvedPath}'.");
                return string.Empty;
            }

            try
            {
                using var stream = File.OpenRead(resolvedPath);
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(stream);
                var value = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                _installStateService.UpdateLocalMd5Async(game.Id, value, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                return value;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"MD5 failed for '{game.Title}': {ex.Message}");
                return string.Empty;
            }
        }

        private async Task DownloadMediaAsync(IPlatform platform, IGame game, RommRom rom, CancellationToken cancellationToken)
        {
            if (platform == null || game == null || rom == null)
            {
                return;
            }

            _logger?.Debug($"Attempting media download for {game.Title} ({rom.Id}).");
            var coverUrl = BuildAbsoluteMediaUrl(
                rom.PathCoverLarge,
                rom.PathCoverSmall,
                rom.Media?.BoxFrontUrl,
                rom.Media?.CoverUrl,
                rom.UrlCover);
            if (string.IsNullOrWhiteSpace(coverUrl))
            {
                _logger?.Debug($"No cover URL available for {game.Title}. BoxFrontUrl='{rom.Media?.BoxFrontUrl}', CoverUrl='{rom.Media?.CoverUrl}', UrlCover='{rom.UrlCover}', PathCoverLarge='{rom.PathCoverLarge}', PathCoverSmall='{rom.PathCoverSmall}'.");
            }
            else
            {
                try
                {
                    _logger?.Info($"Downloading cover art: {coverUrl}");
                    var bytes = await _client.DownloadMediaAsync(coverUrl, cancellationToken).ConfigureAwait(false);
                    var extension = GetFileExtension(coverUrl);
                    var path = game.GetNextAvailableImageFilePath(extension, ImageTypes.BoxFront, game.Region ?? string.Empty);
                    _logger?.Info($"Writing cover art to: {path}");
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                    // FrontImagePath is read-only on IGame; rely on file placement.
                    var platformFolder = platform?.FrontImagesFolder;
                    if (!string.IsNullOrWhiteSpace(platformFolder))
                    {
                        TryCopyMedia(path, platformFolder);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to download cover art for {game.Title}: {ex.Message}");
                }
            }

            var screenshotUrl = BuildAbsoluteMediaUrl(
                rom.Media?.ScreenshotUrl,
                rom.MergedScreenshots?.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(screenshotUrl))
            {
                try
                {
                    _logger?.Info($"Downloading screenshot: {screenshotUrl}");
                    var bytes = await _client.DownloadMediaAsync(screenshotUrl, cancellationToken).ConfigureAwait(false);
                    var extension = GetFileExtension(screenshotUrl);
                    var path = game.GetNextAvailableImageFilePath(extension, ImageTypes.ScreenshotGameplay, game.Region ?? string.Empty);
                    _logger?.Info($"Writing screenshot to: {path}");
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                    // ScreenshotImagePath is read-only on IGame; rely on file placement.
                    var platformFolder = platform?.ScreenshotImagesFolder;
                    if (!string.IsNullOrWhiteSpace(platformFolder))
                    {
                        TryCopyMedia(path, platformFolder);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to download screenshot for {game.Title}: {ex.Message}");
                }
            }
        }

        private string BuildAbsoluteMediaUrl(params string[] candidates)
        {
            if (candidates == null)
            {
                return string.Empty;
            }

            var settings = _settingsManager.Load();
            var serverUrl = settings?.ServerUrl ?? string.Empty;
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
                {
                    return absolute.ToString();
                }

                if (!string.IsNullOrWhiteSpace(serverUrl) && Uri.TryCreate(new Uri(serverUrl), candidate, out var combined))
                {
                    return combined.ToString();
                }
            }

            return string.Empty;
        }

        private static string GetFileExtension(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return ".jpg";
            }

            var extension = string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                extension = Path.GetExtension(absolute.AbsolutePath);
            }
            else
            {
                var trimmed = url.Split('?')[0];
                extension = Path.GetExtension(trimmed);
            }
            if (string.IsNullOrWhiteSpace(extension))
            {
                return ".jpg";
            }

            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }

        private static void TryCopyMedia(string sourcePath, string destinationFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationFolder))
                {
                    return;
                }

                if (!File.Exists(sourcePath))
                {
                    return;
                }

                Directory.CreateDirectory(destinationFolder);
                var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath, true);
            }
            catch
            {
            }
        }

        private void ExecuteWithRetry(Action action, string operationName, int maxAttempts = 5)
        {
            if (action == null)
            {
                return;
            }

            var attempt = 0;
            var delayMs = 150;
            while (true)
            {
                try
                {
                    attempt++;
                    action();
                    if (attempt > 1)
                    {
                        _logger?.Info($"{operationName} succeeded after {attempt} attempt(s).");
                    }
                    return;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    _logger?.Warning($"{operationName} failed due to IO lock (attempt {attempt} of {maxAttempts}): {ex.Message}");
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    _logger?.Warning($"{operationName} failed (attempt {attempt} of {maxAttempts}): {ex.Message}");
                }

                try
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch
                {
                }
                delayMs = Math.Min(delayMs * 2, 1500);
            }
        }

        private async Task<(int Downloaded, int Skipped, int Failed, int Total)> ImportSavesForRomAsync(RommRom rom, IPlatform platform, IGame game, CancellationToken cancellationToken)
        {
            if (rom == null)
            {
                return (0, 0, 1, 0);
            }

            var platformName = platform?.Name ?? game?.Platform ?? "Unknown";
            return await ImportSavesAsync(rom.Id, rom.PlatformId, platformName, game?.Title ?? rom.DisplayTitle ?? rom.Title, game, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<(int Downloaded, int Skipped, int Failed, int Total)> ImportSavesAsync(
            string romId,
            string platformId,
            string platformName,
            string gameTitle,
            IGame game,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(romId))
            {
                return (0, 0, 1, 0);
            }

            var saves = await _client.ListSavesAsync(romId, platformId, cancellationToken).ConfigureAwait(false);
            if (saves == null || saves.Count == 0)
            {
                _logger?.Info($"No saves available for RomM ID {romId}.");
                return (0, 0, 0, 0);
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                _logger?.Warning($"Save import skipped for RomM ID {romId}: LaunchBox root not resolved.");
                return (0, 0, saves.Count, saves.Count);
            }

            var platformSaveRoot = Path.Combine(launchBoxRoot, "Saves", NormalizePathSegment(platformName));
            Directory.CreateDirectory(platformSaveRoot);
            var saveRoot = EnsureGameSaveFolder(platformSaveRoot, gameTitle);
            var rootSavePath = platformSaveRoot == saveRoot ? string.Empty : platformSaveRoot;

            var downloaded = 0;
            var skipped = 0;
            var failed = 0;
            var saveEntries = new List<GameSaveEntry>();
            foreach (var save in saves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (save == null)
                {
                    failed++;
                    continue;
                }

                if (save.MissingFromFs)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var fileName = BuildSaveFileName(save, gameTitle);
                    var targetPath = EnsureUniqueFilePath(saveRoot, fileName);
                    var bytes = await _client.DownloadSaveAsync(save.DownloadPath, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
                    var rootCopyPath = string.IsNullOrWhiteSpace(rootSavePath)
                        ? targetPath
                        : EnsureUniqueFilePath(rootSavePath, fileName);
                    if (!string.Equals(rootCopyPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        await File.WriteAllBytesAsync(rootCopyPath, bytes, cancellationToken).ConfigureAwait(false);
                    }
                    var relativePath = BuildLaunchBoxSaveRelativePath(platformName, Path.GetFileName(rootCopyPath));
                    saveEntries.Add(new GameSaveEntry
                    {
                        GameId = game?.Id ?? string.Empty,
                        EmulatorFileName = ResolveEmulatorFileName(_dataManager ?? PluginHelper.DataManager, game, platformName),
                        EmulatorCore = ResolveEmulatorCore(_dataManager ?? PluginHelper.DataManager, game, platformName),
                        Title = BuildSaveTitle(save, gameTitle),
                        FilePath = relativePath,
                        OriginalFileName = save?.FileName ?? save?.FileNameNoTags ?? save?.FileNameNoExt ?? string.Empty
                    });
                    downloaded++;
                    _logger?.Info($"Imported save for RomM ID {romId}: {targetPath}");
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger?.Warning($"Failed to import save for RomM ID {romId}: {ex.Message}");
                }
            }

            TryWriteGameSaveEntries(platformName, saveEntries);
            return (downloaded, skipped, failed, saves.Count);
        }

        private static string BuildSaveFileName(RommSave save, string gameTitle)
        {
            var candidate = save?.FileName;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = save?.FileNameNoTags;
            }

            var extension = Path.GetExtension(candidate ?? string.Empty);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = save?.FileNameNoExt ?? gameTitle ?? "Save";
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = NormalizeExtension(save?.FileExtension);
            }

            var sanitized = SanitizeFileName(candidate);
            if (!string.IsNullOrWhiteSpace(extension) && !sanitized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                sanitized += extension;
            }

            return string.IsNullOrWhiteSpace(sanitized) ? "Save" + NormalizeExtension(save?.FileExtension) : sanitized;
        }

        private static string EnsureGameSaveFolder(string platformSaveRoot, string gameTitle)
        {
            if (string.IsNullOrWhiteSpace(platformSaveRoot))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(gameTitle))
            {
                return platformSaveRoot;
            }

            var folderName = NormalizePathSegment(gameTitle);
            var gameSaveRoot = Path.Combine(platformSaveRoot, folderName);
            Directory.CreateDirectory(gameSaveRoot);
            return gameSaveRoot;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return ".sav";
            }

            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? string.Empty : cleaned.Trim();
        }

        private static string EnsureUniqueFilePath(string directory, string fileName)
        {
            var targetPath = Path.Combine(directory, fileName);
            if (!File.Exists(targetPath))
            {
                return targetPath;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(directory, $"{baseName} ({counter}){extension}");
                counter++;
            }

            return targetPath;
        }

        private sealed class GameSaveEntry
        {
            public string GameId { get; init; }
            public string EmulatorFileName { get; init; }
            public string EmulatorCore { get; init; }
            public string Title { get; init; }
            public string FilePath { get; init; }
            public string OriginalFileName { get; init; }
        }

        private static string BuildLaunchBoxSaveRelativePath(string platformName, string fileName)
        {
            var sanitizedPlatform = NormalizePathSegment(platformName);
            var sanitizedFile = NormalizePathSegment(Path.GetFileName(fileName));
            var relative = Path.Combine("Saves", sanitizedPlatform, sanitizedFile);
            return relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static string BuildSaveTitle(RommSave save, string gameTitle)
        {
            return "RomM: Save Game";
        }

        private string ResolveEmulatorFileName(IDataManager dataManager, IGame game, string platformName)
        {
            var emulator = ResolveEmulator(dataManager, game, platformName);
            if (emulator == null || string.IsNullOrWhiteSpace(emulator.ApplicationPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(emulator.ApplicationPath);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveEmulatorCore(IDataManager dataManager, IGame game, string platformName)
        {
            var emulator = ResolveEmulator(dataManager, game, platformName);
            if (emulator == null)
            {
                return string.Empty;
            }

            var commandLine = ResolveEmulatorCommandLine(emulator, platformName);
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return string.Empty;
            }

            var coreArg = ExtractCommandLineValue(commandLine, "-L");
            if (string.IsNullOrWhiteSpace(coreArg))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileNameWithoutExtension(coreArg);
            }
            catch
            {
                return coreArg.Trim();
            }
        }

        private static IEmulator ResolveEmulator(IDataManager dataManager, IGame game, string platformName)
        {
            if (dataManager == null)
            {
                return null;
            }

            if (game != null && !string.IsNullOrWhiteSpace(game.EmulatorId))
            {
                return dataManager.GetEmulatorById(game.EmulatorId);
            }

            var emulators = dataManager.GetAllEmulators() ?? Array.Empty<IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true)
                    {
                        return emulator;
                    }
                }
            }

            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return emulator;
                    }
                }
            }

            return null;
        }

        private static string ResolveEmulatorCommandLine(IEmulator emulator, string platformName)
        {
            if (emulator == null)
            {
                return string.Empty;
            }

            var platforms = emulator.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            var platformEntry = platforms.FirstOrDefault(platform =>
                string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                && platform?.IsDefault == true)
                ?? platforms.FirstOrDefault(platform =>
                    string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase));

            return platformEntry?.CommandLine ?? emulator.CommandLine ?? string.Empty;
        }

        private static string ExtractCommandLineValue(string commandLine, string flag)
        {
            if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(flag))
            {
                return string.Empty;
            }

            var tokens = TokenizeCommandLine(commandLine).ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!string.Equals(tokens[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < tokens.Count)
                {
                    return tokens[i + 1];
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> TokenizeCommandLine(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                yield break;
            }

            var current = new StringBuilder();
            var inQuotes = false;
            foreach (var ch in commandLine)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        private void TryWriteGameSaveEntries(string platformName, IReadOnlyList<GameSaveEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return;
            }

            var platformPath = ResolvePlatformXmlPath(platformName);
            if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
            {
                _logger?.Warning($"Platform XML not found for '{platformName}'. Save entries not written.");
                return;
            }

            try
            {
                var xml = File.ReadAllText(platformPath);
                var updated = UpsertGameSaveEntries(xml, entries);
                if (!string.Equals(xml, updated, StringComparison.Ordinal))
                {
                    WritePlatformXmlSafely(platformPath, updated);
                    _logger?.Info($"Added {entries.Count} save entry(ies) to '{platformName}' platform XML.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to update platform XML for '{platformName}': {ex.Message}");
            }
        }

        private static string ResolvePlatformXmlPath(string launchBoxPlatformName)
        {
            if (string.IsNullOrWhiteSpace(launchBoxPlatformName))
            {
                return string.Empty;
            }

            var root = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            var fileName = SanitizePlatformFileName(launchBoxPlatformName);
            return Path.Combine(root, "Data", "Platforms", fileName + ".xml");
        }

        private static string SanitizePlatformFileName(string platformName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(platformName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return sanitized.Trim();
        }

        private static string UpsertGameSaveEntries(string xml, IReadOnlyList<GameSaveEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return xml;
            }

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                var root = doc.Root;
                if (root == null)
                {
                    return xml;
                }

                var existing = root.Elements("GameSave")
                    .Select(element => new
                    {
                        Element = element,
                        GameId = element.Element("GameId")?.Value ?? string.Empty,
                        FileLocation = element.Element("FileLocation")?.Value
                            ?? element.Element("FilePath")?.Value
                            ?? string.Empty
                    })
                    .ToList();

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.GameId) || string.IsNullOrWhiteSpace(entry.FilePath))
                    {
                        continue;
                    }

                    var exists = existing.Any(item =>
                        string.Equals(item.GameId, entry.GameId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.FileLocation, entry.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (exists)
                    {
                        continue;
                    }

                    var gameSave = new XElement("GameSave",
                        new XElement("GameId", entry.GameId),
                        new XElement("EmulatorFileName", entry.EmulatorFileName ?? string.Empty),
                        new XElement("EmulatorCore", entry.EmulatorCore ?? string.Empty),
                        new XElement("Title", entry.Title ?? string.Empty),
                        new XElement("FileLocation", entry.FilePath ?? string.Empty),
                        new XElement("OriginalFileName", entry.OriginalFileName ?? string.Empty));
                    root.Add(gameSave);
                }

                doc.Declaration = new XDeclaration("1.0", "utf-8", "yes");
                using var memoryStream = new MemoryStream();
                using (var streamWriter = new StreamWriter(memoryStream, new UnicodeEncoding(false, false), 1024, true))
                {
                    doc.Save(streamWriter, SaveOptions.DisableFormatting);
                }

                return Encoding.Unicode.GetString(memoryStream.ToArray());
            }
            catch
            {
                return xml;
            }
        }

        private static void WritePlatformXmlSafely(string platformPath, string xml)
        {
            if (string.IsNullOrWhiteSpace(platformPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(platformPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                File.WriteAllText(platformPath, xml);
                return;
            }

            var fileName = Path.GetFileName(platformPath);
            var tempPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
            var backupPath = Path.Combine(directory, $"{fileName}.bak");

            File.WriteAllText(tempPath, xml, new UnicodeEncoding(false, false));
            try
            {
                File.Replace(tempPath, platformPath, backupPath, true);
            }
            catch
            {
                File.WriteAllText(platformPath, xml, new UnicodeEncoding(false, false));
                File.Delete(tempPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
