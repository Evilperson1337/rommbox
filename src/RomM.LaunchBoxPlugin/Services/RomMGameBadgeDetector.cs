using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RomMbox.Services.Logging;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    internal sealed class RomMGameBadgeDetector
    {
        private const string RomMSource = "RomM";
        private const string RomMTooltip = "Available via RomM";
        private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex RommLaunchPathPattern = new Regex(@"(?:^|/+)rom(?:/|%2f).+?(?:/|%2f)ruffle(?:$|[/?#])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] AdditionalApplicationVersionSignals =
        {
            RommAdditionalApplicationService.RommVersion,
            "RomMbox Managed"
        };
        private static readonly string[] AdditionalApplicationStatusSignals =
        {
            RommAdditionalApplicationService.ManagedStatus,
            "Managed by RomMbox (Disc)",
            "Managed by RomMbox"
        };
        private static readonly string[] MarkerValues =
        {
            RomMSource,
            "RomMbox"
        };
        private readonly LoggingService _logger;
        private readonly ConcurrentDictionary<string, DetectionResult> _cache = new(StringComparer.OrdinalIgnoreCase);

        public RomMGameBadgeDetector(LoggingService logger)
        {
            _logger = logger;
        }

        public bool AppliesTo(IGame game)
        {
            return Detect(game).IsRomM;
        }

        public DetectionResult Detect(IGame game)
        {
            if (game == null)
            {
                return DetectionResult.NotDetected("Game was null.");
            }

            _logger?.Debug($"[RomM Badge] Evaluating GameId={game.Id ?? string.Empty} Title=\"{game.Title ?? string.Empty}\" Source=\"{game.Source ?? string.Empty}\".");

            var cacheKey = GetCacheKey(game);
            if (!string.IsNullOrWhiteSpace(cacheKey)
                && _cache.TryGetValue(cacheKey, out var cached))
            {
                _logger?.Debug($"[RomM Badge] Cache hit for GameId={game.Id ?? string.Empty} Title=\"{game.Title ?? string.Empty}\" IsRomM={cached.IsRomM} Reason=\"{cached.Reason}\".");
                return cached;
            }

            var result = DetectCore(game);
            if (!string.IsNullOrWhiteSpace(cacheKey))
            {
                _cache[cacheKey] = result;
            }

            return result;
        }

        private DetectionResult DetectCore(IGame game)
        {
            if (TryDetectBySource(game, out var sourceResult))
            {
                Log(game, sourceResult);
                return sourceResult;
            }

            if (TryDetectByAdditionalApplication(game, out var additionalAppResult))
            {
                Log(game, additionalAppResult);
                return additionalAppResult;
            }

            if (TryDetectByMetadataMarker(game, out var metadataResult))
            {
                Log(game, metadataResult);
                return metadataResult;
            }

            var notDetected = DetectionResult.NotDetected("No RomM association signals matched.");
            Log(game, notDetected);
            return notDetected;
        }

        private static string GetCacheKey(IGame game)
        {
            return string.IsNullOrWhiteSpace(game?.Id)
                ? string.Empty
                : game.Id.Trim();
        }

        private bool TryDetectBySource(IGame game, out DetectionResult result)
        {
            if (Comparer.Equals(game.Source ?? string.Empty, RomMSource))
            {
                result = DetectionResult.Detected("Source", $"[{nameof(IGame)}.{nameof(IGame.Source)}] matched '{RomMSource}'.");
                return true;
            }

            result = default;
            return false;
        }

        private bool TryDetectByAdditionalApplication(IGame game, out DetectionResult result)
        {
            var additionalApplications = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>();
            _logger?.Debug($"[RomM Badge] Additional application scan. GameId={game.Id ?? string.Empty} Title=\"{game.Title ?? string.Empty}\" Count={additionalApplications.Length}.");
            foreach (var additionalApplication in additionalApplications)
            {
                if (additionalApplication == null)
                {
                    continue;
                }

                _logger?.Debug($"[RomM Badge] Inspecting additional app. GameId={game.Id ?? string.Empty} AppId={additionalApplication.Id ?? string.Empty} Name=\"{additionalApplication.Name ?? string.Empty}\" Version=\"{additionalApplication.Version ?? string.Empty}\" Status=\"{additionalApplication.Status ?? string.Empty}\" Path=\"{additionalApplication.ApplicationPath ?? string.Empty}\".");

                if (RommAdditionalApplicationService.IsRommAdditionalApplication(additionalApplication, game.Id, null))
                {
                    result = DetectionResult.Detected("AdditionalApplication", $"Matched managed RomM additional application '{additionalApplication.Name ?? string.Empty}'.");
                    return true;
                }

                if (ContainsRomMToken(additionalApplication.Name))
                {
                    result = DetectionResult.Detected("AdditionalApplication", $"Name contained RomM token ('{additionalApplication.Name ?? string.Empty}').");
                    return true;
                }

                if (AdditionalApplicationVersionSignals.Any(signal => Comparer.Equals(additionalApplication.Version ?? string.Empty, signal)))
                {
                    result = DetectionResult.Detected("AdditionalApplication", $"Version matched RomM signal ('{additionalApplication.Version ?? string.Empty}').");
                    return true;
                }

                if (AdditionalApplicationStatusSignals.Any(signal => Comparer.Equals(additionalApplication.Status ?? string.Empty, signal)))
                {
                    result = DetectionResult.Detected("AdditionalApplication", $"Status matched RomM signal ('{additionalApplication.Status ?? string.Empty}').");
                    return true;
                }

                if (MatchesKnownRommEndpoint(additionalApplication.ApplicationPath)
                    || MatchesKnownRommEndpoint(additionalApplication.CommandLine))
                {
                    result = DetectionResult.Detected("AdditionalApplication", "Launch path matched a known RomM endpoint pattern.");
                    return true;
                }
            }

            result = default;
            return false;
        }

        private bool TryDetectByMetadataMarker(IGame game, out DetectionResult result)
        {
            if (TryMatchMetadataCollection(game, "GetAllCustomFields", out var customFieldResult)
                || TryMatchMetadataCollection(game, "CustomFields", out customFieldResult))
            {
                result = customFieldResult;
                return true;
            }

            if (TryMatchMetadataCollection(game, "GetAllTags", out var tagResult)
                || TryMatchMetadataCollection(game, "Tags", out tagResult))
            {
                result = tagResult;
                return true;
            }

            result = default;
            return false;
        }

        private bool TryMatchMetadataCollection(object target, string memberName, out DetectionResult result)
        {
            try
            {
                var values = GetMetadataValues(target, memberName);
                foreach (var candidate in values)
                {
                    if (!ContainsMarker(candidate))
                    {
                        continue;
                    }

                    result = DetectionResult.Detected("MetadataMarker", $"Metadata member '{memberName}' contained RomM marker ('{candidate}').");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Write(LogLevel.Debug, "RomMBadgeMetadataInspectionFailed", ex,
                    "Member", memberName,
                    "Tooltip", RomMTooltip,
                    "Subsystem", "Badges");
            }

            result = default;
            return false;
        }

        private static IEnumerable<string> GetMetadataValues(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                yield break;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = target.GetType().GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (method != null)
            {
                foreach (var value in FlattenValues(method.Invoke(target, null)))
                {
                    yield return value;
                }

                yield break;
            }

            var property = target.GetType().GetProperty(memberName, flags);
            if (property == null)
            {
                yield break;
            }

            foreach (var value in FlattenValues(property.GetValue(target)))
            {
                yield return value;
            }
        }

        private static IEnumerable<string> FlattenValues(object value)
        {
            if (value == null)
            {
                yield break;
            }

            if (value is string text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }

                yield break;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item is string entry)
                    {
                        if (!string.IsNullOrWhiteSpace(entry))
                        {
                            yield return entry;
                        }

                        continue;
                    }

                    var itemType = item.GetType();
                    var name = itemType.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)?.ToString();
                    var fieldValue = itemType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)?.ToString();
                    var combined = string.Join(" ", new[] { name, fieldValue }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        yield return combined;
                        continue;
                    }

                    var itemText = item.ToString();
                    if (!string.IsNullOrWhiteSpace(itemText))
                    {
                        yield return itemText;
                    }
                }

                yield break;
            }

            var scalar = value.ToString();
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                yield return scalar;
            }
        }

        private static bool ContainsMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return MarkerValues.Any(marker => value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsRomMToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(RomMSource, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesKnownRommEndpoint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Replace('\\', '/');
            return RommLaunchPathPattern.IsMatch(normalized)
                || normalized.IndexOf("/rom/", StringComparison.OrdinalIgnoreCase) >= 0
                   && normalized.IndexOf("/ruffle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Log(IGame game, DetectionResult result)
        {
            var message = result.IsRomM
                ? $"[RomM Badge] GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\" → RomM detected via {result.Reason}"
                : $"[RomM Badge] GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\" → Not a RomM game";

            _logger?.Debug(message);
        }

        internal readonly struct DetectionResult
        {
            public DetectionResult(bool isRomM, string reason, string detail)
            {
                IsRomM = isRomM;
                Reason = reason ?? string.Empty;
                Detail = detail ?? string.Empty;
            }

            public bool IsRomM { get; }

            public string Reason { get; }

            public string Detail { get; }

            public static DetectionResult Detected(string reason, string detail)
            {
                return new DetectionResult(true, reason, detail);
            }

            public static DetectionResult NotDetected(string detail)
            {
                return new DetectionResult(false, string.Empty, detail);
            }
        }
    }
}
