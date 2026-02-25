using System.Windows;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.ViewModels;

namespace RomMbox.UI.Views
{
    /// <summary>
    /// Dialog that allows users to choose from multiple executable candidates.
    /// </summary>
    public partial class ExecutableSelectionDialog : Window
    {
        /// <summary>
        /// Initializes the dialog and applies custom chrome styling.
        /// </summary>
        public ExecutableSelectionDialog()
        {
            InitializeComponent();
            WindowChromeService.Apply(this, Title);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExecutableSelectionViewModel viewModel)
            {
                Title = viewModel.Title;
            }
        }
    }
}
