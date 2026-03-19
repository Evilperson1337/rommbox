using System.Text.Json.Serialization;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Token response returned by the RomM OAuth token endpoint.
    /// </summary>
    internal sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("expires")]
        public int Expires { get; set; }
    }
}
