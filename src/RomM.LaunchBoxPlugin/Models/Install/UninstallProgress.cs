namespace RomMbox.Models.Install
{
    /// <summary>
    /// Progress payload used to report uninstall status.
    /// </summary>
    internal sealed class UninstallProgress
    {
        /// <summary>
        /// Creates a new uninstall progress payload.
        /// </summary>
        public UninstallProgress(string stage, string message, double percent, bool isIndeterminate)
        {
            Stage = stage ?? string.Empty;
            Message = message ?? string.Empty;
            Percent = percent;
            IsIndeterminate = isIndeterminate;
        }

        /// <summary>
        /// Gets the current stage label.
        /// </summary>
        public string Stage { get; }

        /// <summary>
        /// Gets the detailed message for the current operation.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the percentage complete (0-100).
        /// </summary>
        public double Percent { get; }

        /// <summary>
        /// Gets whether the progress should be indeterminate.
        /// </summary>
        public bool IsIndeterminate { get; }
    }
}
