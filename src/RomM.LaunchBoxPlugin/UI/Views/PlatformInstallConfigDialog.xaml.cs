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
    /// Opens a folder picker for the PS3 games directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowsePs3Games_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select PS3 games directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.Ps3GameDirectory = selectedPath;
            }
        });
    }

    /// <summary>
    /// Opens a file picker for the RPCS3 executable.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseRpcs3Executable_Click(object sender, RoutedEventArgs e)
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Select RPCS3 executable",
            Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        })
        {
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
                {
                    viewModel.Rpcs3ExecutablePath = dialog.FileName;
                }
            }
        }
    }

    /// <summary>
    /// Opens a folder picker for the RPCS3 license directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseRpcs3License_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select RPCS3 license directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.Rpcs3LicenseDirectory = selectedPath;
            }
        });
    }

    /// <summary>
    /// Opens a folder picker for the PS4 games directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowsePs4Games_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder("Select PS4 games directory", selectedPath =>
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                viewModel.Ps4GamesDirectory = selectedPath;
            }
        });
    }

    /// <summary>
    /// Opens a file picker for the ShadPS4 executable.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowseShadPs4Executable_Click(object sender, RoutedEventArgs e)
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Select ShadPS4 executable",
            Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        })
        {
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
                {
                    viewModel.ShadPs4ExecutablePath = dialog.FileName;
                }
            }
        }
    }

    /// <summary>
    /// Opens a file picker for the optional PS4 external PKG extractor.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void BrowsePs4ExternalExtractor_Click(object sender, RoutedEventArgs e)
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Select PS4 external PKG extractor",
            Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        })
        {
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
                {
                    viewModel.Ps4ExternalPkgExtractorPath = dialog.FileName;
                }
            }
        }
    }

    private void BrowsePcsx2Executable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select PCSX2 executable", vm => vm?.Pcsx2ExecutablePath ?? string.Empty, (vm, path) => vm.Pcsx2ExecutablePath = path);

    private void BrowsePpssppExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select PPSSPP executable", vm => vm?.PpssppExecutablePath ?? string.Empty, (vm, path) => vm.PpssppExecutablePath = path);

    private void BrowseRetroArchExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select RetroArch executable", vm => vm?.RetroArchExecutablePath ?? string.Empty, (vm, path) => vm.RetroArchExecutablePath = path);

    private void BrowseVita3kExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select Vita3K executable", vm => vm?.Vita3kExecutablePath ?? string.Empty, (vm, path) => vm.Vita3kExecutablePath = path);

    private void BrowseSwitchEdenExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select Eden executable", vm => vm?.SwitchEdenExecutablePath ?? string.Empty, (vm, path) => vm.SwitchEdenExecutablePath = path);

    private void BrowseAzaharExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select Azahar executable", vm => vm?.AzaharExecutablePath ?? string.Empty, (vm, path) => vm.AzaharExecutablePath = path);

    private void BrowseAzaharPlusExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select AzaharPlus executable", vm => vm?.AzaharPlusExecutablePath ?? string.Empty, (vm, path) => vm.AzaharPlusExecutablePath = path);

    private void BrowseDolphinExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select Dolphin executable", vm => vm?.DolphinExecutablePath ?? string.Empty, (vm, path) => vm.DolphinExecutablePath = path);

    private void BrowseCemuExecutable_Click(object sender, RoutedEventArgs e) => BrowseForExecutable("Select Cemu executable", vm => vm?.CemuExecutablePath ?? string.Empty, (vm, path) => vm.CemuExecutablePath = path);

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

    private void BrowseForExecutable(string title, Func<ViewModels.PlatformInstallConfigViewModel, string> currentValueAccessor, Action<ViewModels.PlatformInstallConfigViewModel, string> setter)
    {
        using (var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        })
        {
            if (DataContext is ViewModels.PlatformInstallConfigViewModel viewModel)
            {
                var currentValue = currentValueAccessor?.Invoke(viewModel);
                if (!string.IsNullOrWhiteSpace(currentValue))
                {
                    dialog.FileName = currentValue;
                }
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && DataContext is ViewModels.PlatformInstallConfigViewModel vm)
            {
                setter?.Invoke(vm, dialog.FileName);
            }
        }
    }
}
