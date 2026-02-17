namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Log severity levels used by the plugin.
    /// </summary>
    internal enum LogLevel
    {
        /// <summary>
        /// Extremely verbose diagnostic messages.
        /// </summary>
        Trace = 0,
        /// <summary>
        /// Verbose diagnostic messages.
        /// </summary>
        Debug = 1,
        /// <summary>
        /// Informational messages.
        /// </summary>
        Info = 2,
        /// <summary>
        /// Warnings that indicate unexpected but recoverable conditions.
        /// </summary>
        Warning = 3,
        /// <summary>
        /// Error messages indicating failures.
        /// </summary>
        Error = 4,
        /// <summary>
        /// Critical failures that prevent the plugin from continuing.
        /// </summary>
        Critical = 5
    }
}
