using System;
using System.Collections.Generic;
using System.Threading;

namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Provides a simple logging API with level filtering.
    /// </summary>
    internal sealed class LoggingService
    {
        private readonly ILogSink _sink;
        private readonly object _sync = new object();
        private static readonly AsyncLocal<string> OperationIdScope = new AsyncLocal<string>();

        /// <summary>
        /// Creates a logging service with a minimum level and sink.
        /// </summary>
        /// <param name="minimumLevel">The minimum log level to emit.</param>
        /// <param name="sink">The sink that receives log messages.</param>
        public LoggingService(LogLevel minimumLevel, ILogSink sink)
        {
            MinimumLevel = minimumLevel;
            _sink = sink;
        }

        /// <summary>
        /// Gets the minimum level required to write log messages.
        /// </summary>
        public LogLevel MinimumLevel { get; }

        /// <summary>
        /// Starts a new operation scope and returns an IDisposable that restores the previous scope.
        /// </summary>
        /// <param name="operationId">The operation identifier to apply.</param>
        public IDisposable BeginOperation(string operationId)
        {
            var prior = OperationIdScope.Value;
            OperationIdScope.Value = operationId;
            return new OperationScope(prior);
        }

        /// <summary>
        /// Gets the current operation identifier, if any.
        /// </summary>
        public string CurrentOperationId => OperationIdScope.Value;

        /// <summary>
        /// Writes a debug-level message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Debug(string message)
        {
            Write(LogLevel.Debug, message, null);
        }

        /// <summary>
        /// Writes a trace-level message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Trace(string message)
        {
            Write(LogLevel.Trace, message, null);
        }

        /// <summary>
        /// Writes an info-level message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Info(string message)
        {
            Write(LogLevel.Info, message, null);
        }

        /// <summary>
        /// Writes a warning-level message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Warning(string message)
        {
            Write(LogLevel.Warning, message, null);
        }

        /// <summary>
        /// Writes a critical-level message with an exception.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to include.</param>
        public void Critical(string message, Exception exception)
        {
            Write(LogLevel.Critical, message, exception);
        }

        /// <summary>
        /// Writes a structured log message.
        /// </summary>
        public void Write(LogLevel level, string message, Exception exception, IReadOnlyDictionary<string, object> properties)
        {
            WriteInternal(level, message, exception, properties);
        }

        /// <summary>
        /// Writes a structured log message with a single property.
        /// </summary>
        public void Write(LogLevel level, string message, Exception exception, string propertyName, object propertyValue)
        {
            var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [propertyName] = propertyValue
            };
            WriteInternal(level, message, exception, properties);
        }

        /// <summary>
        /// Writes a structured log message with up to three properties.
        /// </summary>
        public void Write(
            LogLevel level,
            string message,
            Exception exception,
            string propertyName1,
            object propertyValue1,
            string propertyName2,
            object propertyValue2,
            string propertyName3,
            object propertyValue3)
        {
            var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [propertyName1] = propertyValue1,
                [propertyName2] = propertyValue2,
                [propertyName3] = propertyValue3
            };
            WriteInternal(level, message, exception, properties);
        }

        /// <summary>
        /// Writes an error-level message with an exception.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to include.</param>
        public void Error(string message, Exception exception)
        {
            Write(LogLevel.Error, message, exception);
        }

        /// <summary>
        /// Writes an error-level message without an exception.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Error(string message)
        {
            Write(LogLevel.Error, message, null);
        }

        /// <summary>
        /// Writes a log message if it meets the minimum level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">Optional exception details.</param>
        private void Write(LogLevel level, string message, Exception exception)
        {
            WriteInternal(level, message, exception, null);
        }

        private void WriteInternal(LogLevel level, string message, Exception exception, IReadOnlyDictionary<string, object> properties)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            var safeMessage = message ?? string.Empty;
            var logMessage = new LogMessage(DateTimeOffset.Now, level, safeMessage, exception, properties, OperationIdScope.Value);
            lock (_sync)
            {
                _sink.Write(logMessage);
            }
        }

        /// <summary>
        /// Creates a sanitized path for log output.
        /// </summary>
        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            var normalized = path.Replace('/', '\\');
            var marker = "\\Users\\";
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index + marker.Length;
                var next = normalized.IndexOf('\\', start);
                if (next > start)
                {
                    var prefix = normalized.Substring(0, start);
                    var suffix = normalized.Substring(next);
                    return prefix + "<redacted>" + suffix;
                }
            }

            return normalized;
        }

        /// <summary>
        /// Creates a sanitized URL string for log output.
        /// </summary>
        public static string SanitizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url ?? string.Empty;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Scheme + "://" + uri.Host;
            }

            return "<invalid-url>";
        }

        private sealed class OperationScope : IDisposable
        {
            private readonly string _prior;

            public OperationScope(string prior)
            {
                _prior = prior;
            }

            public void Dispose()
            {
                OperationIdScope.Value = _prior;
            }
        }
    }
}
