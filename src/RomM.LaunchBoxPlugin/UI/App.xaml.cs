using System.Windows;

namespace RomMbox.UI;

/// <summary>
/// WPF application entry point used by the plugin-hosted UI shell.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets or sets whether the app is running inside the LaunchBox plugin host.
    /// </summary>
    public static bool IsPluginHost { get; set; }

    /// <summary>
    /// Prepares the app for plugin-hosted execution by deferring shutdown and ensuring a main window.
    /// </summary>
    public void InitializeForPluginHost()
    {
        IsPluginHost = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (MainWindow == null)
        {
            MainWindow = new MainWindow();
        }
    }
}
