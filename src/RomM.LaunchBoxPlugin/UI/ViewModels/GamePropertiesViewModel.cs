using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RomMbox.Models.Romm;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.GameActions;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Views;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model for the game properties dialog.
/// </summary>
internal sealed class GamePropertiesViewModel : ObservableObject
{
    private readonly LoggingService _logger;
    private readonly GamePropertiesContext _context;
    private readonly ExternalLauncherService _launcherService;
    private readonly RommViewUrlService _viewUrlService;
    private readonly ImportService _importService;
    private readonly SettingsManager _settingsManager;
    private readonly PlatformMappingService _mappingService;
    private readonly InstallStateService _installStateService;
    private string _statusText;
    private bool _isBusy;

    /// <summary>
    /// Creates the view model for a selected game.
    /// </summary>
    /// <param name="context">Context containing game metadata.</param>
    /// <param name="logger">Logging service for UI actions.</param>
    internal GamePropertiesViewModel(GamePropertiesContext context, LoggingService logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
        _settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(_logger);
        var client = BuildClient();
        _mappingService = new PlatformMappingService(_logger, _settingsManager, client);
        _installStateService = PluginEntry.InstallStateService ?? new InstallStateService(_logger, _settingsManager);
        _launcherService = new ExternalLauncherService(_logger);
        _viewUrlService = new RommViewUrlService(_logger);
        _importService = new ImportService(_logger, _settingsManager, _mappingService, client);

        ViewOnRomMCommand = new RelayCommand(ViewOnRomM, () => CanViewOnRomM);
        UpdateGameCommand = new RelayCommand(UpdateGame, () => CanUpdateGame);
        CloseCommand = new RelayCommand(Close);

        StatusText = "Ready.";
        RefreshFromContext();
    }

    private IRommClient BuildClient()
    {
        try
        {
            return new RommClient(_logger, _settingsManager);
        }
        catch (Exception ex)
        {
        _logger?.Write(LogLevel.Warning, "UpdateGameClientInitFailed", ex,
            "GameTitle", _context.Game?.Title ?? string.Empty,
            "Platform", _context.Game?.Platform ?? string.Empty,
            "ServerUrl", LoggingService.SanitizeUrl(_settingsManager.Load()?.ServerUrl));
        return new RommClient(_logger, _settingsManager, requireServerUrl: false);
    }
    }

    /// <summary>
    /// Gets the display title of the game.
    /// </summary>
    public string Title { get; private set; }
    /// <summary>
    /// Gets the platform name used for the selection.
    /// </summary>
    public string PlatformName { get; private set; }
    /// <summary>
    /// Gets the formatted release date string.
    /// </summary>
    public string ReleaseDateDisplay { get; private set; }
    /// <summary>
    /// Gets the comma-separated genre display string.
    /// </summary>
    public string Genres { get; private set; }
    /// <summary>
    /// Gets the installed status display.
    /// </summary>
    public string InstalledStatus { get; private set; }
    /// <summary>
    /// Gets the installed path display.
    /// </summary>
    public string InstalledPath { get; private set; }
    /// <summary>
    /// Gets the install type display.
    /// </summary>
    public string WindowsInstallType { get; private set; }
    /// <summary>
    /// Gets the archive path display.
    /// </summary>
    public string ArchivePath { get; private set; }
    /// <summary>
    /// Gets the RomM ROM id.
    /// </summary>
    public string RommRomId { get; private set; }
    /// <summary>
    /// Gets the RomM platform id.
    /// </summary>
    public string RommPlatformId { get; private set; }
    /// <summary>
    /// Gets the RomM platform display name.
    /// </summary>
    public string RommPlatformName { get; private set; }
    /// <summary>
    /// Gets the RomM revision/version string.
    /// </summary>
    public string RommVersion { get; private set; }
    /// <summary>
    /// Gets the server URL display.
    /// </summary>
    public string ServerUrlDisplay { get; private set; }

    /// <summary>
    /// Gets whether the View on RomM button should be enabled.
    /// </summary>
    public bool CanViewOnRomM => !string.IsNullOrWhiteSpace(RommRomId) && !string.IsNullOrWhiteSpace(ServerUrlDisplay);
    /// <summary>
    /// Gets whether the Update Game button should be enabled.
    /// </summary>
    public bool CanUpdateGame => !_isBusy;

    /// <summary>
    /// Gets whether the view model is performing an update.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Gets the status message shown in the dialog footer.
    /// </summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>
    /// Command that attempts to open the RomM view URL in the browser.
    /// </summary>
    public RelayCommand ViewOnRomMCommand { get; }
    /// <summary>
    /// Command that updates RomM metadata for the game.
    /// </summary>
    public RelayCommand UpdateGameCommand { get; }
    /// <summary>
    /// Command that closes the dialog.
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// Action assigned by the view to close the dialog window.
    /// </summary>
    public Action CloseAction { get; set; }

    private void RefreshFromContext()
    {
        Title = _context.Game?.Title ?? string.Empty;
        PlatformName = _context.LaunchBoxPlatformName ?? _context.Game?.Platform ?? string.Empty;
        ReleaseDateDisplay = _context.Game?.ReleaseDate?.ToString("d") ?? string.Empty;
        Genres = _context.Game?.GenresString ?? string.Empty;
        InstalledStatus = _context.InstallState?.IsInstalled == true ? "Installed" : "Not Installed";
        InstalledPath = _context.InstallState?.InstalledPath ?? string.Empty;
        WindowsInstallType = _context.InstallState?.WindowsInstallType ?? string.Empty;
        ArchivePath = _context.InstallState?.ArchivePath ?? string.Empty;
        RommRomId = _context.RommRomId ?? string.Empty;
        RommPlatformId = _context.RommPlatformId ?? string.Empty;
        RommPlatformName = _context.RommRom?.PlatformDisplayName ?? string.Empty;
        RommVersion = _context.RommRom?.Revision ?? string.Empty;
        ServerUrlDisplay = NormalizeServerUrl(_context.ServerUrl ?? string.Empty);

        RaisePropertyChanged(nameof(Title));
        RaisePropertyChanged(nameof(PlatformName));
        RaisePropertyChanged(nameof(ReleaseDateDisplay));
        RaisePropertyChanged(nameof(Genres));
        RaisePropertyChanged(nameof(InstalledStatus));
        RaisePropertyChanged(nameof(InstalledPath));
        RaisePropertyChanged(nameof(WindowsInstallType));
        RaisePropertyChanged(nameof(ArchivePath));
        RaisePropertyChanged(nameof(RommRomId));
        RaisePropertyChanged(nameof(RommPlatformId));
        RaisePropertyChanged(nameof(RommPlatformName));
        RaisePropertyChanged(nameof(RommVersion));
        RaisePropertyChanged(nameof(ServerUrlDisplay));
        RaisePropertyChanged(nameof(CanViewOnRomM));
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            return serverUrl;
        }

        if ((uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && uri.Port == 443)
            || (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && uri.Port == 80))
        {
            var builder = new UriBuilder(uri)
            {
                Port = -1
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return uri.ToString().TrimEnd('/');
    }

    private void ViewOnRomM()
    {
        var operationId = Guid.NewGuid().ToString("N");
        var start = DateTimeOffset.UtcNow;

        _logger?.Write(LogLevel.Debug, "ViewOnRomMRequested", null,
            "OperationId", operationId,
            "GameTitle", Title,
            "Platform", PlatformName);

        if (string.IsNullOrWhiteSpace(ServerUrlDisplay))
        {
            _logger?.Write(LogLevel.Warning, "ViewOnRomMServerUrlMissing", null,
                "OperationId", operationId,
                "GameTitle", Title,
                "Platform", PlatformName);
            ShowInfoDialog("RomM", "Server URL is not configured. Configure it in RomM settings.");
            return;
        }

        if (string.IsNullOrWhiteSpace(RommRomId))
        {
            _logger?.Write(LogLevel.Warning, "ViewOnRomMMissingRomId", null,
                "OperationId", operationId,
                "GameTitle", Title,
                "Platform", PlatformName);
            ShowInfoDialog("RomM", "RomM ID is missing for this game.");
            return;
        }

        if (!Uri.TryCreate(ServerUrlDisplay, UriKind.Absolute, out _))
        {
            _logger?.Write(LogLevel.Warning, "ViewOnRomMInvalidServerUrl", null,
                "OperationId", operationId,
                "GameTitle", Title,
                "Platform", PlatformName);
            ShowInfoDialog("RomM", "Server URL is invalid. Please update it in RomM settings.");
            return;
        }

        using (_logger?.BeginOperation(operationId))
        {
            var url = _viewUrlService.BuildViewUrl(ServerUrlDisplay, RommRomId);
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger?.Write(LogLevel.Warning, "ViewOnRomMUrlBuildFailed", null,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);
                ShowInfoDialog("RomM", "RomM URL could not be built. Please check settings.");
                return;
            }

            if (_launcherService.TryOpenUrl(url))
            {
                _logger?.Write(LogLevel.Info, "ViewOnRomMOpened", null,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);
                StatusText = "RomM page opened in browser.";
                return;
            }

            _logger?.Write(LogLevel.Warning, "ViewOnRomMBrowserLaunchFailed", null,
                "OperationId", operationId,
                "GameTitle", Title,
                "Platform", PlatformName);
            ShowInfoDialog("RomM", "Failed to open the RomM page in your browser.");
        }

        var duration = DateTimeOffset.UtcNow - start;
        _logger?.Write(LogLevel.Info, "ViewOnRomMCompleted", null,
            "OperationId", operationId,
            "DurationMs", (long)duration.TotalMilliseconds,
            "Game", $"{Title} ({PlatformName})");
    }

    private void UpdateGame()
    {
        if (_isBusy)
        {
            return;
        }

        IsBusy = true;
        RaisePropertyChanged(nameof(CanUpdateGame));
        UpdateGameCommand.RaiseCanExecuteChanged();
        StatusText = "Updating RomM metadata...";

        Task.Run(UpdateGameAsync);
    }

    private async Task UpdateGameAsync()
    {
        var operationId = Guid.NewGuid().ToString("N");
        var start = DateTimeOffset.UtcNow;
        using (_logger?.BeginOperation(operationId))
        {
                _logger?.Write(LogLevel.Info, "UpdateGameStarted", null,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);

            try
            {
                var serverUrl = _settingsManager.Load()?.ServerUrl?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameServerUrlMissing", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    ShowInfoDialog("RomM", "Server URL is not configured. Configure it in RomM settings.");
                    return;
                }

                if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameServerUrlInvalid", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    ShowInfoDialog("RomM", "Server URL is invalid. Please update it in RomM settings.");
                    return;
                }

                if (!TryResolvePlatformId(out var platformId))
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameMissingPlatformMapping", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    ShowInfoDialog("RomM", "Platform mapping is missing. Configure the platform mapping first.");
                    return;
                }

                var dataManager = PluginHelper.DataManager;
                var platform = dataManager?.GetPlatformByName(PlatformName);
                if (dataManager == null || platform == null)
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameMissingDataManager", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    ShowInfoDialog("RomM", "LaunchBox data services are unavailable.");
                    return;
                }

                var candidates = await FetchRomCandidatesAsync(platformId).ConfigureAwait(false);
                if (candidates.Count == 0)
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameNoMatches", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    ShowInfoDialog("RomM", "No RomM match was found for this game.");
                    return;
                }

                var selection = SelectRomMatch(candidates, operationId);
                if (selection == null)
                {
                    _logger?.Write(LogLevel.Warning, "UpdateGameSelectionCancelled", null,
                        "OperationId", operationId,
                        "GameTitle", Title,
                        "Platform", PlatformName);
                    return;
                }

                _logger?.Write(LogLevel.Debug, "UpdateGameApplyMatch", null,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);

                _importService.ApplyMatchForReview(_context.Game, selection.Rom, selection.Strategy, selection.Confidence);
                var romDetails = await _importService.ResolveRomDetailsForReview(selection.Rom.Id, CancellationToken.None).ConfigureAwait(false);
                if (romDetails != null)
                {
                    _context.RommRom = romDetails;
                }
                _context.RommRomId = selection.Rom.Id;
                _context.RommPlatformId = selection.Rom.PlatformId;

                var identity = _installStateService.GetIdentity(_context.Game);
                _context.RommRomId = identity.RommRomId ?? _context.RommRomId;
                _context.RommPlatformId = identity.RommPlatformId ?? _context.RommPlatformId;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RefreshFromContext();
                    StatusText = "RomM metadata updated.";
                });

                _logger?.Write(LogLevel.Info, "UpdateGameCompleted", null,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);
            }
            catch (Exception ex)
            {
                _logger?.Write(LogLevel.Error, "UpdateGameFailed", ex,
                    "OperationId", operationId,
                    "GameTitle", Title,
                    "Platform", PlatformName);
                ShowInfoDialog("RomM", "Failed to update the game from RomM.");
            }
            finally
            {
                var duration = DateTimeOffset.UtcNow - start;
                _logger?.Write(LogLevel.Info, "UpdateGameFinished", null,
                    "OperationId", operationId,
                    "DurationMs", (long)duration.TotalMilliseconds,
                    "Game", $"{Title} ({PlatformName})");

                IsBusy = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RaisePropertyChanged(nameof(CanUpdateGame));
                    UpdateGameCommand.RaiseCanExecuteChanged();
                });
            }
        }
    }

    private bool TryResolvePlatformId(out string platformId)
    {
        platformId = string.Empty;
        if (!string.IsNullOrWhiteSpace(_context.RommPlatformId))
        {
            platformId = _context.RommPlatformId;
            return true;
        }

        try
        {
            var settings = _settingsManager.Load();
            var mapping = settings?.PlatformMappings?.FirstOrDefault(entry =>
                string.Equals(entry.LaunchBoxPlatformName, PlatformName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(mapping?.RommPlatformId))
            {
                platformId = mapping.RommPlatformId;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Write(LogLevel.Warning, "UpdateGamePlatformLookupFailed", ex,
                "GameTitle", Title,
                "Platform", PlatformName,
                "Phase", "PlatformLookup");
        }

        return false;
    }

    private async Task<List<RommRom>> FetchRomCandidatesAsync(string platformId)
    {
        var results = new List<RommRom>();
        var pageResult = await _importService.ListPlatformRomsAsync(platformId, CancellationToken.None).ConfigureAwait(false);
        if (pageResult != null && pageResult.Count > 0)
        {
            results.AddRange(pageResult);
        }

        var normalizedTitle = NormalizeTitleForMatch(Title);
        return results
            .Where(rom => rom != null)
            .Where(rom => string.Equals(rom.PlatformId, platformId, StringComparison.OrdinalIgnoreCase))
            .Where(rom => IsTitleMatch(normalizedTitle, rom))
            .ToList();
    }

    private bool IsTitleMatch(string normalizedTitle, RommRom rom)
    {
        if (rom == null)
        {
            return false;
        }

        var candidate = NormalizeTitleForMatch(rom.DisplayTitle ?? rom.Title ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.Equals(normalizedTitle, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ComputeTitleSimilarity(normalizedTitle, candidate) >= 0.72;
    }

    private static string NormalizeTitleForMatch(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = title.Trim();
        value = StripBracketedSegments(value);
        value = value.Replace("&", "and", StringComparison.OrdinalIgnoreCase);
        value = value.Normalize(System.Text.NormalizationForm.FormD);
        var normalized = new string(value
            .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => char.ToLowerInvariant(ch))
            .ToArray());
        normalized = string.Join(" ", normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        return normalized;
    }

    private static string StripBracketedSegments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch == '(' || ch == '[' || ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == ')' || ch == ']' || ch == '}')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }

            if (depth == 0)
            {
                buffer[index++] = ch;
            }
        }

        return new string(buffer.Slice(0, index));
    }

    private static double ComputeTitleSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0.0;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return 0.0;
        }

        var leftSet = new HashSet<string>(leftTokens, StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(rightTokens, StringComparer.OrdinalIgnoreCase);
        var intersection = leftSet.Intersect(rightSet, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftSet.Union(rightSet, StringComparer.OrdinalIgnoreCase).Count();
        if (union == 0)
        {
            return 0.0;
        }

        return (double)intersection / union;
    }

    private RomMatchSelection SelectRomMatch(List<RommRom> candidates, string operationId)
    {
        var dataManager = PluginHelper.DataManager;
        var platform = dataManager?.GetPlatformByName(PlatformName);
        if (platform == null)
        {
            return null;
        }

        var matchIndex = _importService.BuildMatchIndexForUi(dataManager, platform, includeMd5: true);
        var matches = new List<RomMatchSelection>();
        foreach (var rom in candidates)
        {
            var match = _importService.EvaluateMatchForUi(matchIndex, platform, rom, matchByRomId: true, matchByMd5: true, matchByTitle: true, matchByFileName: true);
            if (match.Game == null || !string.Equals(match.Game.Id, _context.Game?.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches.Add(new RomMatchSelection
            {
                Rom = rom,
                Strategy = match.Strategy,
                Confidence = match.Confidence
            });
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        return ShowMatchSelectionDialog(matches, operationId);
    }

    private RomMatchSelection ShowMatchSelectionDialog(List<RomMatchSelection> matches, string operationId)
    {
        var selected = default(RomMatchSelection);
        Application.Current.Dispatcher.Invoke(() =>
        {
            var viewModel = new MatchReviewViewModel
            {
                RequireAcceptance = true
            };
            foreach (var entry in matches)
            {
                viewModel.Matches.Add(new UI.Models.MatchReviewRow
                {
                    RommId = entry.Rom?.Id,
                    RommTitle = entry.Rom?.DisplayTitle ?? entry.Rom?.Title,
                    LaunchBoxTitle = Title,
                    Strategy = entry.Strategy,
                    Confidence = entry.Confidence,
                    Decision = false
                });
            }

            var suppressDecision = false;
            foreach (var row in viewModel.Matches)
            {
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(UI.Models.MatchReviewRow.Decision))
                    {
                        if (suppressDecision)
                        {
                            return;
                        }

                        if (row.Decision == true)
                        {
                            suppressDecision = true;
                            foreach (var other in viewModel.Matches)
                            {
                                if (!ReferenceEquals(other, row))
                                {
                                    other.Decision = false;
                                }
                            }
                            suppressDecision = false;
                        }

                        viewModel.ApplyCommand.RaiseCanExecuteChanged();
                    }
                };
            }

            var dialog = new MatchReviewDialog
            {
                DataContext = viewModel,
                Owner = Application.Current?.MainWindow
            };

            viewModel.RequestClose += result =>
            {
                dialog.DialogResult = result;
                dialog.Close();
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            var accepted = viewModel.Matches.FirstOrDefault(row => row.Decision == true);
            if (accepted == null)
            {
                return;
            }

            selected = matches.FirstOrDefault(entry => string.Equals(entry.Rom?.Id, accepted.RommId, StringComparison.OrdinalIgnoreCase));
        });

        return selected;
    }

    private void Close()
    {
        CloseAction?.Invoke();
    }

    private void ShowInfoDialog(string title, string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var dialog = new InfoDialog(title, message)
            {
                Owner = Application.Current?.MainWindow
            };
            dialog.ShowDialog();
        });
    }

    private sealed class RomMatchSelection
    {
        public RommRom Rom { get; init; }
        public string Strategy { get; init; }
        public string Confidence { get; init; }
    }
}
