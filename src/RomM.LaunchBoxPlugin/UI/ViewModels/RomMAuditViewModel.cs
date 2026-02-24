using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RomMbox.Models.Audit;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;
using Unbroken.LaunchBox.Plugins;

namespace RomMbox.UI.ViewModels;

internal sealed class RomMAuditViewModel : ObservableObject
{
    private readonly LoggingService _logger;
    private readonly SettingsManager _settingsManager;
    private readonly PlatformMappingService _mappingService;
    private readonly RomMAuditService _auditService;
    private CancellationTokenSource _cts;
    private bool _isRunning;
    private int _progressPercent;
    private string _statusText;
    private string _previewSummaryText;
    private string _resultsSummaryText;
    private RommPlatformOption _selectedPlatform;
    private int _maxParallelism = 4;
    private int _apiDelayMs = 0;
    private bool _rematchMissing = true;
    private bool _revalidateExisting;
    private bool _forceRematch;
    private bool _dryRun;

    public RomMAuditViewModel()
    {
        PluginEntry.EnsureInitialized();
        _logger = PluginEntry.Logger;
        _settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(_logger);
        var client = new RommClient(_logger, _settingsManager, requireServerUrl: true);
        _mappingService = new PlatformMappingService(_logger, _settingsManager, client);
        _auditService = new RomMAuditService(
            _logger,
            _settingsManager,
            PluginEntry.InstallStateService ?? new InstallStateService(_logger, _settingsManager),
            _mappingService,
            new ImportService(_logger, _settingsManager, _mappingService, client));

        Platforms = new ObservableCollection<RommPlatformOption>();
        ResultsLog = new ObservableCollection<string>();

        StartCommand = new RelayCommand(StartAudit, () => CanStart);
        CancelCommand = new RelayCommand(CancelAudit, () => CanCancel);
        CloseCommand = new RelayCommand(Close);

        StatusText = "Select a platform and configure audit options.";
        PreviewSummaryText = string.Empty;
        ResultsSummaryText = string.Empty;
        _ = LoadPlatformsAsync();
    }

    public ObservableCollection<RommPlatformOption> Platforms { get; }
    public ObservableCollection<string> ResultsLog { get; }

    public RommPlatformOption SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (SetProperty(ref _selectedPlatform, value))
            {
                _ = RefreshSummaryAsync();
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int MaxParallelism
    {
        get => _maxParallelism;
        set => SetProperty(ref _maxParallelism, Math.Max(1, value));
    }

    public int ApiDelayMs
    {
        get => _apiDelayMs;
        set => SetProperty(ref _apiDelayMs, Math.Max(0, value));
    }

    public bool RematchMissing
    {
        get => _rematchMissing;
        set => SetProperty(ref _rematchMissing, value);
    }

    public bool RevalidateExisting
    {
        get => _revalidateExisting;
        set => SetProperty(ref _revalidateExisting, value);
    }

    public bool ForceRematch
    {
        get => _forceRematch;
        set => SetProperty(ref _forceRematch, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => SetProperty(ref _dryRun, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value ?? string.Empty);
    }

    public string PreviewSummaryText
    {
        get => _previewSummaryText;
        private set => SetProperty(ref _previewSummaryText, value ?? string.Empty);
    }

    public string ResultsSummaryText
    {
        get => _resultsSummaryText;
        private set => SetProperty(ref _resultsSummaryText, value ?? string.Empty);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanStart => !IsRunning && SelectedPlatform != null;
    public bool CanCancel => IsRunning;

    public RelayCommand StartCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CloseCommand { get; }
    public Action CloseAction { get; set; }

    private async Task LoadPlatformsAsync()
    {
        try
        {
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
                Platforms.Clear();
                foreach (var option in options)
                {
                    Platforms.Add(option);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to load platforms for audit.", ex);
        }
    }

    private async Task RefreshSummaryAsync()
    {
        if (SelectedPlatform == null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PreviewSummaryText = string.Empty;
            });
            return;
        }

        var summary = await Task.Run(() =>
        {
            var dataManager = PluginHelper.DataManager;
            if (dataManager == null)
            {
                return "LaunchBox data services unavailable.";
            }

            var platformName = SelectedPlatform.LaunchBoxPlatformName ?? string.Empty;
            var games = dataManager.GetAllGames() ?? Array.Empty<Unbroken.LaunchBox.Plugins.Data.IGame>();
            var platformGames = games.Where(game => game != null && string.Equals(game.Platform, platformName, StringComparison.OrdinalIgnoreCase)).ToList();
            var total = platformGames.Count;
            var installState = PluginEntry.InstallStateService ?? new InstallStateService(_logger, _settingsManager);
            var missing = platformGames.Count(game => string.IsNullOrWhiteSpace(installState.GetIdentity(game).RommRomId));
            var has = total - missing;

            return $"Total games: {total}\nMissing RomM ID: {missing}\nWith RomM ID: {has}\nCandidates for re-match: {(ForceRematch ? total : missing)}";
        }).ConfigureAwait(false);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            PreviewSummaryText = summary;
        });
    }

    private void StartAudit()
    {
        if (IsRunning || SelectedPlatform == null)
        {
            return;
        }

        IsRunning = true;
        ResultsLog.Clear();
        ResultsSummaryText = string.Empty;
        ProgressPercent = 0;
        StatusText = "Starting audit...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var request = new RomMAuditRequest
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            RommPlatformId = SelectedPlatform.Id,
            RommPlatformName = SelectedPlatform.Name,
            LaunchBoxPlatformName = SelectedPlatform.LaunchBoxPlatformName,
            Options = new RomMAuditOptions
            {
                RematchMissingRommId = RematchMissing,
                RevalidateExistingMatches = RevalidateExisting,
                ForceFullRematch = ForceRematch,
                DryRun = DryRun,
                MaxParallelism = MaxParallelism,
                ApiDelayMs = ApiDelayMs
            }
        };

        var progress = new Progress<RomMAuditProgress>(payload =>
        {
            ProgressPercent = (int)Math.Round(payload.PercentComplete);
            StatusText = $"{payload.Processed}/{payload.TotalGames} - {payload.CurrentGameTitle}";
        });

        _ = RunAuditAsync(request, progress, _cts.Token);
    }

    private async Task RunAuditAsync(RomMAuditRequest request, IProgress<RomMAuditProgress> progress, CancellationToken cancellationToken)
    {
        RomMAuditResult result = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await _auditService.RunAuditAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultsLog.Add("Audit cancelled.");
                StatusText = "Audit cancelled.";
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("Audit failed.", ex);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultsLog.Add("Audit failed: " + ex.Message);
                StatusText = "Audit failed.";
            });
        }
        finally
        {
            stopwatch.Stop();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (result != null)
                {
                    foreach (var entry in result.GameResults)
                    {
                        var line = BuildResultLine(entry);
                        ResultsLog.Add(line);
                    }

                    ResultsSummaryText = BuildSummary(result);
                    StatusText = result.Cancelled ? "Audit cancelled." : "Audit completed.";
                }
                else
                {
                    ResultsSummaryText = "Audit failed to produce results.";
                }

                IsRunning = false;
                ProgressPercent = result == null ? 0 : 100;
            });
        }
    }

    private void CancelAudit()
    {
        if (!IsRunning)
        {
            return;
        }

        StatusText = "Cancelling audit...";
        _cts?.Cancel();
    }

    private static string BuildResultLine(RomMAuditGameResult entry)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        var baseText = $"{entry.GameTitle}: {entry.Outcome}";
        if (!string.IsNullOrWhiteSpace(entry.NewRommId))
        {
            baseText += $" (RomM {entry.NewRommId})";
        }
        if (!string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            baseText += $" - {entry.ErrorMessage}";
        }

        return baseText;
    }

    private static string BuildSummary(RomMAuditResult result)
    {
        if (result == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Total Audited: {result.Summary.TotalGames}");
        builder.AppendLine($"Updated: {result.Summary.Updated}");
        builder.AppendLine($"Unchanged: {result.Summary.Unchanged}");
        builder.AppendLine($"Failed: {result.Summary.Failed}");
        builder.AppendLine($"Missing Matches: {result.Summary.MissingMatches}");
        builder.AppendLine($"Duration: {result.Summary.Duration:c}");
        return builder.ToString().TrimEnd();
    }

    private void Close()
    {
        CloseAction?.Invoke();
    }
}
