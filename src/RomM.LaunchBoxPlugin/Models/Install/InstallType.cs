namespace RomMbox.Models.Install
{
    /// <summary>
    /// Represents the inferred type of install package.
    /// </summary>
    internal enum InstallType
    {
        /// <summary>
        /// Could not determine the install type.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Portable install (no installer required).
        /// </summary>
        Portable = 1,
        /// <summary>
        /// Installer-based package (e.g., setup.exe).
        /// </summary>
        Installer = 2
    }
}
