namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Represents the outcome of a connection test to the RomM server.
    /// </summary>
    internal enum ConnectionTestStatus
    {
        /// <summary>
        /// Connection and authentication succeeded.
        /// </summary>
        Success,
        /// <summary>
        /// Credentials were rejected by the server.
        /// </summary>
        AuthenticationFailed,
        /// <summary>
        /// Connection failed due to network or server issues.
        /// </summary>
        ConnectionFailed
    }

    /// <summary>
    /// Result object containing status and user-facing message.
    /// </summary>
    internal sealed class ConnectionTestResult
    {
        /// <summary>
        /// Creates a new connection test result.
        /// </summary>
        /// <param name="status">The connection status.</param>
        /// <param name="message">The user-facing message.</param>
        public ConnectionTestResult(ConnectionTestStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        /// <summary>
        /// Gets the connection test status.
        /// </summary>
        public ConnectionTestStatus Status { get; }
        /// <summary>
        /// Gets the user-facing message describing the result.
        /// </summary>
        public string Message { get; }
    }
}
