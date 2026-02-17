namespace RomMbox.Models.Install
{
    /// <summary>
    /// Describes how an install should be handled after download.
    /// </summary>
    public enum InstallScenario
    {
        /// <summary>
        /// Basic: use archive/extracted path directly.
        /// </summary>
        Basic = 0,
        /// <summary>
        /// Enhanced: search for a target import file within extracted content.
        /// </summary>
        Enhanced = 1,
        /// <summary>
        /// Installer: run an installer to produce a final install path.
        /// </summary>
        Installer = 2
    }
}
