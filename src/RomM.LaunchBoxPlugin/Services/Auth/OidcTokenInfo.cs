using System;
using System.Runtime.Serialization;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Persisted OIDC token information used by the plugin.
    /// </summary>
    [DataContract]
    internal sealed class OidcTokenInfo
    {
        [DataMember(Name = "accessToken", EmitDefaultValue = false)]
        public string AccessToken { get; set; } = string.Empty;

        [DataMember(Name = "refreshToken", EmitDefaultValue = false)]
        public string RefreshToken { get; set; } = string.Empty;

        [DataMember(Name = "tokenType", EmitDefaultValue = false)]
        public string TokenType { get; set; } = "Bearer";

        [DataMember(Name = "expiresAtUtc", EmitDefaultValue = false)]
        public string ExpiresAtUtcText { get; set; } = string.Empty;

        public DateTimeOffset? GetExpiresAtUtc()
        {
            if (string.IsNullOrWhiteSpace(ExpiresAtUtcText))
            {
                return null;
            }

            return DateTimeOffset.TryParse(ExpiresAtUtcText, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        }

        public void SetExpiresAtUtc(DateTimeOffset? value)
        {
            ExpiresAtUtcText = value?.ToUniversalTime().ToString("O") ?? string.Empty;
        }

        public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

        public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

        public bool IsAccessTokenExpired(TimeSpan buffer)
        {
            var expiresAt = GetExpiresAtUtc();
            if (!expiresAt.HasValue)
            {
                return false;
            }

            return expiresAt.Value <= DateTimeOffset.UtcNow.Add(buffer);
        }

        public static OidcTokenInfo FromTokenResponse(TokenResponse response)
        {
            var tokenInfo = new OidcTokenInfo
            {
                AccessToken = response?.AccessToken ?? string.Empty,
                RefreshToken = response?.RefreshToken ?? string.Empty,
                TokenType = string.IsNullOrWhiteSpace(response?.TokenType) ? "Bearer" : response.TokenType
            };

            if (response != null && response.Expires > 0)
            {
                tokenInfo.SetExpiresAtUtc(DateTimeOffset.UtcNow.AddSeconds(response.Expires));
            }

            return tokenInfo;
        }
    }
}
