using System;
using System.Globalization;
using System.IO;
using System.Text;
using RomMbox.Services.Paths;

namespace RomMbox.Services.Logging
{
    /// <summary>
    /// Writes log messages to a file on disk.
    /// </summary>
    internal sealed class FileLogSink : ILogSink
    {
        private readonly object _lock = new object();
        private readonly string _path;

        /// <summary>
        /// Creates a file log sink with the specified output path.
        /// </summary>
        /// <param name="path">The log file path.</param>
        public FileLogSink(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Writes a log message to the file, swallowing errors to avoid crashing the host.
        /// </summary>
        /// <param name="message">The log message to write.</param>
        public void Write(LogMessage message)
        {
            if (message == null)
            {
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("RomMbox log: " + message.Message);
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = FormatMessage(message);
                lock (_lock)
                {
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never crash the plugin.
            }
        }

        /// <summary>
        /// Ensures the log directory exists and writes an initialization entry.
        /// </summary>
        public void EnsureInitialized()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = FormatMessage(new LogMessage(DateTimeOffset.Now, LogLevel.Info, "Log initialized.", null));
                lock (_lock)
                {
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Formats a log message into a single line string.
        /// </summary>
        /// <param name="message">The message to format.</param>
        /// <returns>The formatted message string.</returns>
        private static string FormatMessage(LogMessage message)
        {
            var timestamp = message.Timestamp.ToString("O", CultureInfo.InvariantCulture);
            var baseLine = $"[{timestamp}] [{message.Level}]";
            if (!string.IsNullOrWhiteSpace(message.OperationId))
            {
                baseLine += " [OpId=" + message.OperationId + "]";
            }

            baseLine += " " + message.Message;
            if (message.Properties != null && message.Properties.Count > 0)
            {
                baseLine += " | ";
                var first = true;
                foreach (var entry in message.Properties)
                {
                    if (!first)
                    {
                        baseLine += ", ";
                    }
                    first = false;
                    baseLine += entry.Key + "=" + entry.Value;
                }
            }
            if (message.Exception == null)
            {
                return baseLine;
            }

            return baseLine + " | Exception: " + message.Exception;
        }

        /// <summary>
        /// Creates the default file log sink using the plugin log path.
        /// </summary>
        /// <returns>A file log sink targeting the default log file.</returns>
        public static FileLogSink CreateDefault()
        {
            return new FileLogSink(PluginPaths.GetLogPath());
        }
    }
}
