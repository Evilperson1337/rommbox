namespace RomMbox.Models.Import
{
    /// <summary>
    /// Progress payload for long-running import operations.
    /// </summary>
    internal sealed class ImportProgress
    {
        /// <summary>
        /// Total number of items expected (if known).
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Number of items processed so far.
        /// </summary>
        public int Processed { get; set; }

        /// <summary>
        /// Count of successful imports.
        /// </summary>
        public int Successful { get; set; }

        /// <summary>
        /// Count of skipped items (duplicates, ignored, etc.).
        /// </summary>
        public int Skipped { get; set; }

        /// <summary>
        /// Count of failed imports.
        /// </summary>
        public int Failed { get; set; }
    }
}
