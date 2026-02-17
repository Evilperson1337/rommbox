using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Plugin;
using RomMbox.Services;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.GameMenu
{
    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RommPlayMenuItem : IGameMenuItemPlugin
    {
        /// <summary>
        /// Indicates whether multiple games can be handled at once (not in v1).
        /// </summary>
        public bool SupportsMultipleGames => false;

        /// <summary>
        /// Caption displayed for the game context menu entry.
        /// </summary>
        public string Caption => "RomM Play on RomM";

        /// <summary>
        /// Optional icon for the menu entry (unused in v1).
        /// </summary>
        public System.Drawing.Image IconImage => null;

        /// <summary>
        /// Hidden by default in LaunchBox until this flow is fully enabled.
        /// </summary>
        public bool ShowInLaunchBox => false;

        /// <summary>
        /// Hidden by default in Big Box until this flow is fully enabled.
        /// </summary>
        public bool ShowInBigBox => false;

        /// <summary>
        /// Determines whether this menu item is valid for a single game selection.
        /// </summary>
        /// <param name="selectedGame">The currently highlighted game.</param>
        /// <returns>Always false in v1.</returns>
        public bool GetIsValidForGame(IGame selectedGame)
        {
            return false;
        }

        /// <summary>
        /// Determines whether this menu item is valid for multi-select.
        /// </summary>
        /// <param name="selectedGames">The selected games.</param>
        /// <returns>Always false because multi-select is not supported.</returns>
        public bool GetIsValidForGames(IGame[] selectedGames) => false;

        /// <summary>
        /// Launches the RomM play URL for the selected game in the default browser.
        /// </summary>
        /// <param name="selectedGame">The game for which the menu was invoked.</param>
        public void OnSelected(IGame selectedGame)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    var installStateService = PluginEntry.InstallStateService;
                    if (installStateService == null)
                    {
                        PluginEntry.Logger?.Warning("InstallStateService is unavailable for Play on RomM.");
                        return;
                    }

                    // Use cached install metadata to construct the RomM play URL.
                    var details = installStateService.GetRomMDetails(selectedGame);
                    var urlService = new RomMPlayUrlService(PluginEntry.Logger);
                    var playUrl = urlService.BuildPlayUrl(details.ServerUrl, details.RommRomId);
                    if (string.IsNullOrWhiteSpace(playUrl))
                    {
                        PluginEntry.Logger?.Warning("Play URL could not be built for selected game.");
                        return;
                    }

                    PluginEntry.Logger?.Info($"Play on RomM selected for game '{selectedGame?.Title}'. URL={playUrl}");
                    var launcher = new ExternalLauncherService(PluginEntry.Logger);
                    if (!launcher.TryOpenUrl(playUrl))
                    {
                        PluginEntry.Logger?.Warning("Failed to launch browser for Play on RomM.");
                    }
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Play on RomM menu failed.", ex);
                }
            });
        }

        public void OnSelected(IGame[] selectedGames)
        {
        }
    }
}
