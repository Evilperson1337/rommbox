using System;

namespace RomMbox.Services
{
    /// <summary>
    /// Enumerates known error categories for RomM API operations.
    /// </summary>
    internal enum RommApiErrorType
    {
        AuthExpired,
        NotFound,
        RateLimited,
        ServerError,
        NetworkError,
        BadResponse
    }

    /// <summary>
    /// Domain-specific exception used to surface RomM API errors.
    /// </summary>
    internal sealed class RommApiException : Exception
    {
        /// <summary>
        /// Creates a new RomM API exception with a typed error classification.
        /// </summary>
        public RommApiException(string message, RommApiErrorType errorType, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorType = errorType;
        }

        /// <summary>
        /// The normalized error type used by callers to decide next steps.
        /// </summary>
        public RommApiErrorType ErrorType { get; }
    }
}
