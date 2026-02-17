using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;

namespace RomMbox.Services
{
    /// <summary>
    /// Persists user decisions to ignore specific RomM/LaunchBox match suggestions.
    /// Stored as JSON in the plugin data directory.
    /// </summary>
    internal sealed class MatchIgnoreStore
    {
        private const string FileName = "ignored-matches.json";
        private readonly LoggingService _logger;
        private readonly string _path;

        /// <summary>
        /// Creates the store with the default data file path.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        public MatchIgnoreStore(LoggingService logger)
        {
            _logger = logger;
            _path = Path.Combine(PluginPaths.GetPluginDataDirectory(), FileName);
        }

        /// <summary>
        /// Loads ignored match entries from disk.
        /// </summary>
        public IReadOnlyList<MatchIgnoreEntry> Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<MatchIgnoreEntry>();
                }

                var json = File.ReadAllText(_path);
                var entries = JsonSerializer.Deserialize<List<MatchIgnoreEntry>>(json);
                return entries ?? new List<MatchIgnoreEntry>();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to load ignored matches: {ex.Message}");
                return Array.Empty<MatchIgnoreEntry>();
            }
        }

        /// <summary>
        /// Saves the provided entries to disk, overwriting the previous file.
        /// </summary>
        public void Save(IEnumerable<MatchIgnoreEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(PluginPaths.GetPluginDataDirectory());
                var json = JsonSerializer.Serialize(entries ?? Array.Empty<MatchIgnoreEntry>(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to save ignored matches: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the specified match tuple has been ignored by the user.
        /// </summary>
        public bool IsIgnored(string platformId, string rommId, string launchBoxGameId)
        {
            if (string.IsNullOrWhiteSpace(platformId) || string.IsNullOrWhiteSpace(rommId) || string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return false;
            }

            return Load().Any(entry =>
                string.Equals(entry.PlatformId, platformId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.RommId, rommId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.LaunchBoxGameId, launchBoxGameId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Adds a new ignored match if it does not already exist.
        /// </summary>
        public IReadOnlyList<MatchIgnoreEntry> AddIgnore(string platformId, string rommId, string launchBoxGameId)
        {
            var entries = Load().ToList();
            if (string.IsNullOrWhiteSpace(platformId) || string.IsNullOrWhiteSpace(rommId) || string.IsNullOrWhiteSpace(launchBoxGameId))
            {
                return entries;
            }

            if (!entries.Any(entry =>
                    string.Equals(entry.PlatformId, platformId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.RommId, rommId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.LaunchBoxGameId, launchBoxGameId, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new MatchIgnoreEntry
                {
                    PlatformId = platformId,
                    RommId = rommId,
                    LaunchBoxGameId = launchBoxGameId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                Save(entries);
            }

            return entries;
        }
    }

    internal sealed class MatchIgnoreEntry
    {
        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        public string PlatformId { get; set; }
        /// <summary>
        /// RomM ROM identifier.
        /// </summary>
        public string RommId { get; set; }
        /// <summary>
        /// LaunchBox game identifier.
        /// </summary>
        public string LaunchBoxGameId { get; set; }
        /// <summary>
        /// When the ignore decision was created (UTC).
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}
