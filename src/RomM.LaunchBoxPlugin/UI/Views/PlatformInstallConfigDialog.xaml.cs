using System;
using System.Windows;
using System.Windows.Forms;

namespace RomMbox.UI.Views;

    /// <summary>
    /// View for configuring platform-specific install behavior.
    /// </summary>
    public partial class PlatformInstallConfigDialog : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes the view.
        /// </summary>
        public PlatformInstallConfigDialog()
        {
            InitializeComponent();
        }

    /// <summary>
    /// Opens a folder picker for the games directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            BrowseForFolder("Select games directory", selectedPath =>
            {
                if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
                {
                    viewModel.GamesDirectory = selectedPath;
                }
            });
        }

    /// <summary>
    /// Opens a folder picker for the music root directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseMusicRoot_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select music root directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.MusicRootPath = selectedPath;
            }
        });
    }

    /// <summary>
    /// Opens a folder picker for the bonus content root directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseBonusRoot_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select bonus content root directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.BonusRootPath = selectedPath;
            }
        });
    }

    /// <summary>
    /// Opens a folder picker for the prerequisites root directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowsePreReqsRoot_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select pre-reqs root directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.PreReqsRootPath = selectedPath;
            }
        });
    }

    /// <summary>
    /// Displays a folder browser dialog and invokes the callback when a path is selected.
    /// </summary>
    /// <param name="description">Dialog description shown to the user.</param>
    /// <param name="onSelected">Callback invoked with the selected path.</param>
    private static void BrowseForFolder(string description, Action<string> onSelected)
    {
        using (var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        })
        {
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                onSelected?.Invoke(dialog.SelectedPath);
            }
        }
    }
}
