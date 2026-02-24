using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI;

/// <summary>
/// Dialog window that displays RomM game properties.
/// </summary>
public partial class GamePropertiesWindow : Window
{
    /// <summary>
    /// Initializes the dialog and applies custom chrome styling.
    /// </summary>
    public GamePropertiesWindow()
    {
        InitializeComponent();
        WindowChromeService.Apply(this, Title);
    }
}
