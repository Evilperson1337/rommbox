using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RomMbox.UI.ViewModels;

namespace RomMbox.UI.Views;

/// <summary>
/// Hosts the connection configuration UI for RomM credentials.
/// </summary>
public partial class ConnectionView : UserControl
{
    /// <summary>
    /// Initializes the view and logs basic diagnostics for the data context.
    /// </summary>
    public ConnectionView()
    {
        Debug.WriteLine("[ConnectionView] Constructor called - initializing view");
        try
        {
            InitializeComponent();
            Debug.WriteLine("[ConnectionView] InitializeComponent() completed successfully");

            // Confirm the expected view model is wired up for troubleshooting.
            if (DataContext is ConnectionViewModel)
            {
                Debug.WriteLine("[ConnectionView] DataContext is ConnectionViewModel");
            }
            else
            {
                Debug.WriteLine($"[ConnectionView] DataContext is: {DataContext?.GetType().Name ?? "null"}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConnectionView] Error during initialization: {ex.Message}");
        }
    }

    /// <summary>
    /// Placeholder click handler retained for XAML wiring.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Button_Click(object sender, RoutedEventArgs e)
    {
    }
}
