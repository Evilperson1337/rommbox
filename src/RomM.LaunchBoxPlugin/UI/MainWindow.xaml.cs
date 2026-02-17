using System.Windows;
using RomMbox.UI.Infrastructure;
using Unbroken.LaunchBox.Plugins;

namespace RomMbox.UI;

/// <summary>
/// Root window for the plugin UI shell.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes the main window and applies custom chrome styling.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        WindowChromeService.Apply(this, Title);
        Closed += OnClosed;
    }

    /// <summary>
    /// Ensures LaunchBox refreshes any updated data when the window closes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnClosed(object sender, System.EventArgs e)
    {
        try
        {
            var dataManager = PluginHelper.DataManager;
            // Trigger a background reload first, then force sync to ensure library changes are visible.
            dataManager?.BackgroundReloadSave(() => { });
            dataManager?.ReloadIfNeeded();
            dataManager?.ForceReload();
        }
        catch
        {
            // Swallow errors on shutdown to avoid crashing the plugin host.
        }
    }
}
