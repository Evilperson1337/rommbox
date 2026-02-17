namespace RomMbox.Models.Download
{
    /// <summary>
    /// Represents the outcome of a ROM download (and optional extraction).
    /// </summary>
    internal sealed class DownloadResult
    {
        /// <summary>
        /// True when the download completed successfully.
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// Path to the downloaded archive file.
        /// </summary>
        public string ArchivePath { get; set; }
        /// <summary>
        /// Path to extracted content when extraction is enabled.
        /// </summary>
        public string ExtractedPath { get; set; }
        /// <summary>
        /// Error message set when the download fails.
        /// </summary>
        public string ErrorMessage { get; set; }
        /// <summary>
        /// Install type inferred from the archive contents.
        /// </summary>
        public RomMbox.Models.Install.InstallType InstallType { get; set; } = RomMbox.Models.Install.InstallType.Unknown;
        /// <summary>
        /// Temporary root directory used during download/extraction.
        /// </summary>
        public string TempRoot { get; set; }
    }
}
