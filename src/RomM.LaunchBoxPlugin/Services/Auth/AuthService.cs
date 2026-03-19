using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Handles authentication-related calls against the RomM server.
    /// </summary>
    internal sealed class AuthService
    {
        private readonly LoggingService _logger;

        /// <summary>
        /// Creates a new auth service with the provided logger.
        /// </summary>
        /// <param name="logger">Logging service for diagnostics.</param>
        public AuthService(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Tests connectivity and credentials against the RomM login endpoint.
        /// </summary>
        /// <param name="serverUrl">Base server URL.</param>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="password">Password to authenticate.</param>
        /// <param name="timeout">Timeout for the request.</param>
        /// <param name="allowInvalidTls">Whether to allow invalid TLS certificates.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A connection test result describing the outcome.</returns>
        public async Task<ConnectionTestResult> TestConnectionAsync(string serverUrl, string username, string password, TimeSpan timeout, bool allowInvalidTls, CancellationToken cancellationToken)
        {
            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("Server URL must not be empty.", nameof(serverUrl));
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("Server URL must be a valid absolute URL.", nameof(serverUrl));
            }

            var handler = new HttpClientHandler();
            if (allowInvalidTls)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            using (var httpClient = new HttpClient(handler))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(timeout);
                httpClient.Timeout = timeout;

                _logger?.Info($"Testing connection to '{LoggingService.SanitizeUrl(baseUri?.ToString() ?? string.Empty)}' with username '<redacted>'. AllowInvalidTls={allowInvalidTls}. Timeout={timeout.TotalSeconds:0}s.");

                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes((username ?? string.Empty) + ":" + (password ?? string.Empty)));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

                try
                {
                    var response = await httpClient.PostAsync(new Uri(baseUri, "/api/login"), new StringContent(string.Empty), cts.Token).ConfigureAwait(false);
                    var responseBody = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    _logger?.Debug($"Connection test response {(int)response.StatusCode} {response.ReasonPhrase}. Body={responseBody}.");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        _logger?.Info("Connection test succeeded.");
                        return new ConnectionTestResult(ConnectionTestStatus.Success, "Connection successful.");
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger?.Warning("Connection test authentication failed.");
                        return new ConnectionTestResult(ConnectionTestStatus.AuthenticationFailed, "Authentication failed.");
                    }

                    _logger?.Warning($"Connection test failed with status {(int)response.StatusCode}.");
                    return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection failed.");
                }
                catch (OperationCanceledException)
                {
                    _logger?.Error("Connection test timed out.");
                    return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection timed out.");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Connection test failed. {ex.GetType().Name}: {ex.Message}", ex);
                    return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection failed.");
                }
            }
        }

        /// <summary>
        /// Tests connectivity using saved OIDC bearer tokens.
        /// </summary>
        public async Task<ConnectionTestResult> TestOidcConnectionAsync(string serverUrl, SettingsManager settingsManager, TimeSpan timeout, bool allowInvalidTls, CancellationToken cancellationToken)
        {
            if (settingsManager == null)
            {
                throw new ArgumentNullException(nameof(settingsManager));
            }

            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("Server URL must be a valid absolute URL.", nameof(serverUrl));
            }

            var handler = new HttpClientHandler();
            if (allowInvalidTls)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            using var httpClient = new HttpClient(handler) { Timeout = timeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                var provider = new OidcRommAuthProvider(_logger, settingsManager, () =>
                {
                    var tokenHandler = new HttpClientHandler();
                    if (allowInvalidTls)
                    {
                        tokenHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                    }

                    return new HttpClient(tokenHandler) { Timeout = timeout };
                });

                await provider.PrepareAsync(httpClient, serverUrl, cts.Token).ConfigureAwait(false);
                var response = await httpClient.GetAsync(new Uri(baseUri, "/api/heartbeat"), cts.Token).ConfigureAwait(false);
                var responseBody = await SafeReadBodyAsync(response).ConfigureAwait(false);
                _logger?.Debug($"OIDC connection test response {(int)response.StatusCode} {response.ReasonPhrase}. Body={responseBody}.");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return new ConnectionTestResult(ConnectionTestStatus.Success, "Connection successful.");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new ConnectionTestResult(ConnectionTestStatus.AuthenticationFailed, "Authentication failed.");
                }

                return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection failed.");
            }
            catch (OperationCanceledException)
            {
                return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection timed out.");
            }
            catch (RommApiException ex) when (ex.ErrorType == RommApiErrorType.AuthExpired)
            {
                return new ConnectionTestResult(ConnectionTestStatus.AuthenticationFailed, "Authentication failed.");
            }
            catch (Exception ex)
            {
                _logger?.Error($"OIDC connection test failed. {ex.GetType().Name}: {ex.Message}", ex);
                return new ConnectionTestResult(ConnectionTestStatus.ConnectionFailed, "Connection failed.");
            }
        }

        /// <summary>
        /// Safely reads and truncates an HTTP response body for logging.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <returns>A readable response body string or a placeholder.</returns>
        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
        {
            if (response?.Content == null)
            {
                return "<no body>";
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    return "<empty body>";
                }

                body = body.Trim();
                return body.Length > 500 ? body.Substring(0, 500) + "..." : body;
            }
            catch (Exception ex)
            {
                return $"<failed to read body: {ex.GetType().Name}>";
            }
        }
    }
}
