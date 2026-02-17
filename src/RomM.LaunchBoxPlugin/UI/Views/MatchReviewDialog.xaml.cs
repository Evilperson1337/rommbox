using System.Windows;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Views
{
    /// <summary>
    /// Dialog that allows users to review and resolve match candidates.
    /// </summary>
    public partial class MatchReviewDialog : Window
    {
        /// <summary>
        /// Initializes the dialog and applies custom chrome styling.
        /// </summary>
        public MatchReviewDialog()
        {
            InitializeComponent();
            WindowChromeService.Apply(this, Title);
        }
    }
}
