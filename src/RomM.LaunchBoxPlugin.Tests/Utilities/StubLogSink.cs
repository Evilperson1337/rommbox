using System.Collections.Concurrent;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Utilities
{
    internal sealed class StubLogSink : ILogSink
    {
        private readonly ConcurrentQueue<LogMessage> _messages = new ConcurrentQueue<LogMessage>();

        public void Write(LogMessage message)
        {
            _messages.Enqueue(message);
        }

        public LogMessage[] Drain()
        {
            return _messages.ToArray();
        }
    }
}
