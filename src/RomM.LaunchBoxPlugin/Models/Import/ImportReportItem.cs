namespace RomMbox.Models.Import
{
    /// <summary>
    /// High-level status for a single import report entry.
    /// </summary>
    public enum ImportReportStatus
    {
        Success,
        Skipped,
        Failed
    }

    /// <summary>
    /// Summary row shown in the import report dialog.
    /// </summary>
    public sealed class ImportReportItem
    {
        /// <summary>
        /// Game title displayed in the report.
        /// </summary>
        public string GameTitle { get; set; } = string.Empty;

        /// <summary>
        /// Status of the import for this game.
        /// </summary>
        public ImportReportStatus Status { get; set; }

        /// <summary>
        /// Additional detail about success/failure/skip reason.
        /// </summary>
        public string Details { get; set; } = string.Empty;
    }
}
