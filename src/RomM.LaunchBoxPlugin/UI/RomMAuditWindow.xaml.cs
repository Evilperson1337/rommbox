using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI;

/// <summary>
/// Window for platform-level RomM audits.
/// </summary>
public partial class RomMAuditWindow : Window
{
    public RomMAuditWindow()
    {
        InitializeComponent();
        WindowChromeService.Apply(this, Title);
        var viewModel = new ViewModels.RomMAuditViewModel();
        viewModel.CloseAction = Close;
        DataContext = viewModel;
    }
}
