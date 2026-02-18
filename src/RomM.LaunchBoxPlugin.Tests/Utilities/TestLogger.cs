using RomMbox.Services.Logging;

namespace RomMbox.Tests.Utilities
{
    internal static class TestLogger
    {
        public static LoggingService Create(LogLevel level = LogLevel.Debug)
        {
            return new LoggingService(level, new StubLogSink());
        }
    }
}
