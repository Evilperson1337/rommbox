using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model for global plugin settings and download defaults.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly LoggingService _logger;
    private readonly SettingsManager _settingsManager;
    private readonly PlatformMappingService _mappingService;

    /// <summary>
    /// Creates the settings view model and loads initial values.
    /// </summary>
    public SettingsViewModel()
    {
        _logger = new LoggingService(LogLevel.Debug, FileLogSink.CreateDefault());
        _settingsManager = new SettingsManager(_logger);
        var client = new RommClient(_logger, _settingsManager, requireServerUrl: false);
        _mappingService = new PlatformMappingService(_logger, _settingsManager, client);

        PlatformRows = new ObservableCollection<DownloadSettingsRow>();
        BehaviorOptions = Enum.GetValues(typeof(ExtractionBehavior)).Cast<ExtractionBehavior>().ToList();
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        SaveCommand = new RelayCommand(Save);
        BrowseSevenZipCommand = new RelayCommand(BrowseSevenZipPath);

        _ = LoadAsync();
    }

    /// <summary>
    /// Gets the per-platform download settings rows.
    /// </summary>
    public ObservableCollection<DownloadSettingsRow> PlatformRows { get; }
    /// <summary>
    /// Gets the available extraction behavior options.
    /// </summary>
    public IReadOnlyList<ExtractionBehavior> BehaviorOptions { get; }
    /// <summary>
    /// Command that refreshes the settings data.
    /// </summary>
    public RelayCommand RefreshCommand { get; }
    /// <summary>
    /// Command that saves the current settings.
    /// </summary>
    public RelayCommand SaveCommand { get; }
    /// <summary>
    /// Command that opens a file picker for the 7-Zip executable.
    /// </summary>
    public RelayCommand BrowseSevenZipCommand { get; }

    private string _statusText = "";
    /// <summary>
    /// Gets the status text displayed in the UI.
    /// </summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private string _sevenZipPath = "";
    /// <summary>
    /// Gets or sets the configured 7-Zip executable path.
    /// </summary>
    public string SevenZipPath { get => _sevenZipPath; set => SetProperty(ref _sevenZipPath, value); }

    private bool _useSevenZipFallback = true;
    /// <summary>
    /// Gets or sets whether the 7-Zip fallback should be used.
    /// </summary>
    public bool UseSevenZipFallback { get => _useSevenZipFallback; set => SetProperty(ref _useSevenZipFallback, value); }

    private bool _keepArchivesAfterExtraction = true;
    /// <summary>
    /// Gets whether archives are kept after extraction.
    /// </summary>
    public bool KeepArchivesAfterExtraction { get => _keepArchivesAfterExtraction; private set => SetProperty(ref _keepArchivesAfterExtraction, value); }

    /// <summary>
    /// Loads platform mappings and settings from storage.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            StatusText = "Loading platform mappings...";
            var result = await _mappingService.DiscoverPlatformsAsync(CancellationToken.None).ConfigureAwait(false);
            
            // Check if we got results due to missing configuration
            if (result.Mappings == null || result.Mappings.Count == 0)
            {
                StatusText = "Server not configured. Please configure connection in the Connection tab first.";
                return;
            }
            var excluded = _mappingService.GetExcludedRommPlatformIds() ?? Array.Empty<string>();
            var rows = result.Mappings
                .Where(mapping => !excluded.Contains(mapping.RommPlatformId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(mapping => mapping.RommPlatformName)
                .Select(mapping => new DownloadSettingsRow
                {
                    RommPlatformId = mapping.RommPlatformId,
                    RommPlatformName = mapping.RommPlatformName,
                    LaunchBoxPlatformName = mapping.LaunchBoxPlatformName,
                    AutoMapped = mapping.AutoMapped,
                    ExtractAfterDownload = mapping.ExtractAfterDownload,
                    ExtractionBehavior = mapping.ExtractionBehavior
                })
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlatformRows.Clear();
                foreach (var row in rows)
                {
                    PlatformRows.Add(row);
                }

                var settings = _settingsManager.Load();
                SevenZipPath = settings.GetSevenZipPath();
                UseSevenZipFallback = settings.GetUseSevenZipFallback();
                KeepArchivesAfterExtraction = true;

                StatusText = PlatformRows.Count == 0
                    ? "No mapped platforms available."
                    : $"Loaded {PlatformRows.Count} platform(s).";
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load settings.", ex);
            
            // Check if this is a missing credentials error
            if (ex.Message.Contains("Missing credentials") || ex.Message.Contains("Server URL is not configured"))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = "Server not configured. Please configure server connection first.";
                    PlatformRows.Clear();
                });
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = "Failed to load platform mappings.";
                    MessageBox.Show("Failed to load settings.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }

    /// <summary>
    /// Saves platform mappings and general settings to storage.
    /// </summary>
    public void Save()
    {
        try
        {
            var platformMappings = PlatformRows.Select(row => new RomMbox.Models.PlatformMapping.PlatformMapping
            {
                RommPlatformId = row.RommPlatformId,
                RommPlatformName = row.RommPlatformName,
                LaunchBoxPlatformName = row.LaunchBoxPlatformName,
                AutoMapped = row.AutoMapped,
                ExtractAfterDownload = row.ExtractAfterDownload,
                ExtractionBehavior = row.ExtractionBehavior
            }).ToArray();

            _mappingService.SaveMappings(platformMappings);

            var settings = _settingsManager.Load();
            settings.SevenZipPath = SevenZipPath ?? string.Empty;
            settings.UseSevenZipFallback = UseSevenZipFallback;
            settings.KeepArchivesAfterExtraction = true;
            _settingsManager.Save(settings);
            KeepArchivesAfterExtraction = true;
            StatusText = "Settings saved.";
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings.", ex);
            StatusText = "Failed to save settings.";
            MessageBox.Show("Failed to save settings.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens a file dialog to select the 7-Zip executable path.
    /// </summary>
    private void BrowseSevenZipPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select 7z.exe",
            Filter = "7-Zip (7z.exe)|7z.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        var currentPath = SevenZipPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(currentPath);
                dialog.FileName = System.IO.Path.GetFileName(currentPath);
            }
            catch
            {
            }
        }

        if (dialog.ShowDialog() == true)
        {
            SevenZipPath = dialog.FileName;
        }
    }
}
