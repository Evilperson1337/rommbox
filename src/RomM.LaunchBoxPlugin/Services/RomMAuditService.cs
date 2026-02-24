using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Audit;
using RomMbox.Models.Romm;
using RomMbox.Plugin;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    /// <summary>
    /// Executes platform-level RomM audits with controlled parallelism and structured logging.
    /// </summary>
    internal sealed class RomMAuditService
    {
        private static readonly SemaphoreSlim AuditGuard = new SemaphoreSlim(1, 1);
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly InstallStateService _installStateService;
        private readonly PlatformMappingService _mappingService;
        private readonly ImportService _importService;

        public RomMAuditService(
            LoggingService logger,
            SettingsManager settingsManager,
            InstallStateService installStateService,
            PlatformMappingService mappingService,
            ImportService importService)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _installStateService = installStateService;
            _mappingService = mappingService;
            _importService = importService;
        }

        public static RomMAuditService CreateDefault()
        {
            PluginEntry.EnsureInitialized();
            var logger = PluginEntry.Logger;
            var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(logger);
            var client = new RommClient(logger, settingsManager, requireServerUrl: true);
            var mappingService = new PlatformMappingService(logger, settingsManager, client);
            var importService = new ImportService(logger, settingsManager, mappingService, client);
            var installState = PluginEntry.InstallStateService ?? new InstallStateService(logger, settingsManager);
            return new RomMAuditService(logger, settingsManager, installState, mappingService, importService);
        }

        public async Task<RomMAuditResult> RunAuditAsync(
            RomMAuditRequest request,
            IProgress<RomMAuditProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;
            request.CorrelationId = correlationId;

            var startedUtc = DateTimeOffset.UtcNow;
            var result = new RomMAuditResult
            {
                CorrelationId = correlationId,
                StartedUtc = startedUtc
            };

            var guardAcquired = false;
            try
            {
                guardAcquired = await AuditGuard.WaitAsync(0, cancellationToken).ConfigureAwait(false);
                if (!guardAcquired)
                {
                    _logger?.Write(LogLevel.Warning, "AuditAlreadyRunning", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("Platform", request.LaunchBoxPlatformName ?? string.Empty)));
                    result.Failed = true;
                    result.FailureMessage = "Another audit is already running.";
                    result.CompletedUtc = DateTimeOffset.UtcNow;
                    return result;
                }

                using (_logger?.BeginOperation(correlationId))
                {
                    var dataManager = PluginHelper.DataManager;
                    if (dataManager == null)
                    {
                        _logger?.Write(LogLevel.Error, "AuditDataManagerMissing", null,
                            BuildProps(
                                ("CorrelationId", correlationId),
                                ("Platform", request.LaunchBoxPlatformName ?? string.Empty)));
                        result.Failed = true;
                        result.FailureMessage = "LaunchBox data services are unavailable.";
                        result.CompletedUtc = DateTimeOffset.UtcNow;
                        return result;
                    }

                    var platformName = request.LaunchBoxPlatformName ?? string.Empty;
                    var platform = dataManager.GetPlatformByName(platformName);
                    if (platform == null)
                    {
                        _logger?.Write(LogLevel.Warning, "AuditPlatformNotFound", null,
                            BuildProps(
                                ("CorrelationId", correlationId),
                                ("Platform", platformName)));
                        result.Failed = true;
                        result.FailureMessage = "Selected platform not found.";
                        result.CompletedUtc = DateTimeOffset.UtcNow;
                        return result;
                    }

                    var games = dataManager.GetAllGames()
                        ?.Where(game => game != null && string.Equals(game.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                        .ToList()
                        ?? new List<IGame>();

                    var totalGames = games.Count;
                    result.Summary.TotalGames = totalGames;

                    _logger?.Write(LogLevel.Info, "AuditStarted", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("Platform", platformName),
                            ("TotalGames", totalGames)));

                    if (totalGames == 0)
                    {
                        result.CompletedUtc = DateTimeOffset.UtcNow;
                        return result;
                    }

                    var options = request.Options ?? new RomMAuditOptions();
                    var maxParallel = Math.Max(1, options.MaxParallelism);
                    var throttleDelay = Math.Max(0, options.ApiDelayMs);

                    var matchIndex = _importService.BuildMatchIndexForUi(dataManager, platform, includeMd5: true);
                    var candidatesByNormalizedTitle = await LoadPlatformRomCandidatesAsync(request.RommPlatformId, cancellationToken)
                        .ConfigureAwait(false);

                    var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
                    var tasks = new List<Task>();
                    var counter = 0;
                    var updated = 0;
                    var unchanged = 0;
                    var failed = 0;
                    var missing = 0;
                    var gameResults = new ConcurrentBag<RomMAuditGameResult>();

                    foreach (var game in games)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                var gameResult = await AuditGameAsync(
                                        request,
                                        platform,
                                        matchIndex,
                                        candidatesByNormalizedTitle,
                                        game,
                                        options,
                                        throttleDelay,
                                        cancellationToken)
                                    .ConfigureAwait(false);

                                gameResults.Add(gameResult);
                                switch (gameResult.Outcome)
                                {
                                    case RomMAuditOutcome.Updated:
                                        Interlocked.Increment(ref updated);
                                        break;
                                    case RomMAuditOutcome.NotFound:
                                        Interlocked.Increment(ref missing);
                                        break;
                                    case RomMAuditOutcome.Failed:
                                        Interlocked.Increment(ref failed);
                                        break;
                                    default:
                                        Interlocked.Increment(ref unchanged);
                                        break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                Interlocked.Increment(ref failed);
                                _logger?.Write(LogLevel.Warning, "GameAuditCancelled", null,
                                    BuildProps(
                                        ("CorrelationId", correlationId),
                                        ("GameTitle", game?.Title ?? string.Empty),
                                        ("Platform", platformName)));
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref failed);
                                _logger?.Write(LogLevel.Error, "GameAuditFailed", ex,
                                    BuildProps(
                                        ("CorrelationId", correlationId),
                                        ("GameTitle", game?.Title ?? string.Empty),
                                        ("Platform", platformName)));
                            }
                            finally
                            {
                                var processed = Interlocked.Increment(ref counter);
                                progress?.Report(new RomMAuditProgress
                                {
                                    CorrelationId = correlationId,
                                    PlatformName = platformName,
                                    CurrentGameTitle = game?.Title ?? string.Empty,
                                    TotalGames = totalGames,
                                    Processed = processed,
                                    Updated = updated,
                                    Unchanged = unchanged,
                                    Failed = failed,
                                    MissingMatches = missing,
                                    PercentComplete = totalGames == 0 ? 100 : processed * 100.0 / totalGames
                                });
                                semaphore.Release();
                            }
                        }, cancellationToken);
                        tasks.Add(task);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    result.GameResults = gameResults.OrderBy(entry => entry.GameTitle).ToList();
                    result.Summary.Updated = updated;
                    result.Summary.Unchanged = unchanged;
                    result.Summary.Failed = failed;
                    result.Summary.MissingMatches = missing;
                    result.CompletedUtc = DateTimeOffset.UtcNow;
                    result.Summary.Duration = result.CompletedUtc - result.StartedUtc;

                    _logger?.Write(LogLevel.Info, "AuditCompleted", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("Platform", platformName),
                            ("DurationMs", (long)result.Summary.Duration.TotalMilliseconds),
                            ("UpdatedCount", updated),
                            ("FailedCount", failed),
                            ("MissingMatches", missing)));
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                result.CompletedUtc = DateTimeOffset.UtcNow;
                result.Summary.Duration = result.CompletedUtc - result.StartedUtc;
                _logger?.Write(LogLevel.Warning, "AuditCancelled", null,
                    BuildProps(
                        ("CorrelationId", correlationId),
                        ("Platform", request.LaunchBoxPlatformName ?? string.Empty),
                        ("DurationMs", (long)result.Summary.Duration.TotalMilliseconds)));
                return result;
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.FailureMessage = ex.Message;
                result.CompletedUtc = DateTimeOffset.UtcNow;
                result.Summary.Duration = result.CompletedUtc - result.StartedUtc;
                _logger?.Write(LogLevel.Error, "AuditFailed", ex,
                    BuildProps(
                        ("CorrelationId", correlationId),
                        ("Platform", request.LaunchBoxPlatformName ?? string.Empty)));
                return result;
            }
            finally
            {
                if (guardAcquired)
                {
                    AuditGuard.Release();
                }
            }
        }

        private async Task<RomMAuditGameResult> AuditGameAsync(
            RomMAuditRequest request,
            IPlatform platform,
            ImportService.MatchIndex matchIndex,
            IReadOnlyDictionary<string, List<RommRom>> candidatesByNormalizedTitle,
            IGame game,
            RomMAuditOptions options,
            int throttleDelay,
            CancellationToken cancellationToken)
        {
            var result = new RomMAuditGameResult
            {
                GameId = game?.Id ?? string.Empty,
                GameTitle = game?.Title ?? string.Empty,
                PlatformName = platform?.Name ?? string.Empty
            };

            if (game == null)
            {
                result.Outcome = RomMAuditOutcome.Failed;
                result.ErrorMessage = "Game was null.";
                return result;
            }

            var stopwatch = Stopwatch.StartNew();
            var correlationId = request.CorrelationId ?? string.Empty;
            var platformName = platform?.Name ?? string.Empty;
            try
            {
                _logger?.Write(LogLevel.Debug, "GameAuditStarted", null,
                    BuildProps(
                        ("CorrelationId", correlationId),
                        ("GameTitle", game.Title),
                        ("Platform", platformName)));

                var identity = _installStateService.GetIdentity(game);
                var oldRomId = identity.RommRomId ?? string.Empty;
                result.OldRommId = oldRomId;

                var shouldAudit = ShouldAuditGame(oldRomId, options);
                if (!shouldAudit)
                {
                    result.Outcome = RomMAuditOutcome.Skipped;
                    return result;
                }

                var match = FindBestMatch(matchIndex, platform, candidatesByNormalizedTitle, game);
                if (match == null || match.Rom == null || string.IsNullOrWhiteSpace(match.Rom.Id))
                {
                    result.Outcome = RomMAuditOutcome.NotFound;
                    return result;
                }

                result.NewRommId = match.Rom.Id;
                result.MatchStrategy = match.Strategy ?? string.Empty;
                result.MatchConfidence = match.Confidence ?? string.Empty;

                if (!options.ForceFullRematch && string.Equals(oldRomId, match.Rom.Id, StringComparison.OrdinalIgnoreCase))
                {
                    result.Outcome = RomMAuditOutcome.Unchanged;
                    return result;
                }

                if (options.DryRun)
                {
                    result.Outcome = RomMAuditOutcome.Updated;
                    return result;
                }

                _importService.ApplyMatchForReview(game, match.Rom, match.Strategy, match.Confidence);
                await _installStateService.UpsertIdentityAsync(
                        game.Id,
                        match.Rom.Id,
                        match.Rom.PlatformId,
                        match.Rom.Md5,
                        identity.LocalMd5,
                        identity.WindowsInstallType,
                        cancellationToken)
                    .ConfigureAwait(false);

                result.Outcome = RomMAuditOutcome.Updated;

                if (throttleDelay > 0)
                {
                    await Task.Delay(throttleDelay, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Outcome = RomMAuditOutcome.Failed;
                result.ErrorMessage = ex.Message;
                result.NewRommId = result.NewRommId ?? string.Empty;
                return result;
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                WriteGameResultLog(correlationId, platformName, result);
            }
        }

        private void WriteGameResultLog(string correlationId, string platformName, RomMAuditGameResult result)
        {
            if (result == null)
            {
                return;
            }

            var durationMs = (long)result.Duration.TotalMilliseconds;
            switch (result.Outcome)
            {
                case RomMAuditOutcome.Updated:
                    _logger?.Write(LogLevel.Info, "GameMatchFound", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("GameTitle", result.GameTitle ?? string.Empty),
                            ("Platform", platformName),
                            ("OldRomMId", result.OldRommId ?? string.Empty),
                            ("NewRomMId", result.NewRommId ?? string.Empty),
                            ("DurationMs", durationMs)));
                    break;
                case RomMAuditOutcome.Unchanged:
                    _logger?.Write(LogLevel.Info, "GameMatchUnchanged", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("GameTitle", result.GameTitle ?? string.Empty),
                            ("Platform", platformName),
                            ("RommId", result.OldRommId ?? string.Empty),
                            ("DurationMs", durationMs)));
                    break;
                case RomMAuditOutcome.NotFound:
                    _logger?.Write(LogLevel.Warning, "GameMatchNotFound", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("GameTitle", result.GameTitle ?? string.Empty),
                            ("Platform", platformName),
                            ("DurationMs", durationMs)));
                    break;
                case RomMAuditOutcome.Skipped:
                    _logger?.Write(LogLevel.Info, "GameAuditSkipped", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("GameTitle", result.GameTitle ?? string.Empty),
                            ("Platform", platformName),
                            ("DurationMs", durationMs)));
                    break;
                case RomMAuditOutcome.Failed:
                    _logger?.Write(LogLevel.Error, "GameAuditFailed", null,
                        BuildProps(
                            ("CorrelationId", correlationId),
                            ("GameTitle", result.GameTitle ?? string.Empty),
                            ("Platform", platformName),
                            ("Error", result.ErrorMessage ?? string.Empty),
                            ("DurationMs", durationMs)));
                    break;
            }
        }

        private static IReadOnlyDictionary<string, object> BuildProps(params (string Key, object Value)[] entries)
        {
            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return props;
            }

            foreach (var (key, value) in entries)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                props[key] = value ?? string.Empty;
            }

            return props;
        }

        private bool ShouldAuditGame(string rommId, RomMAuditOptions options)
        {
            if (options.ForceFullRematch)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(rommId))
            {
                return options.RematchMissingRommId;
            }

            return options.RevalidateExistingMatches;
        }

        private sealed class MatchCandidate
        {
            public RommRom Rom { get; init; }
            public string Strategy { get; init; }
            public string Confidence { get; init; }
        }

        private MatchCandidate FindBestMatch(
            ImportService.MatchIndex matchIndex,
            IPlatform platform,
            IReadOnlyDictionary<string, List<RommRom>> candidatesByNormalizedTitle,
            IGame game)
        {
            if (game == null || platform == null || matchIndex == null)
            {
                return null;
            }

            var normalized = NormalizeTitleForMatch(game.Title ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (!candidatesByNormalizedTitle.TryGetValue(normalized, out var candidates) || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var matches = new List<MatchCandidate>();
            foreach (var rom in candidates)
            {
                var match = _importService.EvaluateMatchForUi(matchIndex, platform, rom, matchByRomId: true, matchByMd5: true, matchByTitle: true, matchByFileName: true);
                if (match.Game == null || !string.Equals(match.Game.Id, game.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(new MatchCandidate
                {
                    Rom = rom,
                    Strategy = match.Strategy,
                    Confidence = match.Confidence
                });
            }

            if (matches.Count == 0)
            {
                return null;
            }

            return matches[0];
        }

        private async Task<IReadOnlyDictionary<string, List<RommRom>>> LoadPlatformRomCandidatesAsync(string platformId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                return new Dictionary<string, List<RommRom>>(StringComparer.OrdinalIgnoreCase);
            }

            var roms = await _importService.ListPlatformRomsAsync(platformId, cancellationToken).ConfigureAwait(false);
            var dictionary = new Dictionary<string, List<RommRom>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rom in roms ?? Array.Empty<RommRom>())
            {
                if (rom == null)
                {
                    continue;
                }

                var normalized = NormalizeTitleForMatch(rom.DisplayTitle ?? rom.Title ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (!dictionary.TryGetValue(normalized, out var list))
                {
                    list = new List<RommRom>();
                    dictionary[normalized] = list;
                }

                list.Add(rom);
            }

            return dictionary;
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
            value = value.Normalize(System.Text.NormalizationForm.FormD);
            var normalized = new string(value
                .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
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
    }
}
