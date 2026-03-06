using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomMbox.Models.PlatformFolders;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.PlatformFolders
{
    /// <summary>
    /// Resolves LaunchBox platform folders in a centralized, platform-agnostic way.
    /// </summary>
    internal sealed class PlatformFolderResolver
    {
        private readonly LoggingService _logger;

        private static readonly IReadOnlyDictionary<PlatformFolderType, string[]> FolderTypeAliases =
            new Dictionary<PlatformFolderType, string[]>(Enum.GetValues(typeof(PlatformFolderType)).Length)
            {
                [PlatformFolderType.Games] = new[] { "game", "rom", "games", "roms" },
                [PlatformFolderType.Manuals] = new[] { "manual" },
                [PlatformFolderType.Music] = new[] { "music", "soundtrack", "ost" },
                [PlatformFolderType.Images] = new[] { "image", "images" },
                [PlatformFolderType.Videos] = new[] { "video", "videos", "theme video" }
            };

        public PlatformFolderResolver(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Resolves the configured folder path for a platform and folder type.
        /// </summary>
        public string GetPlatformFolder(IPlatform platform, PlatformFolderType folderType)
        {
            var entry = GetPlatformFolderEntry(platform, folderType);
            return entry?.Path ?? string.Empty;
        }

        /// <summary>
        /// Resolves the configured folder path for a platform and folder type.
        /// </summary>
        public string GetPlatformFolder(string platformName, PlatformFolderType folderType, IDataManager dataManager = null)
        {
            var platform = ResolvePlatform(platformName, dataManager);
            return GetPlatformFolder(platform, folderType);
        }

        public string GetPlatformGamesFolder(string platformName, IDataManager dataManager = null)
        {
            return GetPlatformFolder(platformName, PlatformFolderType.Games, dataManager);
        }

        public string GetPlatformManualsFolder(string platformName, IDataManager dataManager = null)
        {
            return GetPlatformFolder(platformName, PlatformFolderType.Manuals, dataManager);
        }

        public string GetPlatformMusicFolder(string platformName, IDataManager dataManager = null)
        {
            return GetPlatformFolder(platformName, PlatformFolderType.Music, dataManager);
        }

        public string GetPlatformImagesFolder(string platformName, IDataManager dataManager = null)
        {
            return GetPlatformFolder(platformName, PlatformFolderType.Images, dataManager);
        }

        public string GetPlatformVideosFolder(string platformName, IDataManager dataManager = null)
        {
            return GetPlatformFolder(platformName, PlatformFolderType.Videos, dataManager);
        }

        /// <summary>
        /// Resolves all platform folders known for the platform.
        /// </summary>
        public IReadOnlyList<PlatformFolderEntry> GetPlatformFolders(string platformName, IDataManager dataManager = null)
        {
            var platform = ResolvePlatform(platformName, dataManager);
            return GetPlatformFolders(platform);
        }

        /// <summary>
        /// Resolves all platform folders known for the platform.
        /// </summary>
        public IReadOnlyList<PlatformFolderEntry> GetPlatformFolders(IPlatform platform)
        {
            var resolved = new List<PlatformFolderEntry>();
            if (platform == null)
            {
                _logger?.Warning("Platform folder resolution skipped: platform is null.");
                return resolved;
            }

            var rawEntries = GetPlatformFolderEntries(platform);
            _logger?.Info($"Resolving configured folders for platform '{platform.Name ?? string.Empty}'. Entries={rawEntries.Count}.");

            foreach (var folderType in Enum.GetValues(typeof(PlatformFolderType)).Cast<PlatformFolderType>())
            {
                var entry = BuildEntry(platform, folderType, rawEntries);
                if (entry != null)
                {
                    resolved.Add(entry);
                }
            }

            _logger?.Info($"Resolved configured folder mappings for '{platform.Name ?? string.Empty}': {string.Join(", ", resolved.Select(entry => entry.Type + "=" + entry.Path))}.");
            return resolved;
        }

        private PlatformFolderEntry GetPlatformFolderEntry(IPlatform platform, PlatformFolderType folderType)
        {
            if (platform == null)
            {
                _logger?.Warning($"Platform folder resolution failed: platform is null. Type={folderType}.");
                return null;
            }

            var rawEntries = GetPlatformFolderEntries(platform);
            var entry = BuildEntry(platform, folderType, rawEntries);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
            {
                _logger?.Warning($"No configured {folderType} folder found for platform '{platform.Name ?? string.Empty}'.");
            }

            return entry;
        }

        private PlatformFolderEntry BuildEntry(IPlatform platform, PlatformFolderType folderType, List<RawPlatformFolderEntry> rawEntries)
        {
            if (platform == null)
            {
                return null;
            }

            var matching = rawEntries
                .FirstOrDefault(entry => IsFolderTypeMatch(entry.MediaType, folderType));
            if (matching == null && folderType == PlatformFolderType.Games)
            {
                matching = rawEntries.FirstOrDefault(entry => IsFolderTypeMatch(entry.TypeName, folderType));
            }

            var resolvedPath = ResolvePlatformFolderPath(platform, folderType, matching?.Path ?? string.Empty);
            return new PlatformFolderEntry(folderType, matching?.MediaType ?? matching?.TypeName ?? string.Empty, resolvedPath, !string.IsNullOrWhiteSpace(resolvedPath));
        }

        private List<RawPlatformFolderEntry> GetPlatformFolderEntries(IPlatform platform)
        {
            var entries = new List<RawPlatformFolderEntry>();
            if (platform == null)
            {
                return entries;
            }

            try
            {
                var folders = platform.GetAllPlatformFolders();
                if (folders == null || folders.Length == 0)
                {
                    _logger?.Info($"No platform folders returned for '{platform.Name ?? string.Empty}'.");
                    return entries;
                }

                _logger?.Info($"Platform folders reported: {folders.Length} for '{platform.Name ?? string.Empty}'.");
                foreach (var folder in folders)
                {
                    if (folder == null)
                    {
                        continue;
                    }

                    var folderType = folder.GetType();
                    var mediaType = TryResolveFolderProperty(folder, folderType, "MediaType")
                        ?? TryResolveFolderProperty(folder, folderType, "ImageType")
                        ?? TryResolveFolderProperty(folder, folderType, "Type");
                    var folderPath = TryResolveFolderProperty(folder, folderType, "FolderPath")
                        ?? TryResolveFolderProperty(folder, folderType, "Path")
                        ?? TryResolveFolderProperty(folder, folderType, "Folder")
                        ?? TryResolveFolderProperty(folder, folderType, "Location");
                    _logger?.Info($"Platform folder entry: Path='{folderPath ?? string.Empty}', MediaType='{mediaType ?? string.Empty}', Type='{folderType.FullName}'.");
                    entries.Add(new RawPlatformFolderEntry(folderPath, mediaType, folderType.FullName));
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform folders for '{platform.Name ?? string.Empty}': {ex.Message}");
            }

            return entries;
        }

        private IPlatform ResolvePlatform(string platformName, IDataManager dataManager)
        {
            if (string.IsNullOrWhiteSpace(platformName))
            {
                _logger?.Warning("Platform folder resolution skipped: platform name is empty.");
                return null;
            }

            var manager = dataManager ?? Unbroken.LaunchBox.Plugins.PluginHelper.DataManager;
            if (manager == null)
            {
                _logger?.Warning($"Platform folder resolution skipped: LaunchBox data manager unavailable. Platform='{platformName}'.");
                return null;
            }

            var platform = manager.GetPlatformByName(platformName);
            if (platform == null)
            {
                _logger?.Warning($"Platform folder resolution failed: platform not found '{platformName}'.");
            }

            return platform;
        }

        private string ResolvePlatformFolderPath(IPlatform platform, PlatformFolderType folderType, string configuredPath)
        {
            if (platform == null)
            {
                return string.Empty;
            }

            if (folderType == PlatformFolderType.Games)
            {
                var platformFolder = platform.Folder;
                if (!string.IsNullOrWhiteSpace(platformFolder))
                {
                    _logger?.Info($"Using platform Folder property for '{platform.Name ?? string.Empty}': '{platformFolder}'.");
                    return ResolveConfiguredFolder(platformFolder, "Platform.Folder");
                }
            }

            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                _logger?.Info($"Using platform folder entry for '{platform.Name ?? string.Empty}': '{configuredPath}'.");
                return ResolveConfiguredFolder(configuredPath, "PlatformFolders");
            }

            return string.Empty;
        }

        private string ResolveConfiguredFolder(string folderValue, string source)
        {
            if (string.IsNullOrWhiteSpace(folderValue))
            {
                return string.Empty;
            }

            var resolved = folderValue;
            if (!Path.IsPathRooted(resolved))
            {
                var launchBoxRootLocal = PluginPaths.GetLaunchBoxRootDirectory();
                _logger?.Info($"LaunchBox root resolved to '{launchBoxRootLocal}'.");
                if (!string.IsNullOrWhiteSpace(launchBoxRootLocal))
                {
                    resolved = Path.Combine(launchBoxRootLocal, resolved);
                }
            }

            _logger?.Info($"Resolved platform folder from {source}: '{resolved}'. Exists={Directory.Exists(resolved)}");
            return resolved;
        }

        private static bool IsFolderTypeMatch(string candidate, PlatformFolderType folderType)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var normalized = candidate.Trim();
            if (normalized.Equals(folderType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!FolderTypeAliases.TryGetValue(folderType, out var aliases) || aliases == null)
            {
                return false;
            }

            return aliases.Any(alias => normalized.Equals(alias, StringComparison.OrdinalIgnoreCase));
        }

        private string TryResolveFolderProperty(object folder, Type folderType, string propertyName)
        {
            var property = folderType.GetProperty(propertyName);
            if (property == null)
            {
                return null;
            }

            var value = property.GetValue(folder) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _logger?.Info($"Platform folder property '{propertyName}' resolved to '{value}'.");
            }

            return value;
        }

        private sealed class RawPlatformFolderEntry
        {
            public RawPlatformFolderEntry(string path, string mediaType, string typeName)
            {
                Path = path;
                MediaType = mediaType;
                TypeName = typeName;
            }

            public string Path { get; }

            public string MediaType { get; }

            public string TypeName { get; }
        }
    }
}
