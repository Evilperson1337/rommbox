using System;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformFolders;
using RomMbox.Services.Paths;
using RomMbox.Services.Settings;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformFolders;
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
        private readonly PlatformFolderResolver _folderResolver;

        /// <summary>
        /// Creates a new install destination service.
        /// </summary>
        /// <param name="logger">Logging service.</param>
        /// <param name="settingsManager">Settings manager.</param>
        public InstallDestinationService(LoggingService logger, SettingsManager settingsManager)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _folderResolver = new PlatformFolderResolver(_logger);
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

            var entry = _folderResolver
                .GetPlatformFolders(platform)
                .FirstOrDefault(folder => string.Equals(folder.Name?.Trim(), mediaType.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
            {
                _logger?.Info($"No platform media folder matched '{mediaType}' for '{platform.Name ?? string.Empty}'.");
                return string.Empty;
            }

            return entry.Path;
        }

        /// <summary>
        /// Resolves a configured platform folder by type.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        /// <param name="folderType">The platform folder type to resolve.</param>
        /// <returns>The resolved folder path or empty string.</returns>
        public string GetPlatformFolder(string platformName, PlatformFolderType folderType)
        {
            return _folderResolver.GetPlatformFolder(platformName, folderType);
        }

        /// <summary>
        /// Resolves the configured platform games folder.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        public string GetPlatformGamesFolder(string platformName)
        {
            return _folderResolver.GetPlatformGamesFolder(platformName);
        }

        /// <summary>
        /// Resolves the configured platform manuals folder.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        public string GetPlatformManualsFolder(string platformName)
        {
            return _folderResolver.GetPlatformManualsFolder(platformName);
        }

        /// <summary>
        /// Resolves the configured platform music folder.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        public string GetPlatformMusicFolder(string platformName)
        {
            return _folderResolver.GetPlatformMusicFolder(platformName);
        }

        /// <summary>
        /// Resolves the configured platform images folder.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        public string GetPlatformImagesFolder(string platformName)
        {
            return _folderResolver.GetPlatformImagesFolder(platformName);
        }

        /// <summary>
        /// Resolves the configured platform videos folder.
        /// </summary>
        /// <param name="platformName">LaunchBox platform name.</param>
        public string GetPlatformVideosFolder(string platformName)
        {
            return _folderResolver.GetPlatformVideosFolder(platformName);
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
                var resolvedDefault = ResolveConfiguredFolder(defaultFolder, "Platform.Folder", defaultFolder);
                if (!string.IsNullOrWhiteSpace(resolvedDefault))
                {
                    return resolvedDefault;
                }
            }
            else
            {
                _logger?.Info("Platform folder is empty; attempting to resolve from existing games and LaunchBox root.");
            }

            var configuredFolder = _folderResolver.GetPlatformFolder(platform, PlatformFolderType.Games);
            if (!string.IsNullOrWhiteSpace(configuredFolder))
            {
                return configuredFolder;
            }

            var xmlFolder = TryResolvePlatformFolderFromXml(platform.Name);
            if (!string.IsNullOrWhiteSpace(xmlFolder))
            {
                var resolvedXml = ResolveConfiguredFolder(xmlFolder, "PlatformXml", xmlFolder);
                if (!string.IsNullOrWhiteSpace(resolvedXml))
                {
                    return resolvedXml;
                }
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

                        var gameDirectory = directory;
                        if (string.IsNullOrWhiteSpace(gameDirectory))
                        {
                            continue;
                        }

                        var platformCandidate = Path.GetDirectoryName(gameDirectory);
                        if (string.IsNullOrWhiteSpace(platformCandidate))
                        {
                            continue;
                        }

                        var launchBoxRootLocal = PluginPaths.GetLaunchBoxRootDirectory();
                        var gamesRoot = string.IsNullOrWhiteSpace(launchBoxRootLocal)
                            ? null
                            : Path.Combine(launchBoxRootLocal, "Games");
                        if (!string.IsNullOrWhiteSpace(gamesRoot))
                        {
                            var normalizedGamesRoot = Path.GetFullPath(gamesRoot)
                                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            var normalizedCandidate = Path.GetFullPath(platformCandidate)
                                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            if (string.Equals(normalizedGamesRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger?.Info($"Existing game path resolved to shared Games root '{normalizedCandidate}'. Skipping.");
                                continue;
                            }
                        }

                        if (Directory.Exists(platformCandidate))
                        {
                            _logger?.Info($"Resolved platform ROM folder from existing game path: {platformCandidate}");
                            return platformCandidate;
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
                _logger?.Warning($"Platform folder not found in metadata for '{platform.Name}'. Falling back to '{fallback}'. Exists={Directory.Exists(fallback)}");
                return fallback;
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
                    _logger?.Info($"Platform XML not found for '{platformName}'. Path='{platformPath ?? string.Empty}'.");
                    return string.Empty;
                }

                var doc = XDocument.Load(platformPath);
                var folderValue = doc.Root?.Element("Folder")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(folderValue))
                {
                    _logger?.Info($"Platform XML folder value missing for '{platformName}'.");
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

        private string ResolveConfiguredFolder(string folderValue, string source, string configuredValue)
        {
            if (string.IsNullOrWhiteSpace(folderValue))
            {
                return string.Empty;
            }

            if (ShouldIgnoreConfiguredFolder(configuredValue, source))
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

        private bool ShouldIgnoreConfiguredFolder(string folderValue, string source)
        {
            if (string.IsNullOrWhiteSpace(folderValue))
            {
                return true;
            }

            var normalized = folderValue.Replace('\\', '/');
            if (normalized.IndexOf("/images/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Warning($"Ignoring {source} folder value '{folderValue}' because it points to Images.");
                return true;
            }

            return false;
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

    }
}
