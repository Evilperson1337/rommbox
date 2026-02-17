using System;

using System.Collections.Generic;

namespace RomMbox.Models.Import
{
    /// <summary>
    /// Aggregates counters and reporting details for a single import run.
    /// </summary>
    internal sealed class ImportResult
    {
        /// <summary>
        /// Total ROMs expected to be processed (if known).
        /// </summary>
        public int TotalRoms { get; set; }
        /// <summary>
        /// Count of successfully imported games.
        /// </summary>
        public int SuccessfulImports { get; set; }
        /// <summary>
        /// Count of skipped duplicates.
        /// </summary>
        public int SkippedDuplicates { get; set; }
        /// <summary>
        /// Count of failed imports.
        /// </summary>
        public int FailedImports { get; set; }
        /// <summary>
        /// Total time spent on the import run.
        /// </summary>
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// Possible matches for user review when duplicates are detected.
        /// </summary>
        public List<RommMatchCandidate> MatchCandidates { get; } = new List<RommMatchCandidate>();
        /// <summary>
        /// Per-ROM reporting items for the import report dialog.
        /// </summary>
        public List<ImportReportItem> ReportItems { get; } = new List<ImportReportItem>();

        /// <summary>
        /// Total processed count derived from success/skip/fail buckets.
        /// </summary>
        public int Processed => SuccessfulImports + SkippedDuplicates + FailedImports;
    }
}
