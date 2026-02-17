namespace RomMbox.Models.Import
{
    /// <summary>
    /// Describes a potential duplicate match between a RomM ROM and a LaunchBox game.
    /// </summary>
    internal sealed class RommMatchCandidate
    {
        /// <summary>
        /// RomM platform identifier for the candidate ROM.
        /// </summary>
        public string PlatformId { get; set; }
        /// <summary>
        /// RomM ROM identifier.
        /// </summary>
        public string RommId { get; set; }
        /// <summary>
        /// RomM ROM title shown to the user.
        /// </summary>
        public string RommTitle { get; set; }
        /// <summary>
        /// LaunchBox game identifier for the potential match.
        /// </summary>
        public string LaunchBoxGameId { get; set; }
        /// <summary>
        /// LaunchBox game title for the potential match.
        /// </summary>
        public string LaunchBoxTitle { get; set; }
        /// <summary>
        /// Matching strategy used (e.g., Id, Md5, Title).
        /// </summary>
        public string Strategy { get; set; }
        /// <summary>
        /// Human-readable confidence label.
        /// </summary>
        public string Confidence { get; set; }
    }
}
