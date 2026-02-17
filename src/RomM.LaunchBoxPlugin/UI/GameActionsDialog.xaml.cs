using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI;

/// <summary>
/// Dialog window that hosts game action commands for a selected title.
/// </summary>
public partial class GameActionsDialog : Window
{
    /// <summary>
    /// Initializes the dialog and applies custom chrome styling.
    /// </summary>
    public GameActionsDialog()
    {
        InitializeComponent();
        WindowChromeService.Apply(this, Title);
    }
}
