using System;
using RomMbox.Services;
using RomMbox.Services.GameActions;
using RomMbox.Services.Logging;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI;

/// <summary>
/// View model backing the game actions dialog with metadata and play actions.
/// </summary>
internal sealed class GameActionsDialogViewModel : ObservableObject
{
    private readonly LoggingService _logger;
    private readonly ExternalLauncherService _launcherService;
    private readonly GameActionContext _context;

    /// <summary>
    /// Creates the view model for a selected game.
    /// </summary>
    /// <param name="context">Context containing game metadata and play URLs.</param>
    /// <param name="logger">Logging service for UI actions.</param>
    internal GameActionsDialogViewModel(GameActionContext context, LoggingService logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
        _launcherService = new ExternalLauncherService(_logger);

        PlayCommand = new RelayCommand(PlayOnRomM);
        CloseCommand = new RelayCommand(Close);

        StatusText = "Ready.";
    }

    /// <summary>
    /// Gets the display title of the game.
    /// </summary>
    public string GameTitle => _context.Game?.Title ?? string.Empty;
    /// <summary>
    /// Gets the platform name used for the selection.
    /// </summary>
    public string PlatformName => _context.PlatformName ?? string.Empty;
    /// <summary>
    /// Gets the formatted release date string.
    /// </summary>
    public string ReleaseDateDisplay => _context.ReleaseDate?.ToString("d") ?? string.Empty;
    /// <summary>
    /// Gets the comma-separated genre display string.
    /// </summary>
    public string Genres => _context.Genres ?? string.Empty;
    /// <summary>
    /// Gets the game description used in the dialog body.
    /// </summary>
    public string Description => _context.Description ?? string.Empty;
    /// <summary>
    /// Gets whether the game can be launched via RomM.
    /// </summary>
    public bool CanPlayOnRomM => _context.CanPlayOnRomM;
    private string _statusText;
    /// <summary>
    /// Gets the status message shown in the dialog footer.
    /// </summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>
    /// Command that attempts to open the RomM play URL in the browser.
    /// </summary>
    public RelayCommand PlayCommand { get; }
    /// <summary>
    /// Command that closes the dialog.
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// Action assigned by the view to close the dialog window.
    /// </summary>
    public Action CloseAction { get; set; }

    /// <summary>
    /// Attempts to open the RomM play URL and updates the status text based on the outcome.
    /// </summary>
    private void PlayOnRomM()
    {
        if (!CanPlayOnRomM)
        {
            StatusText = "Play on RomM is unavailable for this game.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_context.PlayUrl))
        {
            StatusText = "Play URL is unavailable.";
            return;
        }

        _logger?.Info($"Play on RomM requested for '{_context.Game?.Title}'. URL={_context.PlayUrl}");
        if (_launcherService.TryOpenUrl(_context.PlayUrl))
        {
            StatusText = "Play on RomM opened in browser.";
            return;
        }

        StatusText = "Play on RomM failed to open browser.";
    }

    /// <summary>
    /// Invokes the dialog close action when the user cancels.
    /// </summary>
    private void Close()
    {
        CloseAction?.Invoke();
    }
}
