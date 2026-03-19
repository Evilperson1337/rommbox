using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Applies bearer token authentication using OIDC-derived RomM tokens.
    /// </summary>
    internal sealed class OidcRommAuthProvider : IRommAuthProvider
    {
        private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private readonly Func<HttpClient> _tokenClientFactory;

        public OidcRommAuthProvider(LoggingService logger, SettingsManager settingsManager, Func<HttpClient> tokenClientFactory = null)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _tokenClientFactory = tokenClientFactory ?? (() => new HttpClient());
        }

        public async Task PrepareAsync(HttpClient httpClient, string serverUrl, CancellationToken cancellationToken)
        {
            var settings = _settingsManager.Load();
            if (!settings.UseSavedCredentials || !settings.HasSavedCredentials)
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            var tokens = _settingsManager.GetSavedOidcTokens(serverUrl);
            if (tokens == null || (!tokens.HasAccessToken && !tokens.HasRefreshToken))
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            if (!tokens.HasAccessToken || tokens.IsAccessTokenExpired(RefreshWindow))
            {
                tokens = await RefreshTokensAsync(serverUrl, tokens, cancellationToken).ConfigureAwait(false);
            }

            if (tokens == null || !tokens.HasAccessToken)
            {
                throw new RommApiException("Authentication failed.", RommApiErrorType.AuthExpired);
            }

            var scheme = string.IsNullOrWhiteSpace(tokens.TokenType) ? "Bearer" : tokens.TokenType;
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, tokens.AccessToken);
        }

        private async Task<OidcTokenInfo> RefreshTokensAsync(string serverUrl, OidcTokenInfo tokens, CancellationToken cancellationToken)
        {
            if (tokens == null || !tokens.HasRefreshToken)
            {
                return tokens;
            }

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var latest = _settingsManager.GetSavedOidcTokens(serverUrl) ?? tokens;
                if (latest.HasAccessToken && !latest.IsAccessTokenExpired(RefreshWindow))
                {
                    return latest;
                }

                using var client = _tokenClientFactory();
                var payload = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = latest.RefreshToken ?? string.Empty
                });

                var response = await client.PostAsync(new Uri(new Uri(serverUrl), "/api/token"), payload, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.Warning($"OIDC token refresh failed with status {(int)response.StatusCode}. Body={body}");
                    throw new RommApiException("Authentication failed.", RommApiErrorType.AuthExpired);
                }

                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var refreshed = OidcTokenInfo.FromTokenResponse(tokenResponse);
                if (!refreshed.HasRefreshToken)
                {
                    refreshed.RefreshToken = latest.RefreshToken;
                }

                _settingsManager.SaveOidcTokens(serverUrl, refreshed);
                return refreshed;
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }
}
