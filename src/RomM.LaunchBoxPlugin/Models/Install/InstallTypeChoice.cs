namespace RomMbox.Models.Install
{
    /// <summary>
    /// Represents the user-selected install type for a platform.
    /// </summary>
    public enum InstallTypeChoice
    {
        /// <summary>
        /// Basic install for emulator platforms.
        /// </summary>
        Basic = 0,
        /// <summary>
        /// Enhanced install for Windows platforms.
        /// </summary>
        Enhanced = 1
    }
}
