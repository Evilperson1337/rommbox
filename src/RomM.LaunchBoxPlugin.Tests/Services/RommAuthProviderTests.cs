using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomMbox.Services.Auth;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class RommAuthProviderTests
    {
        [Fact]
        public async Task BasicProvider_ShouldSetBasicAuthorizationHeader_FromSavedCredentials()
        {
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);
            var settings = settingsManager.Load();
            settings.ServerUrl = "https://basic-auth-test.local";
            settings.AuthModeName = nameof(AuthMode.Basic);
            settings.HasSavedCredentials = true;
            settings.UseSavedCredentials = true;
            settingsManager.Save(settings);
            settingsManager.SaveCredentials(settings.ServerUrl, "alice", "secret");

            try
            {
                var provider = new BasicRommAuthProvider(settingsManager);
                using var client = new HttpClient();

                await provider.PrepareAsync(client, settings.ServerUrl, CancellationToken.None);

                client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
                client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Basic");
            }
            finally
            {
                settingsManager.DeleteSavedCredentials(settings.ServerUrl);
            }
        }

        [Fact]
        public async Task OidcProvider_ShouldRefreshExpiredToken_AndSetBearerHeader()
        {
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var settings = settingsManager.Load();
            settings.ServerUrl = "https://oidc-auth-test.local";
            settings.AuthModeName = nameof(AuthMode.Oidc);
            settings.HasSavedCredentials = true;
            settings.UseSavedCredentials = true;
            settingsManager.Save(settings);

            var expired = new OidcTokenInfo
            {
                AccessToken = "expired-token",
                RefreshToken = "refresh-token",
                TokenType = "Bearer"
            };
            expired.SetExpiresAtUtc(DateTimeOffset.UtcNow.AddMinutes(-5));
            settingsManager.SaveOidcTokens(settings.ServerUrl, expired);

            try
            {
                var provider = new OidcRommAuthProvider(logger, settingsManager, () => new HttpClient(new StubTokenHandler()));
                using var client = new HttpClient();

                await provider.PrepareAsync(client, settings.ServerUrl, CancellationToken.None);

                client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
                client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
                client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("fresh-access-token");

                var refreshed = settingsManager.GetSavedOidcTokens(settings.ServerUrl);
                refreshed.Should().NotBeNull();
                refreshed!.AccessToken.Should().Be("fresh-access-token");
                refreshed.RefreshToken.Should().Be("fresh-refresh-token");
            }
            finally
            {
                settingsManager.DeleteSavedOidcTokens(settings.ServerUrl);
            }
        }

        private sealed class StubTokenHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"fresh-access-token\",\"refresh_token\":\"fresh-refresh-token\",\"token_type\":\"Bearer\",\"expires\":3600}")
                };

                return Task.FromResult(response);
            }
        }
    }
}
