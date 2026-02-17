using System;
using System.Collections.Generic;

namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Represents a log entry emitted by the plugin.
    /// </summary>
    internal sealed class LogMessage
    {
        /// <summary>
        /// Creates a new log message entry.
        /// </summary>
        /// <param name="timestamp">The time the entry was created.</param>
        /// <param name="level">The log level.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">Optional exception details.</param>
        public LogMessage(DateTimeOffset timestamp, LogLevel level, string message, Exception exception)
            : this(timestamp, level, message, exception, null, null)
        {
        }

        /// <summary>
        /// Creates a new log message entry with structured metadata.
        /// </summary>
        /// <param name="timestamp">The time the entry was created.</param>
        /// <param name="level">The log level.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">Optional exception details.</param>
        /// <param name="properties">Optional structured properties.</param>
        /// <param name="operationId">Optional correlation identifier.</param>
        public LogMessage(
            DateTimeOffset timestamp,
            LogLevel level,
            string message,
            Exception exception,
            IReadOnlyDictionary<string, object> properties,
            string operationId)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            Exception = exception;
            Properties = properties;
            OperationId = operationId;
        }

        /// <summary>
        /// Gets the timestamp when the log entry was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; }
        /// <summary>
        /// Gets the log level of the entry.
        /// </summary>
        public LogLevel Level { get; }
        /// <summary>
        /// Gets the message body of the entry.
        /// </summary>
        public string Message { get; }
        /// <summary>
        /// Gets the exception details if present.
        /// </summary>
        public Exception Exception { get; }
        /// <summary>
        /// Gets structured properties for the log entry.
        /// </summary>
        public IReadOnlyDictionary<string, object> Properties { get; }
        /// <summary>
        /// Gets the correlation identifier for the log entry.
        /// </summary>
        public string OperationId { get; }
    }
}
