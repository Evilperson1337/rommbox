using System.Windows;

namespace RomMbox.UI.Views
{
    /// <summary>
    /// Simple yes/no confirmation dialog with optional details.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        /// <summary>
        /// Creates a confirmation dialog with a title, message, and optional detail text.
        /// </summary>
        /// <param name="title">Window title; defaults to "RomM" when empty.</param>
        /// <param name="message">Primary message shown to the user.</param>
        /// <param name="detail">Optional detail text (can be empty).</param>
        public ConfirmDialog(string title, string message, string detail)
        {
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(title) ? "RomM" : title;
            TitleText.Text = Title;
            MessageText.Text = message ?? string.Empty;
            DetailText.Text = detail ?? string.Empty;
        }

        /// <summary>
        /// Handles the Yes button click and closes the dialog with a <c>true</c> result.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private void OnYesClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handles the No button click and closes the dialog with a <c>false</c> result.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private void OnNoClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
