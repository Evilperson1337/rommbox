using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI
{
    /// <summary>
    /// Window that displays progress for background uninstall operations.
    /// </summary>
    public partial class UninstallProgressWindow : Window
    {
        /// <summary>
        /// Initializes the progress window and applies custom chrome styling.
        /// </summary>
        public UninstallProgressWindow()
        {
            InitializeComponent();
            WindowChromeService.Apply(this, Title);
        }
    }
}
