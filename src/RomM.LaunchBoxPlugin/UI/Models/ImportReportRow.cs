using RomMbox.Models.Import;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.Models
{
    /// <summary>
    /// Represents a row in the import report UI.
    /// </summary>
    public sealed class ImportReportRow : ObservableObject
    {
        private string _gameTitle = string.Empty;
        /// <summary>
        /// Gets or sets the game title associated with the report row.
        /// </summary>
        public string GameTitle { get => _gameTitle; set => SetProperty(ref _gameTitle, value); }

        private string _status = string.Empty;
        /// <summary>
        /// Gets or sets the human-readable status text.
        /// </summary>
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private string _details = string.Empty;
        /// <summary>
        /// Gets or sets additional detail text for the report row.
        /// </summary>
        public string Details { get => _details; set => SetProperty(ref _details, value); }

        /// <summary>
        /// Gets or sets the structured status value used for styling and filtering.
        /// </summary>
        public ImportReportStatus StatusValue { get; set; }
    }
}
