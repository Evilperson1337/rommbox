using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RomMbox.Models.Install;
using RomMbox.Services.Paths;
using RomMbox.Services.Settings;
using RomMbox.Services.Logging;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Resolves installation destinations for games based on platform and settings.
    /// </summary>
    internal sealed class InstallDestinationService
    {
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;

        /// <summary>
        /// Creates a new install destination service.
        /// </summary>
        /// <param name="logger">Logging service.</param>
        /// <param name="settingsManager">Settings manager.</param>
        public InstallDestinationService(LoggingService logger, SettingsManager settingsManager)
        {
            _logger = logger;
            _settingsManager = settingsManager;
        }

        /// <summary>
        /// Resolves an install location for a game using the default installer mode.
        /// </summary>
        /// <param name="game">The game to install.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The resolved install location result.</returns>
        public Task<InstallLocationResult> ResolveInstallLocationAsync(IGame game, CancellationToken cancellationToken)
        {
            return ResolveInstallLocationAsync(game, InstallerMode.Manual, cancellationToken);
        }

        /// <summary>
        /// Resolves an install location for a game and installer mode.
        /// </summary>
        /// <param name="game">The game to install.</param>
        /// <param name="installerMode">The installer mode.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The resolved install location result.</returns>
        public Task<InstallLocationResult> ResolveInstallLocationAsync(IGame game, InstallerMode installerMode, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return Task.FromResult(new InstallLocationResult
                {
                    Success = false,
                    Message = "Game is required to resolve install location."
                });
            }

            var dataManager = PluginHelper.DataManager;
            if (dataManager == null)
            {
                return Task.FromResult(new InstallLocationResult
                {
                    Success = false,
                    Message = "LaunchBox DataManager is unavailable."
                });
            }

            var platform = dataManager.GetPlatformByName(game.Platform);
            if (platform == null)
            {
                return Task.FromResult(new InstallLocationResult
                {
                    Success = false,
                    Message = "LaunchBox platform not found for game."
                });
            }

            var settings = _settingsManager?.Load() ?? new PluginSettings();
            if (IsWindowsPlatform(platform.Name))
            {
                var defaultDirectory = ResolveDefaultInstallDirectory(platform, settings);
                _logger?.Info($"Windows platform detected for '{game.Title}'. InstallerMode={installerMode}. Using default install directory '{defaultDirectory}'.");
                if (string.IsNullOrWhiteSpace(defaultDirectory))
                {
                    return Task.FromResult(new InstallLocationResult
                    {
                        Success = false,
                        Message = "Default install directory is not configured."
                    });
                }
                return Task.FromResult(new InstallLocationResult
                {
                    Success = true,
                    InstallDirectory = defaultDirectory
                });
            }

            var platformFolder = ResolvePlatformRomFolder(platform);
            if (string.IsNullOrWhiteSpace(platformFolder))
            {
                _logger?.Warning($"Platform ROM folder missing for '{game.Title}' ({platform.Name}). Prompting for install directory.");
                return PromptForPlatformFolderAsync(platform, dataManager, cancellationToken);
            }

            _logger?.Info($"Resolved install folder for '{game.Title}' to '{platformFolder}'.");
            return Task.FromResult(new InstallLocationResult
            {
                Success = true,
                InstallDirectory = platformFolder
            });
        }

        /// <summary>
        /// Resolves a media folder path for a platform by media type (e.g., "Screenshot - Gameplay").
        /// </summary>
        /// <param name="platform">The LaunchBox platform.</param>
        /// <param name="mediaType">The media type label to match.</param>
        /// <returns>The resolved media folder path or empty string.</returns>
        public string ResolvePlatformMediaFolder(IPlatform platform, string mediaType)
        {
            if (platform == null || string.IsNullOrWhiteSpace(mediaType))
            {
                return string.Empty;
            }

            var entries = GetPlatformFolderEntriesByMediaType(
                platform,
                candidate => string.Equals(candidate?.Trim(), mediaType.Trim(), StringComparison.OrdinalIgnoreCase));
            var match = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Path));
            if (match == null)
            {
                _logger?.Info($"No platform media folder matched '{mediaType}' for '{platform.Name ?? string.Empty}'.");
                return string.Empty;
            }

            if (Path.IsPathRooted(match.Path))
            {
                return match.Path;
            }

            var root = PluginPaths.GetLaunchBoxRootDirectory();
            if (!string.IsNullOrWhiteSpace(root))
            {
                return Path.Combine(root, match.Path);
            }

            return match.Path;
        }

        /// <summary>
        /// Determines whether a platform should be treated as Windows/PC.
        /// </summary>
        /// <param name="platformName">The platform name.</param>
        /// <returns><c>true</c> when the platform looks like Windows/PC.</returns>
        internal static bool IsWindowsPlatform(string platformName)
        {
            if (string.IsNullOrWhiteSpace(platformName))
            {
                return false;
            }

            return platformName.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0
                || platformName.IndexOf("pc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Prompts the user for a Windows install directory.
        /// </summary>
        /// <param name="platformName">The platform name.</param>
        /// <param name="defaultDirectory">Default directory suggestion.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The chosen install location result.</returns>
        private async Task<InstallLocationResult> PromptForInstallFolderAsync(string platformName, string defaultDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(defaultDirectory) && !Directory.Exists(defaultDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(defaultDirectory);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to create default install directory '{defaultDirectory}': {ex.Message}");
                    }
                }

                using (var dialog = new FolderBrowserDialog
                {
                    Description = $"Select install directory for {platformName} games",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    SelectedPath = defaultDirectory
                })
                {
                    var result = dialog.ShowDialog();
                    if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        _logger?.Warning("User canceled Windows install directory prompt.");
                        return new InstallLocationResult
                        {
                            Success = false,
                            Message = "Install directory selection was canceled."
                        };
                    }

                    _logger?.Info($"User selected install directory '{dialog.SelectedPath}'.");
                    return new InstallLocationResult
                    {
                        Success = true,
                        InstallDirectory = dialog.SelectedPath
                    };
                }
            }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Prompts the user to select a ROM folder for the platform.
        /// </summary>
        /// <param name="platform">The LaunchBox platform.</param>
        /// <param name="dataManager">The LaunchBox data manager.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The chosen install location result.</returns>
        private async Task<InstallLocationResult> PromptForPlatformFolderAsync(IPlatform platform, IDataManager dataManager, CancellationToken cancellationToken)
        {
            if (platform == null)
            {
                return new InstallLocationResult
                {
                    Success = false,
                    Message = "Platform ROM folder is not configured."
                };
            }

            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = $"Select ROM folder for {platform.Name} games",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    SelectedPath = platform.Folder ?? string.Empty
                })
                {
                    var result = dialog.ShowDialog();
                    if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        _logger?.Warning("User canceled platform ROM folder selection.");
                        return new InstallLocationResult
                        {
                            Success = false,
                            Message = "Platform ROM folder is not configured."
                        };
                    }

                    platform.Folder = dialog.SelectedPath;
                    try
                    {
                        dataManager?.Save(true);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to persist platform folder '{dialog.SelectedPath}': {ex.Message}");
                    }

                    _logger?.Info($"Platform ROM folder set for '{platform.Name}': '{dialog.SelectedPath}'.");
                    return new InstallLocationResult
                    {
                        Success = true,
                        InstallDirectory = dialog.SelectedPath
                    };
                }
            }, System.Windows.Threading.DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a platform ROM folder from LaunchBox metadata or existing games.
        /// </summary>
        /// <param name="platform">The platform to resolve.</param>
        /// <returns>The resolved folder path or empty string.</returns>
        private string ResolvePlatformRomFolder(IPlatform platform)
        {
            if (platform == null)
            {
                return string.Empty;
            }

            var defaultFolder = platform.Folder;
            _logger?.Info($"Platform folder property for '{platform.Name ?? string.Empty}': '{defaultFolder ?? string.Empty}'.");
            if (string.IsNullOrWhiteSpace(defaultFolder))
            {
                _logger?.Info($"Platform folder property is empty for '{platform.Name ?? string.Empty}'; skipping platform media folders for install resolution.");
            }

            _logger?.Info($"ResolvePlatformRomFolder: Platform='{platform.Name ?? string.Empty}', PlatformFolder='{defaultFolder ?? string.Empty}'.");
            if (!string.IsNullOrWhiteSpace(defaultFolder))
            {
                _logger?.Info($"Platform folder configured: '{defaultFolder}'.");
                if (Path.IsPathRooted(defaultFolder))
                {
                    _logger?.Info($"Platform folder is rooted: '{defaultFolder}'. Exists={Directory.Exists(defaultFolder)}");
                    return defaultFolder;
                }

                var launchBoxRootLocal = PluginPaths.GetLaunchBoxRootDirectory();
                _logger?.Info($"LaunchBox root resolved to '{launchBoxRootLocal}'.");
                if (!string.IsNullOrWhiteSpace(launchBoxRootLocal))
                {
                    var combined = Path.Combine(launchBoxRootLocal, defaultFolder);
                    _logger?.Info($"Combined platform folder path: '{combined}'. Exists={Directory.Exists(combined)}");
                    if (Directory.Exists(combined))
                    {
                        return combined;
                    }
                }

                _logger?.Info($"Relative platform folder exists without root: Exists={Directory.Exists(defaultFolder)}");
                if (Directory.Exists(defaultFolder))
                {
                    return defaultFolder;
                }
            }
            else
            {
                _logger?.Info("Platform folder is empty; attempting to resolve from existing games and LaunchBox root.");
            }

            var xmlFolder = TryResolvePlatformFolderFromXml(platform.Name);
            if (!string.IsNullOrWhiteSpace(xmlFolder))
            {
                _logger?.Info($"Resolved platform ROM folder from platform XML: '{xmlFolder}'.");
                return xmlFolder;
            }

            if (!IsWindowsPlatform(platform.Name)
                && platform.GetAllGames(includeHidden: true, includeBroken: true) is { Length: > 0 } games)
            {
                _logger?.Info($"Searching {games.Length} existing games for a ROM folder hint.");
                foreach (var game in games)
                {
                    var path = game?.ApplicationPath;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        var directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(directory) && !Path.IsPathRooted(directory))
                        {
                            var root = PluginPaths.GetLaunchBoxRootDirectory();
                            if (!string.IsNullOrWhiteSpace(root))
                            {
                                directory = Path.Combine(root, directory);
                            }
                        }

                        directory = Path.GetDirectoryName(directory);

                        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                        {
                            _logger?.Info($"Resolved platform ROM folder from existing game path: {directory}");
                            return directory;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (IsWindowsPlatform(platform.Name))
            {
                _logger?.Info("Skipping existing-game folder hints for Windows platform; using LaunchBox Games\\Windows fallback when needed.");
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            _logger?.Info($"LaunchBox root for fallback resolution: '{launchBoxRoot}'.");
            if (!string.IsNullOrWhiteSpace(launchBoxRoot) && !string.IsNullOrWhiteSpace(platform.Name))
            {
                var fallback = Path.Combine(launchBoxRoot, "Games", platform.Name);
                if (Directory.Exists(fallback))
                {
                    _logger?.Info($"Resolved platform ROM folder from LaunchBox Games folder: {fallback}");
                    return fallback;
                }
            }

            return string.Empty;
        }

        private string TryResolvePlatformFolderFromXml(string platformName)
        {
            if (string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            try
            {
                var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(launchBoxRoot))
                {
                    return string.Empty;
                }

                var platformPath = ResolvePlatformXmlPath(platformName, launchBoxRoot);
                if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
                {
                    return string.Empty;
                }

                var doc = XDocument.Load(platformPath);
                var folderValue = doc.Root?.Element("Folder")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(folderValue))
                {
                    return string.Empty;
                }

                if (Path.IsPathRooted(folderValue))
                {
                    return folderValue;
                }

                return Path.Combine(launchBoxRoot, folderValue);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to resolve platform folder from XML for '{platformName}': {ex.Message}");
                return string.Empty;
            }
        }

        private static string ResolvePlatformXmlPath(string platformName, string launchBoxRoot)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((platformName ?? string.Empty)
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray())
                .Trim();
            return Path.Combine(launchBoxRoot, "Data", "Platforms", sanitized + ".xml");
        }

        /// <summary>
        /// Resolves the default install directory for a platform.
        /// </summary>
        /// <param name="platform">The platform to resolve.</param>
        /// <param name="settings">Plugin settings for defaults.</param>
        /// <returns>The resolved directory path.</returns>
        private string ResolveDefaultInstallDirectory(IPlatform platform, PluginSettings settings)
        {
            var platformFolder = ResolvePlatformRomFolder(platform);
            if (!string.IsNullOrWhiteSpace(platformFolder))
            {
                if (!Path.IsPathRooted(platformFolder))
                {
                    var launchBoxRootLocal = PluginPaths.GetLaunchBoxRootDirectory();
                    if (!string.IsNullOrWhiteSpace(launchBoxRootLocal))
                    {
                        return Path.Combine(launchBoxRootLocal, platformFolder);
                    }
                }

                return platformFolder;
            }

            var configuredDefault = settings?.GetDefaultWindowsInstallDirectory() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configuredDefault))
            {
                return configuredDefault;
            }

            var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(launchBoxRoot))
            {
                return string.Empty;
            }

            return Path.Combine(launchBoxRoot, "Games", "Windows");
        }

        private string TryResolvePlatformFolderFromFolders(IPlatform platform, Func<string, bool> mediaTypeFilter)
        {
            try
            {
                var entries = GetPlatformFolderEntries(platform);
                if (entries.Count == 0)
                {
                    return string.Empty;
                }

                if (mediaTypeFilter != null)
                {
                    foreach (var entry in entries)
                    {
                        if (!mediaTypeFilter(entry.MediaType ?? string.Empty))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(entry.Path))
                        {
                            _logger?.Info($"Resolved platform folder from platform folders: '{entry.Path}' (Type='{entry.TypeName}', MediaType='{entry.MediaType ?? string.Empty}').");
                            return entry.Path;
                        }
                    }
                }

                var gameEntry = entries.FirstOrDefault(entry => IsGameMediaType(entry.MediaType) && !string.IsNullOrWhiteSpace(entry.Path));
                if (gameEntry != null)
                {
                    _logger?.Info($"Resolved platform folder from Game media type: '{gameEntry.Path}' (Type='{gameEntry.TypeName}', MediaType='{gameEntry.MediaType ?? string.Empty}').");
                    return gameEntry.Path;
                }

                var fallbackEntry = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Path));
                if (fallbackEntry != null)
                {
                    _logger?.Info($"Resolved platform folder from first available entry: '{fallbackEntry.Path}' (Type='{fallbackEntry.TypeName}', MediaType='{fallbackEntry.MediaType ?? string.Empty}').");
                    return fallbackEntry.Path;
                }

                if (mediaTypeFilter != null)
                {
                    _logger?.Info($"No platform folders matched the requested media type filter for '{platform?.Name ?? string.Empty}'.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform folders for '{platform?.Name ?? string.Empty}': {ex.Message}");
            }

            return string.Empty;
        }

        private sealed class PlatformFolderEntry
        {
            public PlatformFolderEntry(string path, string mediaType, string typeName)
            {
                Path = path;
                MediaType = mediaType;
                TypeName = typeName;
            }

            public string Path { get; }
            public string MediaType { get; }
            public string TypeName { get; }
        }

        private List<PlatformFolderEntry> GetPlatformFolderEntriesByMediaType(IPlatform platform, Func<string, bool> mediaTypeFilter)
        {
            var entries = GetPlatformFolderEntries(platform);
            if (mediaTypeFilter == null)
            {
                return entries;
            }

            return entries.FindAll(entry => mediaTypeFilter(entry.MediaType ?? string.Empty));
        }

        private List<PlatformFolderEntry> GetPlatformFolderEntries(IPlatform platform)
        {
            var entries = new List<PlatformFolderEntry>();
            try
            {
                var folders = platform?.GetAllPlatformFolders();
                if (folders == null || folders.Length == 0)
                {
                    _logger?.Info($"No platform folders returned for '{platform?.Name ?? string.Empty}'.");
                    return entries;
                }

                _logger?.Info($"Platform folders reported: {folders.Length} for '{platform?.Name ?? string.Empty}'.");
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
                    entries.Add(new PlatformFolderEntry(folderPath, mediaType, folderType.FullName));
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read platform folders for '{platform?.Name ?? string.Empty}': {ex.Message}");
            }

            return entries;
        }

        private static bool IsRomOrGameMediaType(string mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return false;
            }

            return mediaType.IndexOf("rom", StringComparison.OrdinalIgnoreCase) >= 0
                || mediaType.IndexOf("game", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGameMediaType(string mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return false;
            }

            return string.Equals(mediaType.Trim(), "Game", StringComparison.OrdinalIgnoreCase);
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
    }
}
