using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Views
{
    /// <summary>
    /// Dialog window that displays the results of an import operation.
    /// </summary>
    public partial class ImportReportDialog : Window
    {
        /// <summary>
        /// Initializes the dialog and applies custom chrome styling.
        /// </summary>
        public ImportReportDialog()
        {
            InitializeComponent();
            WindowChromeService.Apply(this, Title);
        }
    }
}
