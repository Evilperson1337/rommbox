using System;
using System.ComponentModel.Composition;
using System.Windows;
using RomMbox.Plugin;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.GameMenu
{
    [Export(typeof(IGameMenuItemPlugin))]
    public sealed class RomMForceBadgeMenuItem : IGameMenuItemPlugin
    {
        public bool SupportsMultipleGames => false;

        public string Caption => "RomM Debug: Force Badge Match";

        public System.Drawing.Image IconImage => null;

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool GetIsValidForGame(IGame selectedGame)
        {
            return selectedGame != null;
        }

        public bool GetIsValidForGames(IGame[] selectedGames)
        {
            return false;
        }

        public void OnSelected(IGame selectedGame)
        {
            PluginEntry.EnsureInitialized();

            if (selectedGame == null)
            {
                PluginEntry.Logger?.Warning("[RomM Badge] Force badge requested with null game.");
                return;
            }

            try
            {
                var dataManager = PluginHelper.DataManager;
                if (dataManager == null)
                {
                    PluginEntry.Logger?.Warning($"[RomM Badge] Force badge failed. DataManager unavailable. GameId={selectedGame.Id ?? string.Empty} Title=\"{selectedGame.Title ?? string.Empty}\".");
                    MessageBox.Show(Application.Current?.MainWindow, "LaunchBox data manager is unavailable.", "RomM", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                PluginEntry.Logger?.Info($"[RomM Badge] Force badge requested. GameId={selectedGame.Id ?? string.Empty} Title=\"{selectedGame.Title ?? string.Empty}\" SourceBefore=\"{selectedGame.Source ?? string.Empty}\".");

                selectedGame.Source = "RomM";

                dataManager.Save(true);
                dataManager.ReloadIfNeeded();

                PluginEntry.Logger?.Info($"[RomM Badge] Force badge applied. GameId={selectedGame.Id ?? string.Empty} Title=\"{selectedGame.Title ?? string.Empty}\" SourceAfter=\"{selectedGame.Source ?? string.Empty}\".");
                MessageBox.Show(Application.Current?.MainWindow, "RomM badge force flag applied by setting Source to RomM. Refresh the game list if needed.", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Error($"[RomM Badge] Force badge failed for GameId={selectedGame.Id ?? string.Empty} Title=\"{selectedGame.Title ?? string.Empty}\".", ex);
                MessageBox.Show(Application.Current?.MainWindow, "Failed to force the RomM badge match.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnSelected(IGame[] selectedGames)
        {
        }
    }
}
