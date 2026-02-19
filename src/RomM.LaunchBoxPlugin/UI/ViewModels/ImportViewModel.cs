using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using RomMbox.Models.Import;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;
using RomMbox.UI.Views;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View-model for the Import screen. It loads RomM platforms, lists ROMs,
/// and orchestrates import actions (import/install/merge/skip) for the UI.
/// </summary>
public sealed class ImportViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly LoggingService _logger;
    private readonly SettingsManager _settingsManager;
    private PlatformMappingService _mappingService;
    private string _mappingServiceServerUrl = string.Empty;
    private ImportService _importService;
    private readonly List<ImportGameRow> _filteredRowSnapshot;
    private CancellationTokenSource _cts;
    private readonly MatchIgnoreStore _ignoreStore;
    private string _currentOperationId = string.Empty;
    private bool _hasLoaded;

    /// <summary>
    /// Builds the import screen state, initializes services, and wires up commands.
    /// </summary>
    /// <param name="shell">Shared shell view-model for cross-tab state.</param>
    public ImportViewModel(MainWindowViewModel shell)
    {
        _shell = shell;

        // Core services for logging and settings (server URL, mappings, etc.).
        _logger = LoggingServiceFactory.Create();
        _settingsManager = new SettingsManager(_logger);
        
        RommClient client = null;
        try
        {
            // We allow a null server URL here so the UI can still load and show
            // a helpful message when the server is not configured yet.
            client = new RommClient(_logger, _settingsManager, requireServerUrl: false);
        }
        catch
        {
            // Server not configured, client will be null
        }
        
        // Only initialize network-backed services if we have a valid client.
        _mappingService = client != null ? new PlatformMappingService(_logger, _settingsManager, client) : null;
        _mappingServiceServerUrl = _settingsManager.Load()?.ServerUrl?.Trim() ?? string.Empty;
        _importService = client != null ? new ImportService(_logger, _settingsManager, _mappingService, client) : null;
        _ignoreStore = new MatchIgnoreStore(_logger);

        // UI collections bound to lists, dropdowns, and grids.
        RomMPlatforms = new ObservableCollection<RommPlatformOption>();
        Games = new ObservableCollection<ImportGameRow>();
        FilteredGames = new ObservableCollection<ImportGameRow>();
        DuplicateMatchOptions = new ObservableCollection<DuplicateMatchOption>
        {
            DuplicateMatchOption.GameName,
            DuplicateMatchOption.FileName,
            DuplicateMatchOption.Md5
        };
        ImportActions = new ObservableCollection<ImportAction>
        {
            ImportAction.Import,
            ImportAction.Install,
            ImportAction.Merge,
            ImportAction.Skip
        };
        _filteredRowSnapshot = new List<ImportGameRow>();

        // Commands used by buttons in the UI.
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsImportRunning);
        ImportCommand = new RelayCommand(async () => await ImportSelectedAsync(), () => !IsImportRunning);

        StatusText = "Ready to import games from RomM";
        ProgressValue = 0;

        UpdateHeaderText();

        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedPlatform))
            {
                UpdateHeaderText();
            }
        };

        IsImportRunning = false;
        BulkAction = ImportAction.Import;
        AllowDuplicates = false;
        // Lazy-load when the tab is activated.
    }

    public ObservableCollection<RommPlatformOption> RomMPlatforms { get; private set; }
    public ObservableCollection<ImportGameRow> Games { get; }
    public ObservableCollection<ImportGameRow> FilteredGames { get; }
    public ObservableCollection<DuplicateMatchOption> DuplicateMatchOptions { get; }
    public ObservableCollection<ImportAction> ImportActions { get; }

    private ImportAction? _bulkAction;
    public ImportAction? BulkAction
    {
        get => _bulkAction;
        set
        {
            if (SetProperty(ref _bulkAction, value))
            {
                ApplyBulkAction(value);
            }
        }
    }

    private string _headerTitle = "Import Games";
    public string HeaderTitle { get => _headerTitle; private set => SetProperty(ref _headerTitle, value); }

    private string _helperText = "Select the games you want to import from the RomM server.";
    public string HelperText { get => _helperText; private set => SetProperty(ref _helperText, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

    private bool _isImportRunning;
    public bool IsImportRunning
    {
        get => _isImportRunning;
        private set
        {
            if (SetProperty(ref _isImportRunning, value))
            {
                if (value)
                {
                    ImportStatusText = "Loading games...";
                }
                RefreshCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private RommPlatformOption _selectedRomMPlatform;
    public RommPlatformOption SelectedRomMPlatform
    {
        get => _selectedRomMPlatform;
        set
        {
            if (SetProperty(ref _selectedRomMPlatform, value))
            {
                _logger.Debug($"SelectedRomMPlatform changed: {(value == null ? "<null>" : value.Name)}");
                UpdateHeaderText();
                _shell.SelectedPlatform = value?.LaunchBoxPlatformName ?? value?.Name ?? _shell.SelectedPlatform;
                _ = RefreshAsync();
            }
        }
    }

    private string _importStatusText = "";
    public string ImportStatusText { get => _importStatusText; private set => SetProperty(ref _importStatusText, value); }

    private bool _allowDuplicates;
    public bool AllowDuplicates { get => _allowDuplicates; set => SetProperty(ref _allowDuplicates, value); }

    private DuplicateMatchOption _selectedDuplicateMatchOption = DuplicateMatchOption.GameName;
    public DuplicateMatchOption SelectedDuplicateMatchOption
    {
        get => _selectedDuplicateMatchOption;
        set
        {
            if (SetProperty(ref _selectedDuplicateMatchOption, value))
            {
                _logger.Debug($"Duplicate match option updated to '{value}', will be applied during import only.");
            }
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplySearchFilter();
            }
        }
    }

    private bool _hideSkipped = true;
    public bool HideSkipped
    {
        get => _hideSkipped;
        set
        {
            if (SetProperty(ref _hideSkipped, value))
            {
                ApplySearchFilter();
            }
        }
    }

    private double _importProgressPercent;
    public double ImportProgressPercent { get => _importProgressPercent; private set => SetProperty(ref _importProgressPercent, value); }

    private bool _syncingHeaderState;

    private bool? _importAll;
    public bool? ImportAll
    {
        get => _importAll;
        set
        {
            if (_syncingHeaderState)
            {
                SetProperty(ref _importAll, value);
                return;
            }

            if (SetProperty(ref _importAll, value))
            {
                ApplyHeaderToggle(value, row => row.Import = value == true);
            }
        }
    }

    private bool? _downloadAll;
    public bool? DownloadAll
    {
        get => _downloadAll;
        set
        {
            if (_syncingHeaderState)
            {
                SetProperty(ref _downloadAll, value);
                return;
            }

            if (SetProperty(ref _downloadAll, value))
            {
                ApplyHeaderToggle(value, row => row.Download = value == true);
            }
        }
    }

    private bool? _savesAll;
    public bool? SavesAll
    {
        get => _savesAll;
        set
        {
            if (_syncingHeaderState)
            {
                SetProperty(ref _savesAll, value);
                return;
            }

            if (SetProperty(ref _savesAll, value))
            {
                ApplyHeaderToggle(value, row => row.Saves = value == true);
            }
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ImportCommand { get; }

    /// <summary>
    /// Refreshes the platform list from the server, used by the shell when
    /// settings or mappings change.
    /// </summary>
    public Task ReloadPlatformsAsync()
    {
        return LoadPlatformsAsync();
    }

    /// <summary>
    /// Loads platforms once when the tab is first activated.
    /// </summary>
    public Task EnsureLoadedAsync()
    {
        if (_hasLoaded)
        {
            return Task.CompletedTask;
        }

        return LoadPlatformsAsync();
    }

    /// <summary>
    /// Loads the list of RomM platforms and maps them to LaunchBox platforms.
    /// This method always updates ObservableCollections on the UI thread.
    /// </summary>
    private async Task LoadPlatformsAsync()
    {
        try
        {
            _hasLoaded = true;
            EnsureMappingService();
            if (_mappingService == null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RomMPlatforms.Clear();
                    Games.Clear();
                    FilteredGames.Clear();
                    UpdateHeaderToggles();
                    ImportStatusText = "Server not configured. Please configure server connection first.";
                });
                return;
            }

            if (_importService == null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ImportStatusText = "Server connection is initializing. Please refresh in a moment.";
                });
                return;
            }

            // Fetch mappings from RomM, then filter out excluded/disabled entries.
            var result = await _mappingService.DiscoverPlatformsAsync(CancellationToken.None).ConfigureAwait(false);
            var excluded = _mappingService.GetExcludedRommPlatformIds() ?? Array.Empty<string>();
            var options = result.Mappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.RommPlatformId))
                .Where(mapping => !excluded.Contains(mapping.RommPlatformId, StringComparer.OrdinalIgnoreCase))
                .Where(mapping => !mapping.DisableAutoImport)
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatformName))
                .Select(mapping => new RommPlatformOption
                {
                    Id = mapping.RommPlatformId,
                    Name = string.IsNullOrWhiteSpace(mapping.RommPlatformName) ? mapping.RommPlatformId : mapping.RommPlatformName,
                    LaunchBoxPlatformName = mapping.LaunchBoxPlatformName
                })
                .OrderBy(option => option.Name)
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Update collections on the UI thread so WPF bindings stay safe.
                RomMPlatforms.Clear();
                foreach (var option in options)
                {
                    RomMPlatforms.Add(option);
                }

                SelectedRomMPlatform = null;
                Games.Clear();
                FilteredGames.Clear();
                UpdateHeaderToggles();
                ImportStatusText = RomMPlatforms.Count == 0
                    ? "No mapped platforms available."
                    : "Select a platform and click Refresh.";
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load RomM platforms.", ex);
            
            // Check if this is a missing credentials error
            if (ex.Message.Contains("Missing credentials") || ex.Message.Contains("Server URL is not configured"))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RomMPlatforms.Clear();
                    Games.Clear();
                    FilteredGames.Clear();
                    UpdateHeaderToggles();
                    ImportStatusText = "Server not configured. Please configure server connection first.";
                });
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Failed to load RomM platforms.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }

    private void EnsureMappingService()
    {
        try
        {
            var settings = _settingsManager.Load();
            var serverUrl = settings?.ServerUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.IsWellFormedUriString(serverUrl, UriKind.Absolute))
            {
                // No valid server URL means we cannot create a RomM client.
                _mappingService = null;
                _mappingServiceServerUrl = string.Empty;
                _importService = null;
                return;
            }

            if (_mappingService != null && string.Equals(_mappingServiceServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (_importService == null)
                {
                    var existingClient = new RommClient(_logger, _settingsManager, requireServerUrl: true);
                    _importService = new ImportService(_logger, _settingsManager, _mappingService, existingClient);
                }
                return;
            }

            // Recreate the client whenever the server URL changes.
            var client = new RommClient(_logger, _settingsManager, requireServerUrl: true);
            _mappingService = new PlatformMappingService(_logger, _settingsManager, client);
            _mappingServiceServerUrl = serverUrl;
            _importService = new ImportService(_logger, _settingsManager, _mappingService, client);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize RomM client for games list.", ex);
            _mappingService = null;
            _mappingServiceServerUrl = string.Empty;
            _importService = null;
        }
    }

        /// <summary>
        /// Fetches the ROM list for the selected platform and rebuilds the grid.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (IsImportRunning || SelectedRomMPlatform == null)
            {
                return;
            }

            _currentOperationId = Guid.NewGuid().ToString("N");
            using (_logger.BeginOperation(_currentOperationId))
            {
                _logger.Write(LogLevel.Info, "Import refresh started.", null,
                    "Subsystem", "UI",
                    "Operation", "RefreshGames",
                    "PlatformId", SelectedRomMPlatform?.Id ?? string.Empty);
            }

            _logger.Info($"User selected Platform: \"{SelectedRomMPlatform.Name}\".");

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsImportRunning = true;
                ImportStatusText = "Loading games...";
                ImportProgressPercent = 0;
            });
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var platformId = SelectedRomMPlatform.Id;
                // Progress is reported from a background thread; marshal to UI.
                var progress = new Progress<ImportProgress>(progressUpdate =>
                {
                    var total = progressUpdate.Total;
                    var processed = progressUpdate.Processed;
                    var percent = total > 0 ? (double)processed / total * 100d : 0d;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ImportProgressPercent = Math.Clamp(percent, 0, 100);
                        ImportStatusText = total > 0
                            ? $"Loading games... {processed} of {total}"
                            : $"Loading games... {processed}";
                    });
                });

                var roms = await _importService.ListPlatformRomsAsync(platformId, _cts.Token, progress).ConfigureAwait(false);
                // Project RomM API models into rows the grid can bind to.
                var rows = roms
                    .Select(rom => new ImportGameRow
                    {
                        Rom = rom,
                        Name = rom?.DisplayTitle ?? rom?.Title ?? string.Empty,
                    Import = false,
                    Download = false,
                    Saves = false,
                    Action = ImportAction.Import,
                    SkipReason = string.Empty,
                    StatusText = string.Empty
                })
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _isRefreshingRows = true;
                try
                {
                    SearchText = string.Empty;
                    Games.Clear();
                    foreach (var row in rows)
                    {
                        Games.Add(row);
                    }

                    WireGameRowHandlers();
                    EvaluateDuplicateMatchesForRows();
                    UpdateHeaderToggles();
                    ApplySearchFilter();
                    ImportStatusText = $"Loaded {Games.Count} game(s).";
                    ImportProgressPercent = 0;
                    _importCompletedSinceLastChange = false;
                }
                finally
                {
                    _isRefreshingRows = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            using (_logger.BeginOperation(_currentOperationId))
            {
                _logger.Write(LogLevel.Error, "Failed to load games.", ex,
                    "Subsystem", "UI",
                    "Operation", "RefreshGames",
                    "PlatformId", SelectedRomMPlatform?.Id ?? string.Empty);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Failed to load games from RomM.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                ImportStatusText = "Failed to load games.";
            });
        }
        finally
        {
            using (_logger.BeginOperation(_currentOperationId))
            {
                var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Subsystem"] = "UI",
                    ["Operation"] = "RefreshGames",
                    ["PlatformId"] = SelectedRomMPlatform?.Id ?? string.Empty,
                    ["Result"] = "Success"
                };
                _logger.Write(LogLevel.Info, "Import refresh completed.", null, props);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsImportRunning = false;
            });
        }
    }

    /// <summary>
    /// Runs import if there are pending changes. Used by the shell before closing.
    /// </summary>
    public async Task<bool> RunImportAndWaitAsync()
    {
        if (_importCompletedSinceLastChange)
        {
            _logger.Debug("Import already completed with no changes since last run. Skipping re-import.");
            return true;
        }

        return await ImportSelectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Imports the currently selected ROMs, updating progress and showing results.
    /// </summary>
    private async Task<bool> ImportSelectedAsync()
    {
        if (IsImportRunning || SelectedRomMPlatform == null)
        {
            _logger.Debug($"ImportSelected blocked. IsImportRunning={IsImportRunning}, SelectedRomMPlatform={(SelectedRomMPlatform == null ? "<null>" : SelectedRomMPlatform.Name)}");
            return false;
        }

        _logger.Debug($"ImportSelected invoked. Games count={Games.Count}.");
        _currentOperationId = Guid.NewGuid().ToString("N");
        using (_logger.BeginOperation(_currentOperationId))
        {
            _logger.Write(LogLevel.Info, "Import selected started.", null,
                "Subsystem", "UI",
                "Operation", "ImportSelected",
                "PlatformId", SelectedRomMPlatform?.Id ?? string.Empty);
        }
        var launchBoxPlatformName = SelectedRomMPlatform?.LaunchBoxPlatformName ?? _shell.SelectedPlatform;
        if (!HasEmulatorAssigned(launchBoxPlatformName))
        {
            _logger.Info($"Import proceeding without emulator configured for platform '{launchBoxPlatformName ?? "Unknown"}'. Install option will be unavailable.");
        }
        
        // Convert grid rows into a compact selection payload for the service.
        var selections = BuildSelections();
        _logger.Debug($"ImportSelected selections count={selections.Count}.");
        if (selections.Count == 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Select at least one game to import.", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            return false;
        }

        var mergeSelections = selections.Where(selection => selection.Action == ImportAction.Merge).ToList();
        var importSelections = selections.Where(selection => selection.Action != ImportAction.Merge).ToList();

        if (importSelections.Count == 0 && mergeSelections.Count > 0)
        {
            ApplyMergeSelections(mergeSelections);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ImportStatusText = "Merge complete.";
                ImportProgressPercent = 100;
            });
            _importCompletedSinceLastChange = true;
            return true;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsImportRunning = true;
            ImportStatusText = "Importing selected games...";
            ImportProgressPercent = 0;
        });
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        // Report progress back to the UI as the service processes ROMs.
        var progress = new Progress<ImportProgress>(progressUpdate =>
        {
            var total = progressUpdate.Total;
            var processed = progressUpdate.Processed;
            var percent = total > 0 ? (double)processed / total * 100d : 0d;
            Application.Current.Dispatcher.Invoke(() =>
            {
                ImportProgressPercent = Math.Clamp(percent, 0, 100);
                ImportStatusText = $"Imported {progressUpdate.Successful} of {total} (Skipped: {progressUpdate.Skipped}, Failed: {progressUpdate.Failed})";
            });
        });

        try
        {
            _logger.Debug("Starting ImportSelectedRomsAsync.");
            var result = await _importService.ImportSelectedRomsAsync(
                SelectedRomMPlatform.Id,
                importSelections,
                allowDuplicates: AllowDuplicates,
                matchByRomId: true,
                matchByMd5: SelectedDuplicateMatchOption == DuplicateMatchOption.Md5,
                matchByTitle: SelectedDuplicateMatchOption == DuplicateMatchOption.GameName,
                duplicateMatchOption: SelectedDuplicateMatchOption,
                cancellationToken: _cts.Token,
                progress: progress).ConfigureAwait(false);

            _logger.Debug($"ImportSelectedRomsAsync completed. Imported={result.SuccessfulImports}, Skipped={result.SkippedDuplicates}, Failed={result.FailedImports}.");

            if (mergeSelections.Count > 0)
            {
                ApplyMergeSelections(mergeSelections);
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ImportStatusText = $"Import complete: {result.SuccessfulImports} imported, {result.SkippedDuplicates} skipped, {result.FailedImports} failed.";
                ImportProgressPercent = 100;
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (result.ReportItems.Count == 0)
                {
                    return;
                }

                var reportViewModel = new ImportReportViewModel(result.ReportItems);
                var dialog = new ImportReportDialog
                {
                    DataContext = reportViewModel,
                    Owner = Application.Current?.Windows
                        ?.OfType<MainWindow>()
                        .FirstOrDefault()
                };
                reportViewModel.RequestClose += () => dialog.Close();
                dialog.ShowDialog();
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (result.MatchCandidates.Count > 0)
                {
                    _logger.Info($"Match candidates detected ({result.MatchCandidates.Count}), but review dialog is disabled.");
                }
            });
            _importCompletedSinceLastChange = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            _importCompletedSinceLastChange = false;
            return false;
        }
        catch (Exception ex)
        {
            using (_logger.BeginOperation(_currentOperationId))
            {
                _logger.Write(LogLevel.Error, "Failed to import games.", ex,
                    "Subsystem", "UI",
                    "Operation", "ImportSelected",
                    "PlatformId", SelectedRomMPlatform?.Id ?? string.Empty);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Failed to import selected games.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                ImportStatusText = "Import failed.";
            });
            _importCompletedSinceLastChange = false;
            return false;
        }
        finally
        {
            using (_logger.BeginOperation(_currentOperationId))
            {
                var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Subsystem"] = "UI",
                    ["Operation"] = "ImportSelected",
                    ["PlatformId"] = SelectedRomMPlatform?.Id ?? string.Empty,
                    ["Result"] = _importCompletedSinceLastChange ? "Success" : "Failure"
                };
                _logger.Write(LogLevel.Info, "Import selected completed.", null, props);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsImportRunning = false;
            });
        }
    }

    /// <summary>
    /// Optional review step where a user can confirm or reject suggested matches.
    /// Currently not invoked, but kept for future UI enablement.
    /// </summary>
    private void ShowMatchReviewDialog(IReadOnlyList<RommMatchCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return;
        }

        var viewModel = new MatchReviewViewModel();
        foreach (var candidate in candidates)
        {
            var row = new MatchReviewRow
            {
                PlatformId = candidate.PlatformId,
                RommId = candidate.RommId,
                RommTitle = candidate.RommTitle,
                LaunchBoxGameId = candidate.LaunchBoxGameId,
                LaunchBoxTitle = candidate.LaunchBoxTitle,
                Strategy = candidate.Strategy,
                Confidence = candidate.Confidence
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MatchReviewRow.Decision))
                {
                    _logger.Debug($"Match review decision changed. RommId={row.RommId ?? "<null>"}, LaunchBoxId={row.LaunchBoxGameId ?? "<null>"}, Decision={(row.Decision.HasValue ? row.Decision.ToString() : "<null>")}");
                    viewModel.ApplyCommand.RaiseCanExecuteChanged();
                }
            };
            viewModel.Matches.Add(row);
        }

        viewModel.ApplyCommand.RaiseCanExecuteChanged();

        var dialog = new MatchReviewDialog
        {
            Owner = Application.Current?.Windows
                ?.OfType<MainWindow>()
                .FirstOrDefault(),
            DataContext = viewModel
        };

        viewModel.RequestClose += accepted =>
        {
            if (accepted)
            {
                _logger.Info("Match review accepted. Applying decisions.");
                ApplyMatchReviewDecisions(viewModel.Matches);
                dialog.DialogResult = true;
            }
            else
            {
                _logger.Info("Match review cancelled.");
                dialog.DialogResult = false;
            }
            dialog.Close();
        };

        dialog.ShowDialog();
    }

    /// <summary>
    /// Applies decisions from the match review dialog into LaunchBox metadata.
    /// </summary>
    private void ApplyMatchReviewDecisions(IEnumerable<MatchReviewRow> rows)
    {
        if (rows == null)
        {
            return;
        }

        var dataManager = PluginHelper.DataManager;
        if (dataManager == null)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (row == null)
            {
                continue;
            }

            _logger.Debug($"Applying match review decision. RommId={row.RommId ?? "<null>"}, LaunchBoxId={row.LaunchBoxGameId ?? "<null>"}, Decision={(row.Decision.HasValue ? row.Decision.ToString() : "<null>")}");

            if (row.Decision == false)
            {
                _logger.Info($"Match review rejected. Adding ignore for RommId={row.RommId ?? "<null>"}, LaunchBoxId={row.LaunchBoxGameId ?? "<null>"}.");
                _ignoreStore.AddIgnore(row.PlatformId, row.RommId, row.LaunchBoxGameId);
                continue;
            }

            if (row.Decision != true)
            {
                continue;
            }

            var game = dataManager.GetGameById(row.LaunchBoxGameId);
            if (game == null)
            {
                _logger.Warning($"Match review apply skipped: LaunchBox game not found. LaunchBoxId={row.LaunchBoxGameId ?? "<null>"}.");
                continue;
            }

            try
            {
                var rom = _importService.ResolveRomDetailsForReview(row.RommId, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (rom == null)
                {
                    _logger.Warning($"Match review apply skipped: RomM details not found. RommId={row.RommId ?? "<null>"}.");
                    continue;
                }

                _logger.Debug($"Applying match review: Strategy={row.Strategy ?? "<null>"}, Confidence={row.Confidence ?? "<null>"}.");
                _importService.ApplyMatchForReview(game, rom, row.Strategy, row.Confidence);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to apply match review for RomM {row.RommId}: {ex.Message}");
            }
        }

        Task.Run(() =>
        {
            dataManager.Save(true);
            dataManager.ReloadIfNeeded();
            dataManager.ForceReload();
        });
    }

    /// <summary>
    /// Applies merge tags for selections that should be linked to existing games.
    /// </summary>
    private void ApplyMergeSelections(IReadOnlyList<RomImportSelection> selections)
    {
        if (selections == null || selections.Count == 0)
        {
            return;
        }

        var dataManager = PluginHelper.DataManager;
        if (dataManager == null)
        {
            _logger.Warning("Merge selections skipped: DataManager unavailable.");
            return;
        }

        var platformName = SelectedRomMPlatform?.LaunchBoxPlatformName ?? _shell.SelectedPlatform;
        var platform = !string.IsNullOrWhiteSpace(platformName)
            ? dataManager.GetPlatformByName(platformName)
            : null;
        if (platform == null)
        {
            _logger.Warning($"Merge selections skipped: LaunchBox platform not found '{platformName ?? "<null>"}'.");
            return;
        }

        var matchByMd5 = SelectedDuplicateMatchOption == DuplicateMatchOption.Md5;
        var matchByTitle = SelectedDuplicateMatchOption == DuplicateMatchOption.GameName;
        var matchByFileName = SelectedDuplicateMatchOption == DuplicateMatchOption.FileName;

        foreach (var selection in selections)
        {
            var rom = selection?.Rom;
            if (rom == null)
            {
                continue;
            }

            var game = _importService.FindExistingGameForUi(dataManager, platform, rom, matchByMd5, matchByTitle, matchByFileName);
            if (game == null)
            {
                _logger.Warning($"Merge selection skipped: no local match found for RomM {rom.Id ?? "<null>"}.");
                continue;
            }

            _logger.Info($"Applying merge tag for RomM {rom.Id ?? "<null>"} -> LaunchBox {game.Title} ({game.Id}).");
            _importService.ApplyMergeTag(game, rom);
        }

        Task.Run(() =>
        {
            dataManager.Save(true);
            dataManager.ReloadIfNeeded();
            dataManager.ForceReload();
        });
    }

    /// <summary>
    /// Builds a list of selections from the grid rows, honoring filters and actions.
    /// </summary>
    private List<RomImportSelection> BuildSelections()
    {
        var selections = new List<RomImportSelection>();
        var rows = !string.IsNullOrWhiteSpace(SearchText) ? FilteredGames : Games;
        foreach (var row in rows)
        {
            if (row?.Rom == null)
            {
                if (row == null)
                {
                    _logger.Debug("Skipping null row.");
                }
                else
                {
                    _logger.Debug($"Skipping row '{row.Name}': Rom={(row.Rom == null ? "<null>" : row.Rom.Id)}");
                }
                continue;
            }

            if (row.Action == ImportAction.Skip)
            {
                _logger.Debug($"Skipping row '{row.Name}': Action=Skip.");
                continue;
            }

            _logger.Debug($"Selected row '{row.Name}' for import. Action={row.Action}, Download={row.Download}");
            selections.Add(new RomImportSelection(row.Rom, row.Download, downloadSaves: row.Saves, row.Action));
        }

        return selections;
    }

    /// <summary>
    /// Updates the header title and helper text to reflect the active platform.
    /// </summary>
    private void UpdateHeaderText()
    {
        var platformName = SelectedRomMPlatform?.Name;
        var p = string.IsNullOrWhiteSpace(platformName) ? _shell.SelectedPlatform : platformName;
        if (string.IsNullOrWhiteSpace(p))
        {
            p = "Games";
        }

        HeaderTitle = $"Import {p} Games";
        HelperText = $"Select the {p} games you want to import from the RomM server.";
    }

    /// <summary>
    /// Filters the grid rows based on search text and the HideSkipped toggle.
    /// </summary>
    private void ApplySearchFilter()
    {
        var search = (_searchText ?? string.Empty).Trim();
        FilteredGames.Clear();
        _filteredRowSnapshot.Clear();

        foreach (var row in Games)
        {
            if (row == null)
            {
                continue;
            }

            if (HideSkipped && row.Action == ImportAction.Skip)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(search)
                || (row.Name != null && row.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                FilteredGames.Add(row);
                _filteredRowSnapshot.Add(row);
            }
        }
    }

    private bool _isUpdatingHeaders;
    private bool _isUpdatingActions;
    private bool _isApplyingBulkAction;
    private bool _isRefreshingRows;
    private bool _importCompletedSinceLastChange;

    /// <summary>
    /// Applies a header checkbox state (all/none) to row-level flags.
    /// </summary>
    private void ApplyHeaderToggle(bool? value, Action<ImportGameRow> apply)
    {
        if (value == null)
        {
            return;
        }

        _logger.Debug($"Header toggle applied. Value={value}, Rows={Games.Count}.");
        _isUpdatingHeaders = true;
        var rows = !string.IsNullOrWhiteSpace(SearchText) ? FilteredGames : Games;
        foreach (var row in rows)
        {
            apply(row);
        }
        _isUpdatingHeaders = false;

        UpdateHeaderToggles();
    }

    /// <summary>
    /// Recomputes the header checkbox states (true/false/indeterminate).
    /// </summary>
    private void UpdateHeaderToggles()
    {
        _syncingHeaderState = true;
        ImportAll = CalculateAggregateState(row => row.Import);
        DownloadAll = CalculateAggregateState(row => row.Download);
        SavesAll = CalculateAggregateState(row => row.Saves);
        var totalRows = Games?.Count ?? 0;
        var visibleRows = !string.IsNullOrWhiteSpace(SearchText) ? FilteredGames?.Count ?? 0 : totalRows;
        var importCount = Games?.Count(row => row?.Import == true) ?? 0;
        var downloadCount = Games?.Count(row => row?.Download == true) ?? 0;
        var savesCount = Games?.Count(row => row?.Saves == true) ?? 0;
        var search = (SearchText ?? string.Empty).Trim();
        _logger.Write(
            LogLevel.Debug,
            "Header state updated.",
            null,
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ImportAll"] = ImportAll,
                ["DownloadAll"] = DownloadAll,
                ["SavesAll"] = SavesAll,
                ["TotalRows"] = totalRows,
                ["VisibleRows"] = visibleRows,
                ["ImportSelected"] = importCount,
                ["DownloadSelected"] = downloadCount,
                ["SavesSelected"] = savesCount,
                ["HideSkipped"] = HideSkipped,
                ["Search"] = search
            });
        _syncingHeaderState = false;
    }

    /// <summary>
    /// Returns true when any emulator is registered for a platform.
    /// Used to decide whether install actions should be offered.
    /// </summary>
    private static bool HasEmulatorAssigned(string platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        var dataManager = PluginHelper.DataManager;
        var emulators = dataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>();
        foreach (var emulator in emulators)
        {
            var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            foreach (var platform in platforms)
            {
                if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Subscribes to row property changes so the header states stay in sync.
    /// </summary>
    private void WireGameRowHandlers()
    {
        foreach (var row in Games)
        {
            if (row == null)
            {
                continue;
            }

            row.PropertyChanged -= OnGameRowPropertyChanged;
            row.PropertyChanged += OnGameRowPropertyChanged;
        }

        UpdateHeaderToggles();
    }

    /// <summary>
    /// Keeps row actions, flags, and header state synchronized when users edit the grid.
    /// </summary>
    private void OnGameRowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_isUpdatingHeaders || _isUpdatingActions)
        {
            return;
        }

        if (args.PropertyName == nameof(ImportGameRow.Import)
            || args.PropertyName == nameof(ImportGameRow.Download)
            || args.PropertyName == nameof(ImportGameRow.Saves))
        {
            SyncActionFromFlags((ImportGameRow)sender);
            UpdateHeaderToggles();
            _importCompletedSinceLastChange = false;
        }

        if (args.PropertyName == nameof(ImportGameRow.Action))
        {
            SyncFlagsFromAction((ImportGameRow)sender);
            UpdateHeaderToggles();
            _importCompletedSinceLastChange = false;
        }

        if (!_isApplyingBulkAction && !_isUpdatingActions && !_isRefreshingRows && (args.PropertyName == nameof(ImportGameRow.Action)
            || args.PropertyName == nameof(ImportGameRow.Import)
            || args.PropertyName == nameof(ImportGameRow.Download)
            || args.PropertyName == nameof(ImportGameRow.Saves)))
        {
            BulkAction = null;
        }
    }

    /// <summary>
    /// Computes a tri-state checkbox value for the header row.
    /// </summary>
    private bool? CalculateAggregateState(Func<ImportGameRow, bool> selector)
    {
        if (Games.Count == 0)
        {
            return false;
        }

        IEnumerable<ImportGameRow> rows = !string.IsNullOrWhiteSpace(SearchText) ? _filteredRowSnapshot : Games;
        var any = rows.Any(selector);
        var all = rows.All(selector);
        if (all)
        {
            return true;
        }

        return any ? null : false;
    }

    private void EvaluateDuplicateMatchesForRows()
    {
        _logger.Debug("Evaluating duplicate matches for selected platform.");
        _isUpdatingActions = true;
        try
        {
            var dataManager = PluginHelper.DataManager;
            var platformName = SelectedRomMPlatform?.LaunchBoxPlatformName ?? _shell.SelectedPlatform;
            if (dataManager == null || string.IsNullOrWhiteSpace(platformName))
            {
                _logger.Debug($"Duplicate match evaluation skipped. DataManager={dataManager != null}, PlatformName='{platformName ?? "<null>"}'.");
                return;
            }

            var platform = dataManager.GetPlatformByName(platformName);
            if (platform == null)
            {
                _logger.Debug($"Duplicate match evaluation skipped. LaunchBox platform not found: '{platformName}'.");
                return;
            }

            var matchByMd5 = SelectedDuplicateMatchOption == DuplicateMatchOption.Md5;
            var matchByTitle = SelectedDuplicateMatchOption == DuplicateMatchOption.GameName;
            var matchByFileName = SelectedDuplicateMatchOption == DuplicateMatchOption.FileName;
            var matchIndex = _importService.BuildMatchIndexForUi(dataManager, platform, matchByMd5);
            _logger.Debug($"Duplicate match strategy: MatchByRomId=True, MatchByMd5={matchByMd5}, MatchByTitle={matchByTitle}, MatchByFileName={matchByFileName}.");

            IEnumerable<ImportGameRow> rows = !string.IsNullOrWhiteSpace(SearchText) ? _filteredRowSnapshot : Games;
            foreach (var row in rows)
            {
                if (row?.Rom == null)
                {
                    continue;
                }

                var matchResult = _importService.EvaluateMatchForUi(matchIndex, platform, row.Rom, matchByRomId: true, matchByMd5, matchByTitle, matchByFileName);
                row.IsMergeSuggested = matchResult.Game != null;
                if (row.IsMergeSuggested)
                {
                    if (_importService.HasRomMTag(matchResult.Game, row.Rom))
                    {
                        row.SkipReason = "Already imported/merged";
                        row.StatusText = "Already present in LaunchBox";
                        if (!row.IsActionUserOverride)
                        {
                            row.Action = ImportAction.Skip;
                            SyncFlagsFromAction(row);
                        }
                        UpdateStatusTextForAction(row);
                        _logger.Info($"Duplicate match (already merged): RomM '{row.Name}' matched LaunchBox '{matchResult.Game.Title}' via {matchResult.Strategy} (Confidence={matchResult.Confidence}).");
                        continue;
                    }

                    row.SkipReason = "Matched local game (not yet merged)";
                    row.StatusText = "Local match found";
                    if (!row.IsActionUserOverride)
                    {
                        row.Action = ImportAction.Merge;
                        SyncFlagsFromAction(row);
                    }
                    UpdateStatusTextForAction(row);
                    _logger.Info($"Duplicate match: RomM '{row.Name}' matched LaunchBox '{matchResult.Game.Title}' via {matchResult.Strategy} (Confidence={matchResult.Confidence}).");
                    continue;
                }

                row.SkipReason = string.Empty;
                row.StatusText = "Ready to import";
                if (!row.IsActionUserOverride)
                {
                    row.Action = ImportAction.Import;
                    SyncFlagsFromAction(row);
                }
                UpdateStatusTextForAction(row);
                _logger.Debug($"Duplicate match: no match found for RomM '{row.Name}'.");
            }
        }
        finally
        {
            _isUpdatingActions = false;
        }

        if (BulkAction != null)
        {
            ApplyBulkAction(BulkAction);
        }
    }

    /// <summary>
    /// When the action changes, sync the row flags so the UI remains consistent.
    /// </summary>
    private void SyncFlagsFromAction(ImportGameRow row)
    {
        if (row == null)
        {
            return;
        }

        _isUpdatingActions = true;
        try
        {
            switch (row.Action)
            {
                case ImportAction.Import:
                    row.Import = true;
                    row.Download = false;
                    row.Saves = false;
                    row.IsActionUserOverride = true;
                    break;
                case ImportAction.Install:
                    row.Import = true;
                    row.Download = true;
                    row.Saves = false;
                    row.IsActionUserOverride = true;
                    break;
                case ImportAction.Merge:
                    row.Import = false;
                    row.Download = false;
                    row.Saves = false;
                    row.IsActionUserOverride = true;
                    break;
                case ImportAction.Skip:
                    row.Import = false;
                    row.Download = false;
                    row.Saves = false;
                    row.IsActionUserOverride = true;
                    break;
            }

            UpdateStatusTextForAction(row);
        }
        finally
        {
            _isUpdatingActions = false;
        }
    }

    /// <summary>
    /// Applies a bulk action to all visible rows, skipping merge-suggested rows.
    /// </summary>
    private void ApplyBulkAction(ImportAction? action)
    {
        if (action == null)
        {
            return;
        }

        _logger.Debug($"Applying bulk action: {action}.");
        _isApplyingBulkAction = true;
        try
        {
            IEnumerable<ImportGameRow> rows = !string.IsNullOrWhiteSpace(SearchText) ? _filteredRowSnapshot : Games;
            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (row.Action == ImportAction.Merge || row.IsMergeSuggested)
                {
                    continue;
                }

                row.Action = action.Value;
                row.IsActionUserOverride = true;
                SyncFlagsFromAction(row);
            }
        }
        finally
        {
            _isApplyingBulkAction = false;
        }
    }

    /// <summary>
    /// When flags change (import/download/saves), infer the correct action.
    /// </summary>
    private void SyncActionFromFlags(ImportGameRow row)
    {
        if (row == null)
        {
            return;
        }

        _isUpdatingActions = true;
        try
        {
            if (row.Import && row.Download)
            {
                row.Action = ImportAction.Install;
            }
            else if (row.Import)
            {
                row.Action = ImportAction.Import;
            }
            else if (row.Action != ImportAction.Skip)
            {
                row.Action = ImportAction.Merge;
            }

            UpdateStatusTextForAction(row);
        }
        finally
        {
            _isUpdatingActions = false;
        }
    }

    /// <summary>
    /// Keeps the status text aligned with the current action or suggestion.
    /// </summary>
    private void UpdateStatusTextForAction(ImportGameRow row)
    {
        if (row == null)
        {
            return;
        }

        switch (row.Action)
        {
            case ImportAction.Import:
                row.StatusText = "Ready to import";
                break;
            case ImportAction.Install:
                row.StatusText = "Ready to install";
                break;
            case ImportAction.Merge:
                if (row.IsMergeSuggested && !string.IsNullOrWhiteSpace(row.StatusText))
                {
                    return;
                }

                row.StatusText = "Ready to merge";
                break;
            case ImportAction.Skip:
                if (row.IsMergeSuggested && !string.IsNullOrWhiteSpace(row.StatusText))
                {
                    return;
                }

                row.StatusText = string.IsNullOrWhiteSpace(row.SkipReason)
                    ? "Skipped"
                    : row.SkipReason;
                break;
        }
    }

}
