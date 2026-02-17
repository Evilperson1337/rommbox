namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// Determines how archives are extracted after download.
    /// </summary>
    public enum ExtractionBehavior
    {
        /// <summary>
        /// Extract into a subfolder named after the archive.
        /// </summary>
        Subfolder = 0,
        /// <summary>
        /// Extract directly into the target directory.
        /// </summary>
        Direct = 1,
        /// <summary>
        /// Do not extract the archive.
        /// </summary>
        None = 2
    }
}
