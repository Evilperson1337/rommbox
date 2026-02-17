namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// Lightweight mapping entry used by UI or helper logic.
    /// </summary>
    internal sealed class PlatformMappingEntry
    {
        /// <summary>
        /// RomM platform identifier.
        /// </summary>
        public string RommPlatformId { get; set; }
        /// <summary>
        /// RomM platform display name.
        /// </summary>
        public string RommPlatformName { get; set; }
        /// <summary>
        /// LaunchBox platform name to map to.
        /// </summary>
        public string LaunchBoxPlatformName { get; set; }
        /// <summary>
        /// True if the mapping was auto-detected.
        /// </summary>
        public bool AutoMapped { get; set; }
        /// <summary>
        /// True if the platform is excluded from auto import.
        /// </summary>
        public bool IsExcluded { get; set; }
        /// <summary>
        /// Whether to extract downloads for this platform.
        /// </summary>
        public bool ExtractAfterDownload { get; set; }
        /// <summary>
        /// Extraction behavior for this mapping.
        /// </summary>
        public ExtractionBehavior ExtractionBehavior { get; set; }
        /// <summary>
        /// Derived status label used by the UI.
        /// </summary>
        public string MappingStatus => string.IsNullOrWhiteSpace(LaunchBoxPlatformName) ? "Unmapped" : "Mapped";
    }
}
