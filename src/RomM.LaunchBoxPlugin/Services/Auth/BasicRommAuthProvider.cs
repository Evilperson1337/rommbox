using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Settings;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Applies Basic authentication using stored RomM credentials.
    /// </summary>
    internal sealed class BasicRommAuthProvider : IRommAuthProvider
    {
        private readonly SettingsManager _settingsManager;
        private string _lastAuthToken;

        public BasicRommAuthProvider(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public Task PrepareAsync(HttpClient httpClient, string serverUrl, CancellationToken cancellationToken)
        {
            var settings = _settingsManager.Load();
            if (!settings.UseSavedCredentials || !settings.HasSavedCredentials)
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            var credentials = _settingsManager.GetSavedCredentials(serverUrl);
            if (credentials == null)
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes((credentials.Username ?? string.Empty) + ":" + (credentials.Password ?? string.Empty)));
            if (!string.Equals(_lastAuthToken, token, StringComparison.Ordinal))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                _lastAuthToken = token;
            }

            return Task.CompletedTask;
        }
    }
}
