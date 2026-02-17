using System.Windows;

namespace RomMbox.UI.Views
{
    /// <summary>
    /// Simple informational dialog with a single OK action.
    /// </summary>
    public partial class InfoDialog : Window
    {
        /// <summary>
        /// Creates an informational dialog with a title and message.
        /// </summary>
        /// <param name="title">Window title; defaults to "RomM" when empty.</param>
        /// <param name="message">Primary message shown to the user.</param>
        public InfoDialog(string title, string message)
        {
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(title) ? "RomM" : title;
            TitleText.Text = Title;
            MessageText.Text = message ?? string.Empty;
        }

        /// <summary>
        /// Handles the OK button click and closes the dialog.
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
