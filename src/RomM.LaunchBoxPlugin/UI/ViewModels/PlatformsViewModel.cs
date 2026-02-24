using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;
using Unbroken.LaunchBox.Plugins;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model that manages platform mappings and install configuration UI.
/// </summary>
public sealed class PlatformsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly LoggingService _logger;
    private readonly SettingsManager _settingsManager;
    private PlatformMappingService _mappingService;
    private string _mappingServiceServerUrl = string.Empty;
    private bool _hasLoaded;
    private const string UnmappedLabel = "--- Not Mapped ---";

    /// <summary>
    /// Creates the view model and loads initial mappings.
    /// </summary>
    /// <param name="shell">The main window view model shell.</param>
    public PlatformsViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        _logger = LoggingServiceFactory.Create();
        _settingsManager = new SettingsManager(_logger);
        
        EnsureMappingService();

        Platforms = new ObservableCollection<string>();
        LaunchBoxPlatforms = new ObservableCollection<string>();
        Mappings = new ObservableCollection<Models.PlatformMapping>();

        RefreshCommand = new RelayCommand(async () => await LoadMappingsAsync());
        OpenAuditCommand = new RelayCommand(OpenAuditWindow);
        SaveMappingsCommand = new RelayCommand(async () => await SaveMappingsAsync(showConfirmation: true));
        ConfigureCommand = new RelayCommand<Models.PlatformMapping>(OpenConfigureDialog, mapping => mapping != null && !mapping.Exclude);

        // Lazy-load when the tab is activated.
    }

    /// <summary>
    /// Gets the RomM platform names displayed in the UI.
    /// </summary>
    public ObservableCollection<string> Platforms { get; }
    /// <summary>
    /// Gets the LaunchBox platform names available for mapping.
    /// </summary>
    public ObservableCollection<string> LaunchBoxPlatforms { get; }
    /// <summary>
    /// Gets the platform mapping rows.
    /// </summary>
    public ObservableCollection<Models.PlatformMapping> Mappings { get; }

    private string _selectedPlatform = "";
    /// <summary>
    /// Gets or sets the selected platform name.
    /// </summary>
    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (SetProperty(ref _selectedPlatform, value))
            {
                _shell.SelectedPlatform = value;
            }
        }
    }

    private int _selectedTabIndex;
    /// <summary>
    /// Gets or sets the selected tab index in the platforms view.
    /// </summary>
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

    /// <summary>
    /// Command that refreshes platform mappings from RomM.
    /// </summary>
    public RelayCommand RefreshCommand { get; }
    /// <summary>
    /// Command that opens the platform audit window.
    /// </summary>
    public RelayCommand OpenAuditCommand { get; }
    /// <summary>
    /// Command that saves updated platform mappings.
    /// </summary>
    public RelayCommand SaveMappingsCommand { get; }
    /// <summary>
    /// Command that opens the platform install configuration dialog.
    /// </summary>
    public RelayCommand<Models.PlatformMapping> ConfigureCommand { get; }

    private ViewModels.PlatformInstallConfigViewModel _activeConfiguration;
    public ViewModels.PlatformInstallConfigViewModel ActiveConfiguration
    {
        get => _activeConfiguration;
        set
        {
            if (SetProperty(ref _activeConfiguration, value))
            {
                RaisePropertyChanged(nameof(IsConfiguring));
            }
        }
    }

    public bool IsConfiguring => ActiveConfiguration != null;

    private Models.PlatformMapping _selectedMapping;
    public Models.PlatformMapping SelectedMapping
    {
        get => _selectedMapping;
        set => SetProperty(ref _selectedMapping, value);
    }

    private void OpenAuditWindow()
    {
        try
        {
            var window = new RomMbox.UI.RomMAuditWindow
            {
                Owner = Application.Current?.MainWindow
            };
            window.Show();
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to open RomM audit window.", ex);
        }
    }

    /// <summary>
    /// Reloads mappings by re-running the load routine.
    /// </summary>
    public Task ReloadMappingsAsync()
    {
        return LoadMappingsAsync();
    }

    /// <summary>
    /// Loads mappings once when the tab is first activated.
    /// </summary>
    public Task EnsureLoadedAsync()
    {
        if (_hasLoaded)
        {
            return Task.CompletedTask;
        }

        return LoadMappingsAsync();
    }

    /// <summary>
    /// Loads platform mappings from RomM and populates the UI collections.
    /// </summary>
    private async Task LoadMappingsAsync()
    {
        try
        {
            _hasLoaded = true;
            EnsureMappingService();
            if (_mappingService == null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Platforms.Clear();
                    LaunchBoxPlatforms.Clear();
                    LaunchBoxPlatforms.Add(UnmappedLabel);
                    foreach (var name in LoadLaunchBoxPlatforms())
                    {
                        LaunchBoxPlatforms.Add(name);
                    }

                    Mappings.Clear();
                    Mappings.Add(new Models.PlatformMapping
                    {
                        RomMPlatform = "Server not configured",
                        LaunchBoxPlatform = "Please configure server connection first",
                        Exclude = false,
                        DisableAutoImport = false,
                        ExtractAfterDownload = false,
                        ExtractionBehavior = RomMbox.Models.PlatformMapping.ExtractionBehavior.Subfolder,
                        InstallScenario = RomMbox.Models.Install.InstallScenario.Basic,
                        SelfContained = true,
                        TargetImportFile = "",
                        InstallerSilentArgs = "",
                        InstallerMode = RomMbox.Models.Install.InstallerMode.Manual,
                        AssociatedEmulatorId = string.Empty,
                        MusicRootPath = "",
                        InstallOst = false,
                        OstInstallLocation = RomMbox.Models.PlatformMapping.OptionalContentLocation.Centralized,
                        BonusRootPath = "",
                        InstallBonus = false,
                        BonusInstallLocation = RomMbox.Models.PlatformMapping.OptionalContentLocation.Centralized,
                        PreReqsRootPath = "",
                        InstallPreReqs = false,
                        CustomInstallDirectory = ""
                    });
                });
                return;
            }

            var result = await _mappingService.DiscoverPlatformsAsync(CancellationToken.None).ConfigureAwait(false);
            var excluded = _mappingService.GetExcludedRommPlatformIds() ?? Array.Empty<string>();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Platforms.Clear();
                LaunchBoxPlatforms.Clear();
                LaunchBoxPlatforms.Add(UnmappedLabel);
                foreach (var name in LoadLaunchBoxPlatforms())
                {
                    LaunchBoxPlatforms.Add(name);
                }

                Mappings.Clear();
                foreach (var mapping in result.Mappings)
                {
                    var excludedMatch = excluded.Contains(mapping.RommPlatformId, StringComparer.OrdinalIgnoreCase);
                    Platforms.Add(mapping.RommPlatformName);
                    Mappings.Add(new Models.PlatformMapping
                    {
                        RommPlatformId = mapping.RommPlatformId,
                        RomMPlatform = mapping.RommPlatformName,
                        LaunchBoxPlatform = string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatformName)
                            ? UnmappedLabel
                            : mapping.LaunchBoxPlatformName,
                        Exclude = excludedMatch,
                        DisableAutoImport = mapping.DisableAutoImport,
                        InstallScenario = mapping.InstallScenario,
                        SelfContained = mapping.SelfContained,
                        TargetImportFile = mapping.TargetImportFile,
                        InstallerSilentArgs = mapping.InstallerSilentArgs,
                        InstallerMode = mapping.InstallerMode,
                        AssociatedEmulatorId = mapping.AssociatedEmulatorId,
                        MusicRootPath = mapping.MusicRootPath,
                        InstallOst = mapping.InstallOst,
                        OstInstallLocation = mapping.OstInstallLocation,
                        BonusRootPath = mapping.BonusRootPath,
                        InstallBonus = mapping.InstallBonus,
                        BonusInstallLocation = mapping.BonusInstallLocation,
                        PreReqsRootPath = mapping.PreReqsRootPath,
                        InstallPreReqs = mapping.InstallPreReqs,
                        ExtractAfterDownload = mapping.ExtractAfterDownload,
                        ExtractionBehavior = mapping.ExtractionBehavior,
                        CustomInstallDirectory = mapping.CustomInstallDirectory
                    });
                }

                if (Platforms.Count > 0)
                {
                    SelectedPlatform = Platforms[0];
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load platform mappings.", ex);
            
            // Check if this is a missing credentials error
            if (ex.Message.Contains("Missing credentials") || ex.Message.Contains("Server URL is not configured"))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Platforms.Clear();
                    LaunchBoxPlatforms.Clear();
                    LaunchBoxPlatforms.Add(UnmappedLabel);
                    foreach (var name in LoadLaunchBoxPlatforms())
                    {
                        LaunchBoxPlatforms.Add(name);
                    }

                    Mappings.Clear();
                    Mappings.Add(new Models.PlatformMapping
                    {
                        RomMPlatform = "Server not configured",
                        LaunchBoxPlatform = "Please configure server connection first",
                        Exclude = false,
                        DisableAutoImport = false,
                        ExtractAfterDownload = false,
                        ExtractionBehavior = RomMbox.Models.PlatformMapping.ExtractionBehavior.Subfolder,
                        InstallScenario = RomMbox.Models.Install.InstallScenario.Basic,
                        SelfContained = true,
                        TargetImportFile = "",
                        InstallerSilentArgs = "",
                        InstallerMode = RomMbox.Models.Install.InstallerMode.Manual,
                        AssociatedEmulatorId = string.Empty,
                        MusicRootPath = "",
                        InstallOst = false,
                        OstInstallLocation = RomMbox.Models.PlatformMapping.OptionalContentLocation.Centralized,
                        BonusRootPath = "",
                        InstallBonus = false,
                        BonusInstallLocation = RomMbox.Models.PlatformMapping.OptionalContentLocation.Centralized,
                        PreReqsRootPath = "",
                        InstallPreReqs = false,
                        CustomInstallDirectory = ""
                    });
                });
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Failed to load platform mappings.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }

    /// <summary>
    /// Ensures the mapping service is available based on current settings.
    /// </summary>
    private void EnsureMappingService()
    {
        try
        {
            var settings = _settingsManager.Load();
            var serverUrl = settings?.ServerUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.IsWellFormedUriString(serverUrl, UriKind.Absolute))
            {
                _mappingService = null;
                _mappingServiceServerUrl = string.Empty;
                return;
            }

            if (_mappingService != null && string.Equals(_mappingServiceServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var client = new RommClient(_logger, _settingsManager, requireServerUrl: true);
            _mappingService = new PlatformMappingService(_logger, _settingsManager, client);
            _mappingServiceServerUrl = serverUrl;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize RomM client for platform mappings.", ex);
            _mappingService = null;
            _mappingServiceServerUrl = string.Empty;
        }
    }

    /// <summary>
    /// Persists platform mappings and exclusions to settings.
    /// </summary>
    public Task SaveMappingsSilentlyAsync()
    {
        return SaveMappingsAsync(showConfirmation: false);
    }

    private async Task SaveMappingsAsync(bool showConfirmation)
    {
        try
        {
            if (_mappingService == null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Cannot save mappings - server is not configured. Please configure the server connection first.", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            var mappings = Mappings.ToList();
            LogPlatformMappingChanges(mappings);
            var unmapped = mappings
                .Where(mapping => mapping != null)
                .Where(mapping => !mapping.Exclude)
                .Where(mapping => string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatform) || mapping.LaunchBoxPlatform == UnmappedLabel)
                .Select(mapping => mapping.RomMPlatform)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

            if (unmapped.Count > 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var platformsText = string.Join(", ", unmapped);
                    MessageBox.Show($"The following platform(s) are unmapped: {platformsText}. Map them or exclude before saving.", "RomM", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }

            var excluded = mappings
                .Where(mapping => mapping.Exclude)
                .Select(mapping => mapping.RommPlatformId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var platformMappings = mappings
                .Where(mapping => !mapping.Exclude)
                .Select(mapping =>
                {
                    var saved = _mappingService.GetMapping(mapping.RommPlatformId);
                    return new RomMbox.Models.PlatformMapping.PlatformMapping
                    {
                        RommPlatformId = mapping.RommPlatformId,
                        RommPlatformName = mapping.RomMPlatform,
                        LaunchBoxPlatformName = mapping.LaunchBoxPlatform == UnmappedLabel ? string.Empty : mapping.LaunchBoxPlatform,
                        AutoMapped = false,
                        DisableAutoImport = mapping.DisableAutoImport,
                        ExtractAfterDownload = mapping.ExtractAfterDownload,
                        ExtractionBehavior = mapping.ExtractionBehavior,
                        InstallScenario = mapping.InstallScenario,
                        SelfContained = mapping.SelfContained,
                        TargetImportFile = mapping.TargetImportFile,
                        InstallerSilentArgs = mapping.InstallerSilentArgs,
                        InstallerMode = mapping.InstallerMode,
                        AssociatedEmulatorId = mapping.AssociatedEmulatorId,
                        MusicRootPath = mapping.MusicRootPath,
                        InstallOst = mapping.InstallOst,
                        OstInstallLocation = mapping.OstInstallLocation,
                        BonusRootPath = mapping.BonusRootPath,
                        InstallBonus = mapping.InstallBonus,
                        BonusInstallLocation = mapping.BonusInstallLocation,
                        PreReqsRootPath = mapping.PreReqsRootPath,
                        InstallPreReqs = mapping.InstallPreReqs,
                        CustomInstallDirectory = mapping.CustomInstallDirectory
                    };
                })
                .ToArray();

            _mappingService.SaveMappings(platformMappings);
            _mappingService.SaveExcludedRommPlatformIds(excluded);

            if (showConfirmation)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new RomMbox.UI.Views.InfoDialog("RomM", "Platform Mappings Set")
                    {
                        Owner = Application.Current?.Windows
                            ?.OfType<MainWindow>()
                            .FirstOrDefault(),
                        Topmost = true
                    };
                    dialog.Activate();
                    dialog.ShowDialog();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save platform mappings.", ex);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Failed to save platform mappings.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    /// <summary>
    /// Logs install-related settings during save for troubleshooting.
    /// </summary>
    /// <param name="mappings">The mappings being saved.</param>
    private void LogPlatformMappingChanges(IReadOnlyList<Models.PlatformMapping> mappings)
    {
        if (mappings == null || mappings.Count == 0)
        {
            return;
        }

        foreach (var mapping in mappings)
        {
            if (mapping == null)
            {
                continue;
            }

            var saved = _mappingService.GetMapping(mapping.RommPlatformId);
            var mappingName = mapping.RomMPlatform ?? string.Empty;
            var launchBoxName = mapping.LaunchBoxPlatform ?? string.Empty;
            var savedLaunchBox = saved?.LaunchBoxPlatformName ?? string.Empty;

            if (mapping.Exclude)
            {
                _logger?.Info($"Platform excluded: {mapping.RommPlatformId}/{mappingName}.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(launchBoxName)
                && !string.Equals(launchBoxName, UnmappedLabel, StringComparison.Ordinal)
                && !string.Equals(launchBoxName, savedLaunchBox, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info($"Platform mapping override: {mapping.RommPlatformId}/{mappingName} -> {launchBoxName}.");
            }

            if (string.IsNullOrWhiteSpace(mapping.CustomInstallDirectory))
            {
                continue;
            }

            var savedInstall = saved?.CustomInstallDirectory ?? string.Empty;
            if (!string.Equals(mapping.CustomInstallDirectory, savedInstall, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Info(
                    $"Platform custom install directory: {mapping.RommPlatformId}/{mappingName} -> '{mapping.CustomInstallDirectory}'.");
            }
        }
    }

    /// <summary>
    /// Loads LaunchBox platform names from defaults and the data manager.
    /// </summary>
    private static List<string> LoadLaunchBoxPlatforms()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in LoadLaunchBoxPlatformDefaults())
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                results.Add(name);
            }
        }

        try
        {
            var dataManager = PluginHelper.DataManager;
            if (dataManager != null)
            {
                foreach (var platform in dataManager.GetAllPlatforms())
                {
                    if (!string.IsNullOrWhiteSpace(platform?.Name))
                    {
                        results.Add(platform.Name);
                    }
                }
            }
        }
        catch
        {
        }

        return results.OrderBy(name => name).ToList();
    }

    /// <summary>
    /// Opens the platform install configuration dialog for a mapping.
    /// </summary>
    /// <param name="mapping">The mapping to configure.</param>
    private void OpenConfigureDialog(Models.PlatformMapping mapping)
    {
        if (mapping == null)
        {
            return;
        }

        if (_mappingService == null)
        {
            MessageBox.Show("Cannot configure platform - server is not configured. Please configure the server connection first.", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultInstallDirectory = ResolveDefaultInstallDirectory(mapping.LaunchBoxPlatform);
        var viewModel = new ViewModels.PlatformInstallConfigViewModel(
            mapping,
            defaultInstallDirectory,
            onSave: () => SaveInlineConfiguration(mapping),
            onBack: () => ExitInlineConfiguration());
        SelectedMapping = mapping;
        ActiveConfiguration = viewModel;
    }

    private void SaveInlineConfiguration(Models.PlatformMapping mapping)
    {
        if (ActiveConfiguration == null)
        {
            return;
        }

        var updated = ActiveConfiguration.BuildMappingForSave();
        if (updated != null)
        {
            var index = Mappings.IndexOf(mapping);
            if (index >= 0)
            {
                Mappings[index] = updated;
            }
        }

        ExitInlineConfiguration();
    }

    private void ExitInlineConfiguration()
    {
        ActiveConfiguration = null;
        SelectedMapping = null;
    }

    private static string ResolveGamesDirectoryForSave(string gamesDirectory)
    {
        if (string.IsNullOrWhiteSpace(gamesDirectory))
        {
            return string.Empty;
        }

        try
        {
            var root = RomMbox.Services.Paths.PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return gamesDirectory;
            }

            var normalizedRoot = System.IO.Path.GetFullPath(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                + System.IO.Path.DirectorySeparatorChar;
            var normalizedPath = System.IO.Path.GetFullPath(gamesDirectory.Trim());

            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedPath.Substring(normalizedRoot.Length);
                return relative.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
            }
        }
        catch
        {
        }

        return gamesDirectory;
    }

    /// <summary>
    /// Resolves the default install directory for a LaunchBox platform.
    /// </summary>
    /// <param name="launchBoxPlatform">The LaunchBox platform name.</param>
    /// <returns>The resolved install directory, or empty when unknown.</returns>
    private string ResolveDefaultInstallDirectory(string launchBoxPlatform)
    {
        if (string.IsNullOrWhiteSpace(launchBoxPlatform) || launchBoxPlatform == UnmappedLabel)
        {
            return string.Empty;
        }

        try
        {
            var dataManager = PluginHelper.DataManager;
            var platform = dataManager?.GetPlatformByName(launchBoxPlatform);
            if (platform != null && !string.IsNullOrWhiteSpace(platform.Folder))
            {
                return platform.Folder;
            }
        }
        catch
        {
        }

        var root = RomMbox.Services.Paths.PluginPaths.GetLaunchBoxRootDirectory();
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        return System.IO.Path.Combine(root, "Games", launchBoxPlatform);
    }

    /// <summary>
    /// Loads platform defaults from the shipped YAML mapping file.
    /// </summary>
    private static IEnumerable<string> LoadLaunchBoxPlatformDefaults()
    {
        var results = new List<string>();
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

            foreach (var rawLine in File.ReadAllLines(yamlPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                if (!rawLine.StartsWith(" ") && rawLine.TrimEnd().EndsWith(":", StringComparison.Ordinal))
                {
                    var name = rawLine.Trim().TrimEnd(':').Trim();
                    name = UnescapeYamlScalar(name);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        results.Add(name);
                    }
                }
            }
        }
        catch
        {
            return results;
        }

        return results;
    }

    /// <summary>
    /// Converts YAML scalar escapes to their literal characters.
    /// </summary>
    /// <param name="value">The raw YAML scalar.</param>
    /// <returns>The unescaped value.</returns>
    private static string UnescapeYamlScalar(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        if (!trimmed.Contains("\\u"))
        {
            return trimmed;
        }

        return Regex.Replace(trimmed, "\\\\u([0-9a-fA-F]{4})", match =>
        {
            var hex = match.Groups[1].Value;
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code))
            {
                return ((char)code).ToString();
            }
            return match.Value;
        });
    }
}
