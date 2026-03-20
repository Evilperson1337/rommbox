using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.GameActions
{
    internal interface IRomMGameMenuActionService
    {
        bool IsRomMMenuVisible(IGame game);
        IReadOnlyList<RomMGameMenuActionCandidate> GetCandidates(IGame game);
        IReadOnlyList<RomMGameMenuActionDefinition> BuildActions(IGame game);
        RomMGameMenuActionDefinition GetAction(IGame game, string caption);
    }

    internal sealed class RomMGameMenuActionService : IRomMGameMenuActionService
    {
        private readonly Func<IGame, bool> _isRomMCompatible;
        private readonly Func<IGame, bool> _isInstalled;
        private readonly Func<IGame, RomMPlaySupportResult> _playSupportEvaluator;
        private readonly Action<IGame> _installAction;
        private readonly Action<IGame> _uninstallAction;
        private readonly Action<IGame> _viewAction;
        private readonly Action<IGame> _playAction;
        private readonly Action<IGame> _refreshAction;
        private readonly Action<IGame> _propertiesAction;
        private readonly Func<Image> _rommIconFactory;
        private readonly Func<Image> _installIconFactory;
        private readonly Func<Image> _uninstallIconFactory;
        private readonly Func<Image> _viewIconFactory;
        private readonly Func<Image> _playIconFactory;
        private readonly Func<Image> _refreshIconFactory;
        private readonly Func<Image> _propertiesIconFactory;

        public RomMGameMenuActionService(
            Func<IGame, bool> isRomMCompatible,
            Func<IGame, bool> isInstalled,
            Func<IGame, RomMPlaySupportResult> playSupportEvaluator,
            Action<IGame> installAction,
            Action<IGame> uninstallAction,
            Action<IGame> viewAction,
            Action<IGame> playAction,
            Action<IGame> refreshAction,
            Action<IGame> propertiesAction,
            Func<Image> rommIconFactory,
            Func<Image> installIconFactory,
            Func<Image> uninstallIconFactory,
            Func<Image> viewIconFactory,
            Func<Image> playIconFactory,
            Func<Image> refreshIconFactory,
            Func<Image> propertiesIconFactory)
        {
            _isRomMCompatible = isRomMCompatible ?? throw new ArgumentNullException(nameof(isRomMCompatible));
            _isInstalled = isInstalled ?? throw new ArgumentNullException(nameof(isInstalled));
            _playSupportEvaluator = playSupportEvaluator ?? throw new ArgumentNullException(nameof(playSupportEvaluator));
            _installAction = installAction ?? throw new ArgumentNullException(nameof(installAction));
            _uninstallAction = uninstallAction ?? throw new ArgumentNullException(nameof(uninstallAction));
            _viewAction = viewAction ?? throw new ArgumentNullException(nameof(viewAction));
            _playAction = playAction ?? throw new ArgumentNullException(nameof(playAction));
            _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
            _propertiesAction = propertiesAction ?? throw new ArgumentNullException(nameof(propertiesAction));
            _rommIconFactory = rommIconFactory ?? throw new ArgumentNullException(nameof(rommIconFactory));
            _installIconFactory = installIconFactory ?? throw new ArgumentNullException(nameof(installIconFactory));
            _uninstallIconFactory = uninstallIconFactory ?? throw new ArgumentNullException(nameof(uninstallIconFactory));
            _viewIconFactory = viewIconFactory ?? throw new ArgumentNullException(nameof(viewIconFactory));
            _playIconFactory = playIconFactory ?? throw new ArgumentNullException(nameof(playIconFactory));
            _refreshIconFactory = refreshIconFactory ?? throw new ArgumentNullException(nameof(refreshIconFactory));
            _propertiesIconFactory = propertiesIconFactory ?? throw new ArgumentNullException(nameof(propertiesIconFactory));
        }

        public bool IsRomMMenuVisible(IGame game)
        {
            return game != null && _isRomMCompatible(game);
        }

        public IReadOnlyList<RomMGameMenuActionDefinition> BuildActions(IGame game)
        {
            return GetCandidates(game)
                .Where(candidate => candidate.Visible)
                .Select(candidate => candidate.Definition)
                .ToList();
        }

        public IReadOnlyList<RomMGameMenuActionCandidate> GetCandidates(IGame game)
        {
            if (!IsRomMMenuVisible(game))
            {
                return Array.Empty<RomMGameMenuActionCandidate>();
            }

            var actions = new List<RomMGameMenuActionCandidate>();
            var isInstalled = _isInstalled(game);
            var playSupport = _playSupportEvaluator(game) ?? new RomMPlaySupportResult();

            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    RommAdditionalApplicationService.InstallCaption,
                    true,
                    _installIconFactory(),
                    () => _installAction(game)),
                !isInstalled,
                isInstalled ? "Game already installed." : "Game not installed."));

            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    "Uninstall RomM Version...",
                    true,
                    _uninstallIconFactory(),
                    () => _uninstallAction(game)),
                isInstalled,
                isInstalled ? "Game installed." : "Game not installed."));

            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    "View on RomM",
                    true,
                    _viewIconFactory(),
                    () => _viewAction(game)),
                true,
                "RomM association available."));

            var canPlay = playSupport.IsPlayableOnRomm;
            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    "Play on RomM",
                    true,
                    _playIconFactory(),
                    () => _playAction(game)),
                canPlay,
                canPlay ? playSupport.Reason : playSupport.Reason));

            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    "Refresh RomM State",
                    true,
                    _refreshIconFactory(),
                    () => _refreshAction(game)),
                true,
                "Always available."));

            actions.Add(new RomMGameMenuActionCandidate(
                new RomMGameMenuActionDefinition(
                    "Properties",
                    true,
                    _propertiesIconFactory(),
                    () => _propertiesAction(game)),
                true,
                "Always available."));

            return actions;
        }

        public RomMGameMenuActionDefinition GetAction(IGame game, string caption)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                return null;
            }

            return BuildActions(game)
                .FirstOrDefault(action => string.Equals(action.Caption, caption, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class RomMGameMenuActionDefinition
    {
        public RomMGameMenuActionDefinition(string caption, bool enabled, Image icon, Action execute)
        {
            Caption = caption ?? string.Empty;
            Enabled = enabled;
            Icon = icon;
            Execute = execute;
        }

        public string Caption { get; }
        public bool Enabled { get; }
        public Image Icon { get; }
        public Action Execute { get; }
    }

    internal sealed class RomMGameMenuActionCandidate
    {
        public RomMGameMenuActionCandidate(RomMGameMenuActionDefinition definition, bool visible, string reason)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Visible = visible;
            Reason = reason ?? string.Empty;
        }

        public RomMGameMenuActionDefinition Definition { get; }
        public bool Visible { get; }
        public string Reason { get; }
    }
}
