using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    /// <summary>
    /// Builds <see cref="GameActionContext"/> instances for the game actions UI.
    /// </summary>
    internal sealed class GameActionsContextBuilder
    {
        private readonly InstallStateService _installStateService;
        private readonly RomMPlayUrlService _playUrlService;
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates a context builder with required services.
        /// </summary>
        /// <param name="installStateService">Service for install state and RomM details.</param>
        /// <param name="playUrlService">Service for building play URLs.</param>
        /// <param name="logger">Logging service.</param>
        public GameActionsContextBuilder(InstallStateService installStateService, RomMPlayUrlService playUrlService, LoggingService logger)
        {
            _installStateService = installStateService;
            _playUrlService = playUrlService;
            _logger = logger;
        }

        /// <summary>
        /// Builds a game action context for the provided game.
        /// </summary>
        /// <param name="game">The LaunchBox game.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The populated context, or null if the game is missing.</returns>
        public async Task<GameActionContext> BuildAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return null;
            }

            var rommDetails = _installStateService.GetRomMDetails(game);
            var isInstalled = await _installStateService.IsGameInstalledAsync(game, cancellationToken).ConfigureAwait(false);
            var playUrl = _playUrlService.BuildPlayUrl(rommDetails.ServerUrl, rommDetails.RommRomId);
            var canPlay = ResolvePlayableAsync(game);
            if (string.IsNullOrWhiteSpace(playUrl))
            {
                _logger?.Warning($"Play URL unavailable for '{game.Title}'.");
            }

            return new GameActionContext
            {
                Game = game,
                RommRomId = rommDetails.RommRomId,
                ServerUrl = rommDetails.ServerUrl,
                PlatformName = game.Platform,
                IsInstalled = isInstalled,
                PlayUrl = playUrl,
                CanPlayOnRomM = canPlay,
                ReleaseDate = game.ReleaseDate,
                Genres = game.GenresString,
                Description = game.Notes
            };
        }

        /// <summary>
        /// Determines whether the game can be played via RomM based on platform rules.
        /// </summary>
        /// <param name="game">The LaunchBox game.</param>
        /// <returns><c>true</c> if the game is considered playable in RomM.</returns>
        private bool ResolvePlayableAsync(IGame game)
        {
            if (game == null)
            {
                return false;
            }

            var rommDetails = _installStateService.GetRomMDetails(game);
            var settingsManager = PluginEntry.SettingsManager ?? new SettingsManager(_logger);
            if (!RommPlayability.IsPlayablePlatform(rommDetails.RommPlatformId, game.Platform, settingsManager))
            {
                return false;
            }

            return true;
        }
    }
}
