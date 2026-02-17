namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Query filters for RomM ROM listing requests.
    /// </summary>
    internal sealed class RommFilters
    {
        /// <summary>
        /// Free-text search string.
        /// </summary>
        public string Search { get; set; }
        /// <summary>
        /// MD5 hash filter.
        /// </summary>
        public string Md5 { get; set; }
        /// <summary>
        /// Region filter.
        /// </summary>
        public string Region { get; set; }
        /// <summary>
        /// Genre filter.
        /// </summary>
        public string Genre { get; set; }
    }
}
