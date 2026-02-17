namespace RomMbox.Models.Install
{
    /// <summary>
    /// Determines how installers should be invoked.
    /// </summary>
    public enum InstallerMode
    {
        /// <summary>
        /// Attempt to run Inno Setup installers silently.
        /// </summary>
        AutoInnoSilent = 0,
        /// <summary>
        /// Require manual installation.
        /// </summary>
        Manual = 1
    }
}
