using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public sealed class RomMPlayUrlServiceTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        [TestMethod]
        public void BuildPlayUrl_ReturnsExpectedUrl()
        {
            var service = new RomMPlayUrlService(new LoggingService(LogLevel.Debug, new NullSink()));
            var url = service.BuildPlayUrl("https://example.com/", "123");
            Assert.AreEqual("https://example.com/rom/123/ejs", url);
        }

        [TestMethod]
        public void BuildPlayUrl_ReturnsEmptyWhenInvalid()
        {
            var service = new RomMPlayUrlService(new LoggingService(LogLevel.Debug, new NullSink()));
            Assert.AreEqual(string.Empty, service.BuildPlayUrl(string.Empty, "123"));
            Assert.AreEqual(string.Empty, service.BuildPlayUrl("not-a-url", "123"));
            Assert.AreEqual(string.Empty, service.BuildPlayUrl("https://example.com", string.Empty));
        }
    }
}
