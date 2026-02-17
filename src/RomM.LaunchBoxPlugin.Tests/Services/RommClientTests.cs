using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public class RommClientTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }

        private static SettingsManager CreateSettingsManager(LoggingService logger, string serverUrl)
        {
            var manager = new SettingsManager(logger);
            var settings = manager.Load();
            settings.ServerUrl = serverUrl;
            settings.UseSavedCredentials = false;
            settings.HasSavedCredentials = false;
            manager.Save(settings);
            return manager;
        }

        [TestMethod]
        public void Constructor_WithInvalidServerUrl_ShouldThrow()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = new SettingsManager(logger);
            var settings = manager.Load();
            settings.ServerUrl = "not-a-url";
            manager.Save(settings);

            Assert.ThrowsException<InvalidOperationException>(() => new RommClient(logger, manager));
        }

        [TestMethod]
        public async Task ValidateSessionAsync_WithValidResponse_ShouldReturnTrue()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = CreateSettingsManager(logger, "https://romm.test");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var client = new HttpClient(handler);
            var rommClient = new RommClient(logger, manager, client);

            var result = await rommClient.ValidateSessionAsync(CancellationToken.None);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task ValidateSessionAsync_WithInvalidResponse_ShouldReturnFalse()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = CreateSettingsManager(logger, "https://romm.test");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var client = new HttpClient(handler);
            var rommClient = new RommClient(logger, manager, client);

            var result = await rommClient.ValidateSessionAsync(CancellationToken.None);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task ListPlatformsAsync_WithValidResponse_ShouldReturnPlatforms()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = CreateSettingsManager(logger, "https://romm.test");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\":1,\"name\":\"SNES\"}]")
            });
            var client = new HttpClient(handler);
            var rommClient = new RommClient(logger, manager, client);

            var result = await rommClient.ListPlatformsAsync(CancellationToken.None);

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public async Task ListPlatformsAsync_WithInvalidJson_ShouldThrowBadResponse()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = CreateSettingsManager(logger, "https://romm.test");
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json}")
            });
            var client = new HttpClient(handler);
            var rommClient = new RommClient(logger, manager, client);

            var ex = await Assert.ThrowsExceptionAsync<RommApiException>(() => rommClient.ListPlatformsAsync(CancellationToken.None));

            Assert.AreEqual(RommApiErrorType.BadResponse, ex.ErrorType);
        }
    }
}
