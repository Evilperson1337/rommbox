using System;
using RomMbox.Plugin;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    internal interface IRomMPlaySupportEvaluator
    {
        RomMPlaySupportResult Evaluate(IGame game);
    }

    internal sealed class RomMPlaySupportEvaluator : IRomMPlaySupportEvaluator
    {
        private readonly InstallStateService _installStateService;
        private readonly SettingsManager _settingsManager;
        private readonly PlatformInstallerRegistry _platformRegistry;
        private readonly LoggingService _logger;
        private readonly RomMPlayUrlService _playUrlService;

        public RomMPlaySupportEvaluator(
            InstallStateService installStateService,
            SettingsManager settingsManager,
            PlatformInstallerRegistry platformRegistry,
            LoggingService logger)
        {
            _installStateService = installStateService;
            _settingsManager = settingsManager;
            _platformRegistry = platformRegistry;
            _logger = logger;
            _playUrlService = new RomMPlayUrlService(logger);
        }

        public RomMPlaySupportResult Evaluate(IGame game)
        {
            var result = new RomMPlaySupportResult
            {
                PlatformName = game?.Platform ?? string.Empty
            };

            if (game == null)
            {
                result.Reason = "Game is null.";
                return result;
            }

            _logger?.Info($"[RomM Play] Evaluating GameId={game.Id ?? string.Empty} Title=\"{game.Title ?? string.Empty}\" Platform=\"{game.Platform ?? string.Empty}\"");

            if (_installStateService == null)
            {
                result.Reason = "Install state service unavailable.";
                _logger?.Info("[RomM Play] IsPlayableOnRomm=False Reason=InstallStateUnavailable");
                return result;
            }

            var details = _installStateService.GetRomMDetails(game);
            result.HasRommAssociation = !string.IsNullOrWhiteSpace(details.RommRomId)
                && (!string.IsNullOrWhiteSpace(details.RommPlatformId) || !string.IsNullOrWhiteSpace(game.Platform));

            if (!result.HasRommAssociation)
            {
                result.Reason = "Missing RomM association.";
                _logger?.Info($"[RomM Play] HasAssociation=False LaunchTarget=\"\" IsPlayableOnRomm=False Reason=MissingAssociation GameId={game.Id ?? string.Empty}");
                return result;
            }

            var profile = RommPlayEndpointResolver.Resolve(details.RommPlatformId, game.Platform, _settingsManager, _platformRegistry, _logger);
            result.PlatformName = string.IsNullOrWhiteSpace(game.Platform) ? details.RommPlatformId ?? string.Empty : game.Platform;
            result.IsPlayableOnRomm = profile?.IsPlayableOnRomM == true;

            if (!result.IsPlayableOnRomm)
            {
                result.Reason = "Platform not recognized as playable on RomM.";
                _logger?.Info($"[RomM Play] HasAssociation=True LaunchTarget=\"\" IsPlayableOnRomm=False Reason=PlatformNotPlayable GameId={game.Id ?? string.Empty} RommPlatformId=\"{details.RommPlatformId ?? string.Empty}\" Platform=\"{game.Platform ?? string.Empty}\"");
                return result;
            }

            result.LaunchTarget = _playUrlService.BuildPlayUrl(
                details.ServerUrl,
                details.RommRomId,
                details.RommPlatformId,
                game.Platform,
                _settingsManager,
                _platformRegistry);

            result.IsPlayableOnRomm = !string.IsNullOrWhiteSpace(result.LaunchTarget);
            result.Reason = result.IsPlayableOnRomm
                ? $"Recognized RomM endpoint '{profile?.PathSuffix ?? string.Empty}'."
                : "Launch target could not be built.";

            _logger?.Info($"[RomM Play] HasAssociation={result.HasRommAssociation} LaunchTarget=\"{result.LaunchTarget ?? string.Empty}\"");
            _logger?.Info($"[RomM Play] IsPlayableOnRomm={result.IsPlayableOnRomm} Reason=\"{result.Reason}\"");
            return result;
        }
    }

    internal sealed class RomMPlaySupportResult
    {
        public bool HasRommAssociation { get; set; }
        public bool IsPlayableOnRomm { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string LaunchTarget { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
    }
}
