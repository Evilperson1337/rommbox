using System;
using System.ComponentModel.Composition;
using System.Drawing;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.GameActions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.GameMenu
{
    internal static class RomMGameMenuActionProvider
    {
        private static readonly Lazy<IRomMGameMenuActionService> MenuActionService = new(CreateMenuActionService);

        public static bool IsActionVisible(IGame game, string caption, string context)
        {
            var action = GetAction(game, caption);
            if (action != null)
            {
                PluginEntry.Logger?.Info($"[{context}] Menu shown for GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\" Caption='{caption}'.");
            }

            return action != null;
        }

        public static Image GetActionIcon(IGame game, string caption)
        {
            return GetAction(game, caption)?.Icon;
        }

        public static bool Execute(IGame game, string caption, string context)
        {
            var action = GetAction(game, caption);
            if (action == null)
            {
                PluginEntry.Logger?.Warning($"[{context}] Selection triggered without available action. GameId={game?.Id ?? string.Empty} Caption='{caption ?? string.Empty}'.");
                return false;
            }

            PluginEntry.Logger?.Info($"[{context}] Selection triggered for GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\" Caption='{action.Caption}'.");
            action.Execute?.Invoke();
            return true;
        }

        private static RomMGameMenuActionDefinition GetAction(IGame game, string caption)
        {
            if (game == null)
            {
                return null;
            }

            PluginEntry.EnsureInitialized();
            return MenuActionService.Value.GetAction(game, caption);
        }

        private static IRomMGameMenuActionService CreateMenuActionService()
        {
            return new RomMGameMenuActionService(
                game =>
                {
                    var service = PluginEntry.InstallStateService;
                    return game != null && service != null && service.IsRomMSourcedGame(game);
                },
                game => RommMultiMenuItem.HasValidApplicationPathForMenu(game),
                game => RommMultiMenuItem.EvaluatePlaySupport(game),
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: Install. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.InstallGame(game);
                },
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: Uninstall. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.UninstallGameForMenu(game);
                },
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: View. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.ViewOnRomMForMenu(game);
                },
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: Play. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.PlayOnRomMForMenu(game);
                },
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: Refresh. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.RefreshRomMStateForMenu(game);
                },
                game =>
                {
                    PluginEntry.Logger?.Info($"[RomM Menu] Action executed: Properties. GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
                    RommMultiMenuItem.OpenPropertiesForMenu(game);
                },
                RommMultiMenuItem.ResolveRommBadgeForMenu,
                RommMultiMenuItem.ResolveInstallIconForMenu,
                RommMultiMenuItem.ResolveUninstallIconForMenu,
                RommMultiMenuItem.ResolveViewIconForMenu,
                RommMultiMenuItem.ResolvePlayIconForMenu,
                RommMultiMenuItem.ResolveRefreshIconForMenu,
                RommMultiMenuItem.ResolvePropertiesIconForMenu);
        }
    }

    public abstract class RomMGameMenuActionPluginBase : IGameMenuItemPlugin
    {
        protected abstract string ActionCaption { get; }
        protected abstract string LogContext { get; }
        public abstract bool ShowInLaunchBox { get; }
        public abstract bool ShowInBigBox { get; }

        public bool SupportsMultipleGames => false;
        public string Caption => ActionCaption;
        public Image IconImage => null;

        public bool GetIsValidForGame(IGame selectedGame)
        {
            return RomMGameMenuActionProvider.IsActionVisible(selectedGame, ActionCaption, LogContext);
        }

        public bool GetIsValidForGames(IGame[] selectedGames)
        {
            return false;
        }

        public void OnSelected(IGame selectedGame)
        {
            RomMGameMenuActionProvider.Execute(selectedGame, ActionCaption, LogContext);
        }

        public void OnSelected(IGame[] selectedGames)
        {
        }
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxInstallMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => RommAdditionalApplicationService.InstallCaption;
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxUninstallMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => "Uninstall RomM Version...";
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxViewMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => "View on RomM";
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxPlayMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => "Play on RomM";
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxRefreshMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => "Refresh RomM State";
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }

    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMBigBoxPropertiesMenuItem : RomMGameMenuActionPluginBase
    {
        protected override string ActionCaption => "Properties";
        protected override string LogContext => "RomM BigBox";
        public override bool ShowInLaunchBox => false;
        public override bool ShowInBigBox => true;
    }
}
