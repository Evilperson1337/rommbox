namespace RomMbox.Services.Install
{
    /// <summary>
    /// Represents the outcome of resolving a local install directory.
    /// </summary>
    internal sealed class InstallLocationResult
    {
        /// <summary>
        /// True when a valid install location was found or chosen.
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// Absolute path to the install directory, when available.
        /// </summary>
        public string InstallDirectory { get; set; }
        /// <summary>
        /// Human-readable message describing the result or failure reason.
        /// </summary>
        public string Message { get; set; }
    }
}
