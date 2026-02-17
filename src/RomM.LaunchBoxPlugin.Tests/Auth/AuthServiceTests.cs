using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services.Auth;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Auth
{
    [TestClass]
    public class AuthServiceTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        [TestMethod]
        public async Task TestConnectionAsync_WithEmptyServerUrl_ShouldThrowArgumentException()
        {
            var service = new AuthService(new LoggingService(LogLevel.Debug, new NullSink()));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.TestConnectionAsync("", "user", "pass", TimeSpan.FromSeconds(1), false, CancellationToken.None));
        }

        [TestMethod]
        public async Task TestConnectionAsync_WithInvalidUrlFormat_ShouldThrowArgumentException()
        {
            var service = new AuthService(new LoggingService(LogLevel.Debug, new NullSink()));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.TestConnectionAsync("not-a-url", "user", "pass", TimeSpan.FromSeconds(1), false, CancellationToken.None));
        }
    }
}
