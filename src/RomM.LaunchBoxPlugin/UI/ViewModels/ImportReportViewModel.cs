using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RomMbox.Models.Import;
using RomMbox.UI.Infrastructure;
using RomMbox.UI.Models;

namespace RomMbox.UI.ViewModels
{
    /// <summary>
    /// View model that presents the import report summary and rows.
    /// </summary>
    public sealed class ImportReportViewModel : ObservableObject
    {
        /// <summary>
        /// Gets the report rows displayed in the dialog.
        /// </summary>
        public ObservableCollection<ImportReportRow> Rows { get; } = new ObservableCollection<ImportReportRow>();

        private string _summaryText = string.Empty;
        /// <summary>
        /// Gets or sets the summary text displayed at the top of the report.
        /// </summary>
        public string SummaryText { get => _summaryText; set => SetProperty(ref _summaryText, value); }

        /// <summary>
        /// Command that closes the report dialog.
        /// </summary>
        public RelayCommand CloseCommand { get; }

        /// <summary>
        /// Event raised when the dialog should be closed.
        /// </summary>
        public event Action RequestClose;

        /// <summary>
        /// Creates a report view model from the import report items.
        /// </summary>
        /// <param name="items">The import report items to display.</param>
        public ImportReportViewModel(IEnumerable<ImportReportItem> items)
        {
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            var rowList = items?.ToList() ?? new List<ImportReportItem>();
            foreach (var item in rowList)
            {
                Rows.Add(new ImportReportRow
                {
                    GameTitle = item.GameTitle,
                    StatusValue = item.Status,
                    Status = item.Status.ToString(),
                    Details = item.Details
                });
            }

            var successCount = rowList.Count(row => row.Status == ImportReportStatus.Success);
            var skippedCount = rowList.Count(row => row.Status == ImportReportStatus.Skipped);
            var failedCount = rowList.Count(row => row.Status == ImportReportStatus.Failed);
            SummaryText = $"Success: {successCount} | Skipped: {skippedCount} | Failed: {failedCount}";
        }
    }
}
