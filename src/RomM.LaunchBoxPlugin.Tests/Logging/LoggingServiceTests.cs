using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Logging
{
    [TestClass]
    public class LoggingServiceTests
    {
        private sealed class MemorySink : ILogSink
        {
            public List<LogMessage> Messages { get; } = new List<LogMessage>();

            public void Write(LogMessage message)
            {
                Messages.Add(message);
            }
        }

        [TestMethod]
        public void Filters_By_Minimum_Level()
        {
            var sink = new MemorySink();
            var logger = new LoggingService(LogLevel.Warning, sink);

            logger.Debug("debug");
            logger.Info("info");
            logger.Warning("warn");
            logger.Error("error", new InvalidOperationException("boom"));

            Assert.AreEqual(2, sink.Messages.Count);
            Assert.AreEqual(LogLevel.Warning, sink.Messages[0].Level);
            Assert.AreEqual(LogLevel.Error, sink.Messages[1].Level);
        }

        [TestMethod]
        public void Includes_Exception_In_Error()
        {
            var sink = new MemorySink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var exception = new InvalidOperationException("boom");

            logger.Error("failed", exception);

            Assert.AreEqual(1, sink.Messages.Count);
            Assert.AreEqual(exception, sink.Messages[0].Exception);
        }

    }
}
