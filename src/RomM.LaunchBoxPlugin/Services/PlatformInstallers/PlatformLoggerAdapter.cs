using System;
using System.Collections.Generic;
using RomM.Platforms.Abstractions.Logging;
using RomMbox.Services.Logging;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformLoggerAdapter : IPlatformLogger
    {
        private readonly LoggingService _logger;

        public PlatformLoggerAdapter(LoggingService logger)
        {
            _logger = logger;
        }

        public void Write(PlatformLogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
        {
            if (_logger == null)
            {
                return;
            }

            var mapped = level switch
            {
                PlatformLogLevel.Trace => LogLevel.Trace,
                PlatformLogLevel.Debug => LogLevel.Debug,
                PlatformLogLevel.Info => LogLevel.Info,
                PlatformLogLevel.Warning => LogLevel.Warning,
                PlatformLogLevel.Error => LogLevel.Error,
                PlatformLogLevel.Critical => LogLevel.Critical,
                _ => LogLevel.Info
            };

            if (properties != null && properties.Count > 0)
            {
                var converted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in properties)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    converted[pair.Key] = pair.Value ?? string.Empty;
                }
                _logger.Write(mapped, message ?? string.Empty, exception, converted);
                return;
            }

            _logger.Write(mapped, message ?? string.Empty, exception, (IReadOnlyDictionary<string, object>)null);
        }
    }
}
