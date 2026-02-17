using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Windows;
using System.Drawing;
using System.IO;
using RomMbox.Plugin;
using RomMbox.Services.Paths;
using Unbroken.LaunchBox.Plugins;
using RomMbox.UI;

namespace RomMbox.Plugin.Adapters.ToolsMenu
{
    /// <summary>
    /// Adds a RomM entry to the LaunchBox Tools menu and opens the plugin UI.
    /// </summary>
    [Export(typeof(ISystemMenuItemPlugin))]
    public sealed class RomMToolsMenuItem : ISystemMenuItemPlugin
    {
        /// <summary>
        /// Initializes the menu item and ensures plugin services are ready.
        /// </summary>
        public RomMToolsMenuItem()
        {
            try
            {
                PluginEntry.EnsureInitialized();
                PluginEntry.Logger?.Info("RomMToolsMenuItem instantiated.");
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Error("Failed to initialize RomMToolsMenuItem.", ex);
            }
        }

        public string Caption => "RomM";

        public System.Drawing.Image IconImage
        {
            get
            {
                try
                {
                    var pluginRoot = PluginPaths.GetPluginRootDirectory();
                    if (!string.IsNullOrWhiteSpace(pluginRoot))
                    {
                        var pluginAsset = Path.Combine(pluginRoot, "system", "assets", "romm.png");
                        var pluginImage = LoadImageFromFile(pluginAsset);
                        if (pluginImage != null)
                        {
                            return pluginImage;
                        }
                    }

                    var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
                    if (string.IsNullOrWhiteSpace(launchBoxRoot))
                    {
                        return null;
                    }

                    var path = Path.Combine(launchBoxRoot, "Images", "Media Packs", "Badges", "Nostalgic Platform Badges", "RomM.png");
                    return LoadImageFromFile(path);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static Image LoadImageFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                return (Image)image.Clone();
            }
            catch
            {
                return null;
            }
        }

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool AllowInBigBoxWhenLocked => false;

        /// <summary>
        /// Handles menu selection by opening the RomM WPF window.
        /// </summary>
        public void OnSelected()
        {
            PluginEntry.EnsureInitialized();
            try
            {
                PluginEntry.Logger?.Info("RomM Tools menu selected.");
                PluginEntry.Logger?.Info("RomM Tools initializing UI thread...");

                var currentApp = Application.Current;
                if (currentApp != null)
                {
                    PluginEntry.Logger?.Info("RomM Tools using existing WPF Application.");
                    currentApp.Dispatcher.BeginInvoke(new Action(() => ShowWpfWindow(currentApp, isExistingApp: true)));
                    return;
                }

                // Spin up a dedicated STA thread for the WPF UI when no Application is available.
                var uiThread = new Thread(() =>
                {
                    try
                    {
                        PluginEntry.Logger?.Info("RomM Tools UI thread started (WPF)." );
                        var app = new App();
                        app.InitializeComponent();
                        app.InitializeForPluginHost();
                        ShowWpfWindow(app, isExistingApp: false);
                        app.Run();
                    }
                    catch (Exception ex)
                    {
                        PluginEntry.Logger?.Error("RomM Tools dialog failed.", ex);
                    }
                });
                uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.IsBackground = true;
                uiThread.Start();
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Error("Error handling RomM Tools menu selection.", ex);
            }
        }

        /// <summary>
        /// Creates and shows the main WPF window for the plugin.
        /// </summary>
        private static void ShowWpfWindow(Application app, bool isExistingApp)
        {
            EnsureWpfResources(app);
            var window = app.MainWindow as MainWindow ?? new MainWindow();
            if (app.MainWindow == null)
            {
                app.MainWindow = window;
            }
            window.Closed += (closeSender, closeArgs) =>
            {
                PluginEntry.Logger?.Info("RomM Tools window closed.");
                if (!isExistingApp)
                {
                    app.Shutdown();
                }
            };
            PluginEntry.Logger?.Info("RomM Tools window created. Showing dialog...");
            window.Show();
        }

        /// <summary>
        /// Ensures the WPF resource dictionary is loaded once.
        /// </summary>
        private static void EnsureWpfResources(Application app)
        {
            if (app.Resources.Contains("BgBrush"))
            {
                return;
            }

            var dictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/RomMbox;component/UI/Resources.xaml", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}
