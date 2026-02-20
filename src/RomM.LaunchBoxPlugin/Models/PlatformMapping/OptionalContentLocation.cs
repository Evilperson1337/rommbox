namespace RomMbox.Models.PlatformMapping
{
    /// <summary>
    /// Describes where optional content should be installed.
    /// </summary>
    public enum OptionalContentLocation
    {
        /// <summary>
        /// Install into a centralized root path.
        /// </summary>
        Centralized = 0,
        /// <summary>
        /// Install into the game folder (per-game).
        /// </summary>
        GameFolder = 1
    }
}
