using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Models.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins;

namespace RomMbox.Services
{
    /// <summary>
    /// Discovers RomM platforms and maps them to LaunchBox platform names using
    /// saved mappings, defaults, aliases, and fuzzy matching.
    /// </summary>
    internal class PlatformMappingService
    {
        private static readonly TimeSpan PlatformsCacheTtl = TimeSpan.FromSeconds(30);
        private static readonly object PlatformsCacheLock = new object();
        private static readonly Dictionary<string, PlatformsCacheEntry> PlatformsCache =
            new Dictionary<string, PlatformsCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class PlatformsCacheEntry
        {
            public IReadOnlyList<RommPlatform> Platforms { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public Task<IReadOnlyList<RommPlatform>> InFlight { get; set; }
        }
        private static readonly Dictionary<string, string> PredefinedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "nes", "Nintendo Entertainment System" },
            { "famicom", "Nintendo Entertainment System" },
            { "snes", "Super Nintendo Entertainment System" },
            { "super nintendo", "Super Nintendo Entertainment System" },
            { "n64", "Nintendo 64" },
            { "nintendo 64", "Nintendo 64" },
            { "game boy", "Nintendo Game Boy" },
            { "gb", "Nintendo Game Boy" },
            { "gbc", "Nintendo Game Boy Color" },
            { "game boy color", "Nintendo Game Boy Color" },
            { "gba", "Nintendo Game Boy Advance" },
            { "game boy advance", "Nintendo Game Boy Advance" },
            { "nds", "Nintendo DS" },
            { "nintendo ds", "Nintendo DS" },
            { "3ds", "Nintendo 3DS" },
            { "nintendo 3ds", "Nintendo 3DS" },
            { "genesis", "Sega Genesis" },
            { "mega drive", "Sega Genesis" },
            { "sms", "Sega Master System" },
            { "master system", "Sega Master System" },
            { "saturn", "Sega Saturn" },
            { "dreamcast", "Sega Dreamcast" },
            { "ps1", "Sony PlayStation" },
            { "playstation", "Sony PlayStation" },
            { "ps2", "Sony PlayStation 2" },
            { "playstation 2", "Sony PlayStation 2" },
            { "psp", "Sony PSP" },
            { "ps vita", "Sony PS Vita" },
            { "vita", "Sony PS Vita" },
            { "ps3", "Sony PlayStation 3" },
            { "playstation 3", "Sony PlayStation 3" },
            { "ps4", "Sony PlayStation 4" },
            { "playstation 4", "Sony PlayStation 4" }
        };

        private static readonly Lazy<List<DefaultMappingEntry>> DefaultMappings = new Lazy<List<DefaultMappingEntry>>(LoadDefaultMappings);

        private readonly LoggingService _logger;
        private readonly PlatformMappingStore _mappingStore;
        private readonly IRommClient _rommClient;

        /// <summary>
        /// Creates the mapping service used to resolve RomM platform IDs to LaunchBox names.
        /// </summary>
        public PlatformMappingService(LoggingService logger, SettingsManager settingsManager, IRommClient rommClient)
        {
            _logger = logger;
            _mappingStore = new PlatformMappingStore(logger);
            _rommClient = rommClient;
        }

        /// <summary>
        /// Fetches RomM platforms and builds mapping results for the UI.
        /// </summary>
        public virtual async Task<PlatformMappingResult> DiscoverPlatformsAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger?.Info("Checking RomM platforms for mapping.");
                var platforms = await GetPlatformsCachedAsync(cancellationToken).ConfigureAwait(false);
                var mappings = new List<PlatformMapping>();
                foreach (var platform in platforms)
                {
                    var mappingResult = ResolveLaunchBoxPlatform(platform.Id, platform.Name);
                    var saved = _mappingStore.GetPlatformMappingAsync(platform.Id ?? string.Empty, cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    mappings.Add(new PlatformMapping
                    {
                        RommPlatformId = platform.Id ?? string.Empty,
                        RommPlatformName = platform.Name ?? string.Empty,
                        LaunchBoxPlatformName = mappingResult.Name ?? string.Empty,
                        AutoMapped = mappingResult.AutoMapped,
                        DisableAutoImport = saved?.DisableAutoImport ?? false,
                        ExtractAfterDownload = saved?.ExtractAfterDownload ?? false,
                        ExtractionBehavior = saved?.ExtractionBehavior ?? ExtractionBehavior.Subfolder,
                        InstallerMode = saved?.InstallerMode ?? InstallerMode.Manual,
                        MusicRootPath = saved?.MusicRootPath ?? string.Empty,
                        InstallOst = saved?.InstallOst ?? false,
                        BonusRootPath = saved?.BonusRootPath ?? string.Empty,
                        InstallBonus = saved?.InstallBonus ?? false,
                        PreReqsRootPath = saved?.PreReqsRootPath ?? string.Empty,
                        InstallPreReqs = saved?.InstallPreReqs ?? false
                    });
                }
                return new PlatformMappingResult { Mappings = mappings };
            }
            catch (Exception ex)
            {
                _logger?.Error("Failed to discover platforms.", ex);
                throw;
            }
        }

        /// <summary>
        /// Returns a cached list of RomM platforms, refreshing when stale.
        /// </summary>
        private Task<IReadOnlyList<RommPlatform>> GetPlatformsCachedAsync(CancellationToken cancellationToken)
        {
            var cacheKey = _rommClient?.GetType().FullName ?? "romm";
            PlatformsCacheEntry entry;

            lock (PlatformsCacheLock)
            {
                if (PlatformsCache.TryGetValue(cacheKey, out entry))
                {
                    if (entry.Platforms != null && DateTimeOffset.UtcNow - entry.UpdatedAt < PlatformsCacheTtl)
                    {
                        return Task.FromResult(entry.Platforms);
                    }

                    if (entry.InFlight != null && !entry.InFlight.IsCompleted)
                    {
                        return entry.InFlight;
                    }
                }

                entry = entry ?? new PlatformsCacheEntry();
                entry.InFlight = FetchPlatformsAsync(cacheKey, entry, cancellationToken);
                PlatformsCache[cacheKey] = entry;
                return entry.InFlight;
            }
        }

        /// <summary>
        /// Performs the network call to retrieve platforms and updates cache state.
        /// </summary>
        private async Task<IReadOnlyList<RommPlatform>> FetchPlatformsAsync(string cacheKey, PlatformsCacheEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                var platforms = await _rommClient.ListPlatformsAsync(cancellationToken).ConfigureAwait(false);
                lock (PlatformsCacheLock)
                {
                    entry.Platforms = platforms ?? Array.Empty<RommPlatform>();
                    entry.UpdatedAt = DateTimeOffset.UtcNow;
                    entry.InFlight = null;
                    PlatformsCache[cacheKey] = entry;
                }

                return entry.Platforms;
            }
            catch
            {
                lock (PlatformsCacheLock)
                {
                    entry.InFlight = null;
                    PlatformsCache[cacheKey] = entry;
                }

                throw;
            }
        }

        /// <summary>
        /// Resolves a LaunchBox platform name for a given RomM platform id/name.
        /// </summary>
        public string GetLaunchBoxPlatformName(string rommPlatformId, string rommPlatformName)
        {
            return ResolveLaunchBoxPlatform(rommPlatformId, rommPlatformName).Name ?? string.Empty;
        }

        public PlatformMapping GetMapping(string rommPlatformId)
        {
            return _mappingStore.GetPlatformMappingAsync(rommPlatformId ?? string.Empty, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Resolves the LaunchBox platform name and whether it was auto-mapped.
        /// </summary>
        private (string Name, bool AutoMapped) ResolveLaunchBoxPlatform(string rommPlatformId, string rommPlatformName)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformId) && string.IsNullOrWhiteSpace(rommPlatformName))
            {
                return (string.Empty, false);
            }

            var saved = _mappingStore.GetPlatformMappingAsync(rommPlatformId ?? string.Empty, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (saved != null && !string.IsNullOrWhiteSpace(saved.LaunchBoxPlatformName))
            {
                return (saved.LaunchBoxPlatformName, false);
            }

            var autoMapped = AutoMapPlatform(rommPlatformId, rommPlatformName);
            if (!string.IsNullOrWhiteSpace(autoMapped))
            {
                return (autoMapped, true);
            }

            return (string.Empty, false);
        }

        /// <summary>
        /// Persists a single platform mapping to settings.
        /// </summary>
        public void SaveMapping(PlatformMapping mapping)
        {
            _mappingStore.SavePlatformMappingAsync(mapping, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _logger?.Debug($"Saved platform mapping: {mapping?.RommPlatformId} -> {mapping?.LaunchBoxPlatformName}");
        }

        /// <summary>
        /// Persists multiple platform mappings to settings.
        /// </summary>
        public void SaveMappings(PlatformMapping[] mappings)
        {
            _mappingStore.SavePlatformMappingsAsync(mappings, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _logger?.Debug("Saved platform mappings.");
        }

        /// <summary>
        /// Updates LaunchBox platform XML to align with RomM DisableAutoImport flags.
        /// </summary>
        private void ApplyAutoImportSettings(PlatformMapping[] mappings)
        {
            if (mappings == null || mappings.Length == 0)
            {
                return;
            }

            foreach (var mapping in mappings)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatformName))
                {
                    continue;
                }

                try
                {
                    var platformPath = ResolvePlatformXmlPath(mapping.LaunchBoxPlatformName);
                    if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
                    {
                        _logger?.Warning($"Platform XML not found for '{mapping.LaunchBoxPlatformName}'.");
                        continue;
                    }

                    var xml = File.ReadAllText(platformPath);
                    var updated = UpsertPlatformDisableAutoImport(xml, mapping.DisableAutoImport);
                    if (!string.Equals(xml, updated, StringComparison.Ordinal))
                    {
                        WritePlatformXmlSafely(platformPath, updated);
                        _logger?.Info($"Updated DisableAutoImport for '{mapping.LaunchBoxPlatformName}' to {mapping.DisableAutoImport}.");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to update DisableAutoImport for '{mapping.LaunchBoxPlatformName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Resolves the LaunchBox platform XML path for a given platform name.
        /// </summary>
        private static string ResolvePlatformXmlPath(string launchBoxPlatformName)
        {
            if (string.IsNullOrWhiteSpace(launchBoxPlatformName))
            {
                return string.Empty;
            }

            var root = Paths.PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            var fileName = SanitizePlatformFileName(launchBoxPlatformName);
            return Path.Combine(root, "Data", "Platforms", fileName + ".xml");
        }

        /// <summary>
        /// Produces a filesystem-safe platform file name.
        /// </summary>
        private static string SanitizePlatformFileName(string platformName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(platformName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return sanitized.Trim();
        }

        /// <summary>
        /// Inserts or updates the DisableAutoImport element for each game entry.
        /// </summary>
        private static string UpsertPlatformDisableAutoImport(string xml, bool disableAutoImport)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return xml;
            }

            var value = disableAutoImport ? "true" : "false";

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                var gameElements = doc.Root?.Elements("Game").ToList();
                if (gameElements == null || gameElements.Count == 0)
                {
                    return xml;
                }

                foreach (var game in gameElements)
                {
                    var disableElement = game.Element("DisableAutoImport");
                    if (disableElement == null)
                    {
                        disableElement = new XElement("DisableAutoImport", value);
                        game.Add(disableElement);
                    }
                    else
                    {
                        disableElement.Value = value;
                    }
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

        /// <summary>
        /// Writes updated platform XML with a temp file + backup for safety.
        /// </summary>
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

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
        }

        /// <summary>
        /// Returns RomM platform IDs excluded from auto import.
        /// </summary>
        public string[] GetExcludedRommPlatformIds()
        {
            return _mappingStore.GetExcludedRommPlatformIdsAsync(CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Saves the list of RomM platform IDs excluded from auto import.
        /// </summary>
        public void SaveExcludedRommPlatformIds(string[] platformIds)
        {
            _mappingStore.SaveExcludedRommPlatformIdsAsync(platformIds, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _logger?.Debug("Saved excluded RomM platforms.");
        }

        /// <summary>
        /// Adds a custom alias to improve auto-mapping.
        /// </summary>
        public void AddAlias(PlatformAlias alias)
        {
            _mappingStore.SavePlatformAliasAsync(alias, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _logger?.Debug($"Added alias: {alias?.Alias} -> {alias?.LaunchBoxPlatformName}");
        }

        /// <summary>
        /// Removes a previously saved platform alias.
        /// </summary>
        public void RemoveAlias(string aliasId)
        {
            _mappingStore.DeletePlatformAliasAsync(aliasId, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _logger?.Debug($"Removed alias: {aliasId}");
        }

        /// <summary>
        /// Attempts to auto-map RomM platform names to LaunchBox platforms.
        /// Uses defaults, predefined mappings, aliases, then fuzzy matching.
        /// </summary>
        private string AutoMapPlatform(string rommPlatformId, string rommPlatformName)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformName))
            {
                return null;
            }

            var normalized = NormalizePlatformName(rommPlatformName);
            var defaultMatch = TryResolveDefaultMapping(rommPlatformId, rommPlatformName);
            if (!string.IsNullOrWhiteSpace(defaultMatch))
            {
                return defaultMatch;
            }

            if (PredefinedMappings.TryGetValue(normalized, out var predefined))
            {
                return predefined;
            }

            var aliases = _mappingStore.GetPlatformAliasesAsync(CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            foreach (var alias in aliases)
            {
                var aliasNormalized = NormalizePlatformName(alias.Alias);
                if (string.IsNullOrWhiteSpace(aliasNormalized))
                {
                    continue;
                }

                if (normalized.Contains(aliasNormalized) || aliasNormalized.Contains(normalized))
                {
                    return alias.LaunchBoxPlatformName;
                }
            }

            var launchBoxPlatforms = GetLaunchBoxPlatformNames();
            var bestMatch = string.Empty;
            var bestScore = 0.0;
            var rommYamlName = GetRommYamlPlatformName(rommPlatformId, rommPlatformName);
            var rommNormalized = NormalizePlatformName(rommYamlName ?? rommPlatformName);

            foreach (var platformName in launchBoxPlatforms)
            {
                var score = CalculateSimilarity(rommNormalized, NormalizePlatformName(platformName));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = platformName;
                }
            }

            if (bestScore >= 0.8)
            {
                return bestMatch;
            }

            return null;
        }

        /// <summary>
        /// Returns all saved platform aliases.
        /// </summary>
        public PlatformAlias[] GetAliases()
        {
            return _mappingStore.GetPlatformAliasesAsync(CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Normalizes platform names to a lowercase, alphanumeric form for comparisons.
        /// </summary>
        private static string NormalizePlatformName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = name.ToLowerInvariant();
            normalized = Regex.Replace(normalized, "[^a-z0-9]+", " ");
            normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
            return normalized;
        }

        /// <summary>
        /// Uses default mapping YAML to find a canonical RomM platform name.
        /// </summary>
        private static string GetRommYamlPlatformName(string rommPlatformId, string fallback)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformId))
            {
                return fallback ?? string.Empty;
            }

            foreach (var entry in DefaultMappings.Value)
            {
                if (entry.MatchesId(rommPlatformId))
                {
                    return entry.RommPlatformName ?? fallback ?? string.Empty;
                }
            }

            return fallback ?? string.Empty;
        }

        /// <summary>
        /// Looks for a mapping from the default mapping YAML file.
        /// </summary>
        private static string TryResolveDefaultMapping(string rommPlatformId, string rommPlatformName)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformId) && string.IsNullOrWhiteSpace(rommPlatformName))
            {
                return string.Empty;
            }

            var normalizedName = NormalizePlatformName(rommPlatformName);

            foreach (var entry in DefaultMappings.Value)
            {
                if (entry.Matches(normalizedName))
                {
                    return entry.LaunchBoxPlatformName;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Loads default mapping entries from the embedded YAML file on disk.
        /// </summary>
        private static List<DefaultMappingEntry> LoadDefaultMappings()
        {
            var results = new List<DefaultMappingEntry>();
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(location))
                {
                    return results;
                }

                var pluginDir = Path.GetDirectoryName(location);
                if (string.IsNullOrWhiteSpace(pluginDir))
                {
                    return results;
                }

                var yamlPath = Path.Combine(pluginDir, "system", "default-mapping.yaml");
                if (!File.Exists(yamlPath))
                {
                    return results;
                }

                string currentLaunchBox = null;
                string currentRomm = null;
                string currentRommId = null;
                var currentAliases = new List<string>();

                void CommitEntry()
                {
                    if (string.IsNullOrWhiteSpace(currentLaunchBox))
                    {
                        return;
                    }

                    results.Add(new DefaultMappingEntry(currentLaunchBox, currentRomm ?? string.Empty, currentRommId ?? string.Empty, currentAliases));
                    currentLaunchBox = null;
                    currentRomm = null;
                    currentRommId = null;
                    currentAliases = new List<string>();
                }

                foreach (var rawLine in File.ReadAllLines(yamlPath))
                {
                    var line = rawLine?.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!rawLine.StartsWith(" ") && line.EndsWith(":", StringComparison.Ordinal))
                    {
                        CommitEntry();
                        currentLaunchBox = line.TrimEnd(':').Trim();
                        continue;
                    }

                    if (line.StartsWith("RomM Platform:", StringComparison.OrdinalIgnoreCase))
                    {
                        currentRomm = line.Substring("RomM Platform:".Length).Trim().Trim('"');
                        if (string.Equals(currentRomm, "''", StringComparison.OrdinalIgnoreCase))
                        {
                            currentRomm = string.Empty;
                        }
                        continue;
                    }

                    if (line.StartsWith("romm_platform_id:", StringComparison.OrdinalIgnoreCase))
                    {
                        currentRommId = line.Substring("romm_platform_id:".Length).Trim().Trim('"');
                        if (string.Equals(currentRommId, "''", StringComparison.OrdinalIgnoreCase))
                        {
                            currentRommId = string.Empty;
                        }
                        continue;
                    }

                    if (line.StartsWith("-", StringComparison.Ordinal))
                    {
                        var alias = line.Substring(1).Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(alias))
                        {
                            currentAliases.Add(alias);
                        }
                        continue;
                    }
                }

                CommitEntry();
            }
            catch
            {
                return results;
            }

            return results;
        }

        /// <summary>
        /// Represents a single default mapping entry from the YAML file.
        /// </summary>
        private sealed class DefaultMappingEntry
        {
            public DefaultMappingEntry(string launchBoxPlatformName, string rommPlatformName, string rommPlatformId, IEnumerable<string> aliases)
            {
                LaunchBoxPlatformName = launchBoxPlatformName ?? string.Empty;
                RommPlatformName = rommPlatformName ?? string.Empty;
                RommPlatformId = rommPlatformId ?? string.Empty;
                Aliases = aliases?.Where(alias => !string.IsNullOrWhiteSpace(alias)).ToArray() ?? Array.Empty<string>();
                NormalizedRomm = NormalizePlatformName(RommPlatformName);
                NormalizedAliases = Aliases.Select(NormalizePlatformName).Where(alias => !string.IsNullOrWhiteSpace(alias)).ToArray();
            }

            public string LaunchBoxPlatformName { get; }
            public string RommPlatformName { get; }
            public string RommPlatformId { get; }
            public string[] Aliases { get; }
            private string NormalizedRomm { get; }
            private string[] NormalizedAliases { get; }

            public bool Matches(string normalizedRommName)
            {
                if (!string.IsNullOrWhiteSpace(NormalizedRomm)
                    && MatchesValue(NormalizedRomm, normalizedRommName))
                {
                    return true;
                }

                if (NormalizedAliases.Length == 0)
                {
                    return false;
                }

                foreach (var alias in NormalizedAliases)
                {
                    if (MatchesValue(alias, normalizedRommName))
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool MatchesId(string rommPlatformId)
            {
                if (string.IsNullOrWhiteSpace(rommPlatformId))
                {
                    return false;
                }

                return string.Equals(RommPlatformId, rommPlatformId, StringComparison.OrdinalIgnoreCase);
            }

            private static bool MatchesValue(string expected, string actual)
            {
                if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
                {
                    return false;
                }

                if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Computes a similarity score between two strings based on edit distance.
        /// </summary>
        private static double CalculateSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var distance = ComputeLevenshteinDistance(left, right);
            var maxLength = Math.Max(left.Length, right.Length);
            return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
        }

        /// <summary>
        /// Computes Levenshtein edit distance between two strings.
        /// </summary>
        private static int ComputeLevenshteinDistance(string left, string right)
        {
            var n = left.Length;
            var m = right.Length;
            var costs = new int[m + 1];
            for (var j = 0; j <= m; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= n; i++)
            {
                costs[0] = i;
                var previous = i - 1;
                for (var j = 1; j <= m; j++)
                {
                    var current = costs[j];
                    var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    costs[j] = Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1), previous + cost);
                    previous = current;
                }
            }

            return costs[m];
        }

        /// <summary>
        /// Returns platform names already known to LaunchBox for fuzzy matching.
        /// </summary>
        private static string[] GetLaunchBoxPlatformNames()
        {
            try
            {
                var dataManager = PluginHelper.DataManager;
                if (dataManager == null)
                {
                    return Array.Empty<string>();
                }

                var platforms = dataManager.GetAllPlatforms();
                if (platforms == null)
                {
                    return Array.Empty<string>();
                }

                return platforms
                    .Where(platform => platform != null && !string.IsNullOrWhiteSpace(platform.Name))
                    .Select(platform => platform.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
