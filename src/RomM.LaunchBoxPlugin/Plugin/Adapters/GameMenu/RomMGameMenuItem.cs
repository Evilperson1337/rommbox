using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.GameActions;
using RomMbox.UI;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.GameMenu
{
    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMGameMenuItem : IGameMenuItemPlugin
    {
        /// <summary>
        /// Indicates whether this menu entry supports multi-select operations.
        /// </summary>
        public bool SupportsMultipleGames => false;

        /// <summary>
        /// Display caption for the context menu item in LaunchBox/Big Box.
        /// </summary>
        public string Caption => "RomM";

        /// <summary>
        /// Optional icon for the menu entry (unused in v1).
        /// </summary>
        public System.Drawing.Image IconImage => null;

        /// <summary>
        /// We currently surface game actions through other UI entry points.
        /// </summary>
        public bool ShowInLaunchBox => false;

        /// <summary>
        /// Big Box integration for the game menu is not enabled in v1.
        /// </summary>
        public bool ShowInBigBox => false;

        /// <summary>
        /// Determines whether this item should appear for a given game.
        /// </summary>
        /// <param name="selectedGame">Game currently highlighted in the UI.</param>
        /// <returns>Always false in v1 to keep the item hidden.</returns>
        public bool GetIsValidForGame(IGame selectedGame)
        {
            return false;
        }

        /// <summary>
        /// Determines whether this item should appear for a game selection set.
        /// </summary>
        /// <param name="selectedGames">Selected games in the UI.</param>
        /// <returns>Always false because multi-select is not supported.</returns>
        public bool GetIsValidForGames(IGame[] selectedGames)
        {
            return false;
        }

        /// <summary>
        /// Handles single-game selection by building a game action context and
        /// opening the action dialog on the UI thread.
        /// </summary>
        /// <param name="selectedGame">The game for which the menu was invoked.</param>
        public void OnSelected(IGame selectedGame)
        {
            PluginEntry.EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    PluginEntry.Logger?.Info($"RomM game menu selected for game: {selectedGame?.Title}");
                    var installService = PluginEntry.InstallStateService;
                    if (installService == null)
                    {
                        PluginEntry.Logger?.Warning("InstallStateService unavailable while opening game actions dialog.");
                        return;
                    }

                    // Build the data needed by the dialog (install state, play URL, etc.).
                    var urlService = new RomMPlayUrlService(PluginEntry.Logger);
                    var contextBuilder = new GameActionsContextBuilder(installService, urlService, PluginEntry.Logger);
                    var context = contextBuilder.BuildAsync(selectedGame, CancellationToken.None).GetAwaiter().GetResult();
                    if (context == null)
                    {
                        PluginEntry.Logger?.Warning("Failed to build game action context.");
                        return;
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // WPF dialogs must be created/shown on the UI thread.
                        var dialog = new GameActionsDialog();
                        var viewModel = new GameActionsDialogViewModel(context, PluginEntry.Logger)
                        {
                            CloseAction = dialog.Close
                        };
                        dialog.DataContext = viewModel;
                        dialog.Owner = Application.Current?.MainWindow;
                        dialog.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    PluginEntry.Logger?.Error("Error handling RomM game menu selection.", ex);
                }
            });
        }

        public void OnSelected(IGame[] selectedGames)
        {
            // Not supported in v1.
        }
    }
}
