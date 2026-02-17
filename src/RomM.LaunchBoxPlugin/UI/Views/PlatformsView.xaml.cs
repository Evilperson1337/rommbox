using System.Windows.Controls;

namespace RomMbox.UI.Views;

/// <summary>
/// Hosts the platform mapping and configuration UI.
/// </summary>
public partial class PlatformsView : UserControl
{
    /// <summary>
    /// Initializes the platforms view.
    /// </summary>
    public PlatformsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Placeholder selection handler retained for XAML wiring.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection changed event arguments.</param>
    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}
