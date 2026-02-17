namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Defines a destination for log messages.
    /// </summary>
    internal interface ILogSink
    {
        /// <summary>
        /// Writes a log message to the sink.
        /// </summary>
        /// <param name="message">The log message to write.</param>
        void Write(LogMessage message);
    }
}
