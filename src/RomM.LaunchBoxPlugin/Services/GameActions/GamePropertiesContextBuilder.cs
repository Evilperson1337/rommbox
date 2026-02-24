using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Romm;
using RomMbox.Models;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    /// <summary>
    /// Builds <see cref="GamePropertiesContext"/> instances for the game properties UI.
    /// </summary>
    internal sealed class GamePropertiesContextBuilder
    {
        private readonly InstallStateService _installStateService;
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates a context builder with required services.
        /// </summary>
        /// <param name="installStateService">Service for install state and RomM details.</param>
        /// <param name="logger">Logging service.</param>
        public GamePropertiesContextBuilder(InstallStateService installStateService, LoggingService logger)
        {
            _installStateService = installStateService;
            _logger = logger;
        }

        /// <summary>
        /// Builds a game properties context for the provided game.
        /// </summary>
        /// <param name="game">The LaunchBox game.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The populated context, or null if the game is missing.</returns>
        public async Task<GamePropertiesContext> BuildAsync(IGame game, CancellationToken cancellationToken)
        {
            if (game == null)
            {
                return null;
            }

            var rommDetails = _installStateService.GetRomMDetails(game);
            var identity = _installStateService.GetIdentity(game);
            var state = await _installStateService.GetStateAsync(game.Id, cancellationToken).ConfigureAwait(false);
            var serverUrl = rommDetails.ServerUrl ?? string.Empty;
            var platformId = rommDetails.RommPlatformId ?? string.Empty;

            var rommRomId = identity.RommRomId ?? rommDetails.RommRomId;
            RommRom rommRom = null;
            if (!string.IsNullOrWhiteSpace(rommRomId))
            {
                try
                {
                    var settingsManager = new SettingsManager(_logger);
                    var client = new RommClient(_logger, settingsManager, requireServerUrl: false);
                    rommRom = await client.GetRomDetailsAsync(rommRomId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.Error("Failed to load RomM metadata for properties.", ex);
                }
            }

            var launchBoxPlatformName = ResolveLaunchBoxPlatform(platformId, game.Platform);

            return new GamePropertiesContext
            {
                Game = game,
                RommRom = rommRom,
                InstallState = state,
                RommRomId = rommRomId,
                RommPlatformId = identity.RommPlatformId ?? platformId,
                ServerUrl = serverUrl,
                LaunchBoxPlatformName = launchBoxPlatformName
            };
        }

        private string ResolveLaunchBoxPlatform(string rommPlatformId, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(rommPlatformId))
            {
                return fallbackName ?? string.Empty;
            }

            try
            {
                var settingsManager = new SettingsManager(_logger);
                var client = new RommClient(_logger, settingsManager, requireServerUrl: false);
                var mappingService = new PlatformMappingService(_logger, settingsManager, client);
                var mapping = mappingService.GetMapping(rommPlatformId);
                if (!string.IsNullOrWhiteSpace(mapping?.LaunchBoxPlatformName))
                {
                    return mapping.LaunchBoxPlatformName;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to resolve LaunchBox platform name for '{rommPlatformId}': {ex.Message}");
            }

            return fallbackName ?? string.Empty;
        }
    }
}
