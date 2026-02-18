using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Linq;
using RomMbox.Storage;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using RomMbox.Utilities;
using RomMbox.Models.PlatformMapping;

namespace RomMbox.Services.Settings
{
    /// <summary>
    /// Loads, caches, and persists plugin settings and credentials.
    /// </summary>
    internal sealed class SettingsManager
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);
        private static readonly object CredentialCacheSync = new object();
        private static string _cachedCredentialsServerUrl;
        private static CredentialResult _cachedCredentials;
        private readonly LoggingService _logger;
        private readonly object _sync = new object();
        private readonly CredentialStore _credentialStore;
        private PluginSettings _cachedSettings;
        private DateTimeOffset _cachedAt;

        /// <summary>
        /// Creates a settings manager with the provided logger.
        /// </summary>
        /// <param name="logger">The logging service to use.</param>
        public SettingsManager(LoggingService logger)
        {
            Guard.NotNull(logger, nameof(logger));
            _logger = logger;
            _credentialStore = new CredentialStore(logger);
            _cachedAt = DateTimeOffset.MinValue;
        }

        /// <summary>
        /// Loads settings from disk with a short-lived cache.
        /// </summary>
        /// <returns>The loaded settings.</returns>
        public PluginSettings Load()
        {
            lock (_sync)
            {
                if (_cachedSettings != null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
                {
                    return _cachedSettings;
                }

                var settings = LoadInternal();
                _cachedSettings = settings;
                _cachedAt = DateTimeOffset.UtcNow;
                return settings;
            }
        }

        /// <summary>
        /// Saves settings to disk and updates the cache.
        /// </summary>
        /// <param name="settings">The settings to save.</param>
        public void Save(PluginSettings settings)
        {
            Guard.NotNull(settings, nameof(settings));
            lock (_sync)
            {
                try
                {
                    settings.ApplyDefaults();
                    SaveInternal(settings, PluginPaths.GetSettingsPath(), true);
                    CacheSettings(settings);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to save settings.", ex);
                }
            }
        }

        /// <summary>
        /// Saves credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL.</param>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        public void SaveCredentials(string serverUrl, string username, string password)
        {
            Guard.NotNull(serverUrl, nameof(serverUrl));
            _logger?.Debug($"SettingsManager.SaveCredentials called - ServerUrl: {LoggingService.SanitizeUrl(serverUrl)}, Username: <redacted>, Password length: {password?.Length ?? 0}");
            
            lock (CredentialCacheSync)
            {
                try
                {
                    _credentialStore.SaveCredentials(serverUrl, username, password);
                    _cachedCredentialsServerUrl = serverUrl;
                    _cachedCredentials = new CredentialResult(username, password);
                    _logger?.Debug("Credentials saved successfully to credential store");
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to save credentials.", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Retrieves saved credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL.</param>
        /// <returns>The credential result, or null if not found.</returns>
        public CredentialResult GetSavedCredentials(string serverUrl)
        {
            Guard.NotNull(serverUrl, nameof(serverUrl));

            lock (CredentialCacheSync)
            {
                if (!string.IsNullOrWhiteSpace(_cachedCredentialsServerUrl)
                    && _cachedCredentials != null
                    && string.Equals(_cachedCredentialsServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return _cachedCredentials;
                }

                try
                {
                    _logger?.Debug($"SettingsManager.GetSavedCredentials called - ServerUrl: {LoggingService.SanitizeUrl(serverUrl)}");
                    var result = _credentialStore.GetCredentials(serverUrl);
                    if (result != null)
                    {
                        _logger?.Debug($"Found credentials - Username: <redacted>, Password length: {result.Password?.Length ?? 0}");
                        _cachedCredentialsServerUrl = serverUrl;
                        _cachedCredentials = result;
                    }
                    else
                    {
                        _logger?.Debug("No credentials found for this server URL");
                        _cachedCredentialsServerUrl = null;
                        _cachedCredentials = null;
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to read credentials.", ex);
                    _cachedCredentialsServerUrl = null;
                    _cachedCredentials = null;
                    return null;
                }
            }
        }

        /// <summary>
        /// Attempts to retrieve any saved credentials when the server URL is unknown.
        /// </summary>
        public bool TryGetAnySavedCredentials(out string serverUrl, out CredentialResult credentials)
        {
            serverUrl = null;
            credentials = null;

            lock (CredentialCacheSync)
            {
                try
                {
                    var result = _credentialStore.TryGetAnyCredentials(out serverUrl, out credentials);
                    if (result && !string.IsNullOrWhiteSpace(serverUrl) && credentials != null)
                    {
                        _cachedCredentialsServerUrl = serverUrl;
                        _cachedCredentials = credentials;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to enumerate saved credentials.", ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Deletes saved credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL.</param>
        public void DeleteSavedCredentials(string serverUrl)
        {
            Guard.NotNull(serverUrl, nameof(serverUrl));
            lock (CredentialCacheSync)
            {
                try
                {
                    _credentialStore.DeleteCredentials(serverUrl);
                    if (!string.IsNullOrWhiteSpace(_cachedCredentialsServerUrl)
                        && string.Equals(_cachedCredentialsServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedCredentialsServerUrl = null;
                        _cachedCredentials = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to delete credentials.", ex);
                }
            }
        }

        /// <summary>
        /// Gets a single platform mapping by RomM platform ID.
        /// </summary>
        /// <param name="rommPlatformId">The RomM platform ID.</param>
        /// <returns>The platform mapping or null if not found.</returns>
        public PlatformMapping GetPlatformMapping(string rommPlatformId)
        {
            Guard.NotNull(rommPlatformId, nameof(rommPlatformId));
            lock (_sync)
            {
                var settings = LoadInternal();
                return settings.PlatformMappings.FirstOrDefault(mapping => string.Equals(mapping.RommPlatformId, rommPlatformId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Returns all platform mappings.
        /// </summary>
        public PlatformMapping[] GetPlatformMappings()
        {
            lock (_sync)
            {
                var settings = LoadInternal();
                return settings.PlatformMappings.ToArray();
            }
        }

        /// <summary>
        /// Returns all platform aliases.
        /// </summary>
        public PlatformAlias[] GetPlatformAliases()
        {
            lock (_sync)
            {
                var settings = LoadInternal();
                return settings.PlatformAliases.ToArray();
            }
        }

        /// <summary>
        /// Returns all excluded RomM platform IDs.
        /// </summary>
        public string[] GetExcludedRommPlatformIds()
        {
            lock (_sync)
            {
                var settings = LoadInternal();
                return settings.ExcludedRommPlatformIds.ToArray();
            }
        }

        /// <summary>
        /// Saves the excluded RomM platform IDs.
        /// </summary>
        /// <param name="platformIds">The platform IDs to exclude.</param>
        public void SaveExcludedRommPlatformIds(string[] platformIds)
        {
            Guard.NotNull(platformIds, nameof(platformIds));
            lock (_sync)
            {
                var settings = LoadInternal();
                settings.ExcludedRommPlatformIds = platformIds.ToArray();
                settings.ApplyDefaults();
                SaveInternal(settings, PluginPaths.GetSettingsPath(), false);
                CacheSettings(settings);
            }
        }

        /// <summary>
        /// Saves a single platform mapping.
        /// </summary>
        /// <param name="mapping">The mapping to save.</param>
        public void SavePlatformMapping(PlatformMapping mapping)
        {
            Guard.NotNull(mapping, nameof(mapping));
            lock (_sync)
            {
                var settings = LoadInternal();
                var mappings = settings.PlatformMappings.ToList();
                var existingIndex = mappings.FindIndex(item => string.Equals(item.RommPlatformId, mapping.RommPlatformId, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    mappings[existingIndex] = mapping;
                }
                else
                {
                    mappings.Add(mapping);
                }

                settings.PlatformMappings = mappings.ToArray();
                settings.ApplyDefaults();
                SaveInternal(settings, PluginPaths.GetSettingsPath(), false);
                CacheSettings(settings);
            }
        }

        /// <summary>
        /// Saves all platform mappings.
        /// </summary>
        /// <param name="mappings">The mappings to save.</param>
        public void SavePlatformMappings(PlatformMapping[] mappings)
        {
            Guard.NotNull(mappings, nameof(mappings));
            lock (_sync)
            {
                var settings = LoadInternal();
                settings.PlatformMappings = mappings.ToArray();
                settings.ApplyDefaults();
                SaveInternal(settings, PluginPaths.GetSettingsPath(), false);
                CacheSettings(settings);
            }
        }

        /// <summary>
        /// Saves a platform alias entry.
        /// </summary>
        /// <param name="alias">The alias to save.</param>
        public void SavePlatformAlias(PlatformAlias alias)
        {
            Guard.NotNull(alias, nameof(alias));
            lock (_sync)
            {
                var settings = LoadInternal();
                var aliases = settings.PlatformAliases.ToList();
                var existingIndex = aliases.FindIndex(item => string.Equals(item.Id, alias.Id, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    aliases[existingIndex] = alias;
                }
                else
                {
                    aliases.Add(alias);
                }

                settings.PlatformAliases = aliases.ToArray();
                settings.ApplyDefaults();
                SaveInternal(settings, PluginPaths.GetSettingsPath(), false);
                CacheSettings(settings);
            }
        }

        /// <summary>
        /// Deletes a platform alias by its ID.
        /// </summary>
        /// <param name="aliasId">The alias identifier.</param>
        public void DeletePlatformAlias(string aliasId)
        {
            Guard.NotNull(aliasId, nameof(aliasId));
            lock (_sync)
            {
                var settings = LoadInternal();
                var aliases = settings.PlatformAliases.ToList();
                aliases.RemoveAll(item => string.Equals(item.Id, aliasId, StringComparison.OrdinalIgnoreCase));
                settings.PlatformAliases = aliases.ToArray();
                settings.ApplyDefaults();
                SaveInternal(settings, PluginPaths.GetSettingsPath(), false);
                CacheSettings(settings);
            }
        }

        /// <summary>
        /// Updates the cached settings and cache timestamp.
        /// </summary>
        /// <param name="settings">The settings to cache.</param>
        private void CacheSettings(PluginSettings settings)
        {
            _cachedSettings = settings;
            _cachedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Creates a new settings instance with defaults applied.
        /// </summary>
        /// <returns>The default settings.</returns>
        private static PluginSettings CreateDefaults()
        {
            var settings = new PluginSettings();
            settings.ApplyDefaults();
            return settings;
        }

        /// <summary>
        /// Loads settings from disk without cache checks.
        /// </summary>
        /// <returns>The loaded settings.</returns>
        private PluginSettings LoadInternal()
        {
            try
            {
                var path = PluginPaths.GetSettingsPath();
                if (!File.Exists(path))
                {
                    var defaults = CreateDefaults();
                    SaveInternal(defaults, path, false);
                    return defaults;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                    var sanitizedStream = StripUtf8Bom(stream);
                    var settings = serializer.ReadObject(sanitizedStream) as PluginSettings ?? CreateDefaults();
                    settings.ApplyDefaults();
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load settings. Using defaults.", ex);
                var defaults = CreateDefaults();
                try
                {
                    SaveInternal(defaults, PluginPaths.GetSettingsPath(), false);
                }
                catch (Exception saveEx)
                {
                    _logger.Error("Failed to save default settings.", saveEx);
                }

                return defaults;
            }
        }

        /// <summary>
        /// Saves settings to disk without updating the cache.
        /// </summary>
        /// <param name="settings">The settings to save.</param>
        /// <param name="path">The target settings path.</param>
        /// <param name="logSuccess">Whether to log on success.</param>
        private void SaveInternal(PluginSettings settings, string path, bool logSuccess)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                    serializer.WriteObject(stream, settings);
                }
            }
            catch (IOException ex)
            {
                _logger.Error("Settings file is locked; skipping write.", ex);
                return;
            }

            if (logSuccess)
            {
                _logger.Info("Settings saved successfully.");
            }
        }

        /// <summary>
        /// Strips a UTF-8 BOM from a stream if present.
        /// </summary>
        /// <param name="source">The source stream.</param>
        /// <returns>A stream without a BOM at the start.</returns>
        private static Stream StripUtf8Bom(Stream source)
        {
            if (source == null || !source.CanRead)
            {
                return source;
            }

            var buffer = new byte[3];
            var read = source.Read(buffer, 0, 3);
            if (read == 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                var ms = new MemoryStream();
                source.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            source.Position = 0;
            return source;
        }
    }
}
