using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI
{
    /// <summary>
    /// Window that displays progress for background install operations.
    /// </summary>
    public partial class InstallProgressWindow : Window
    {
        /// <summary>
        /// Initializes the progress window and applies custom chrome styling.
        /// </summary>
        public InstallProgressWindow()
        {
            InitializeComponent();
            WindowChromeService.Apply(this, Title);
        }
    }
}
