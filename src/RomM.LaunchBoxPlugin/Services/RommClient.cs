using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Download;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services
{
    /// <summary>
    /// HTTP client wrapper for the RomM API. Handles authentication, retries, and
    /// strongly typed serialization/deserialization for responses.
    /// </summary>
    internal sealed class RommClient : IRommClient
    {
        private readonly LoggingService _logger;
        private readonly SettingsManager _settingsManager;
        private HttpClient _httpClient;
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private static int _sharedTimeoutSeconds;
        private readonly string _serverUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly bool _requireServerUrl;
        private string _lastAuthToken;

        /// <summary>
        /// Creates the RomM API client.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="settingsManager">Settings provider for server URL and auth.</param>
        /// <param name="httpClient">Optional HTTP client for testing or customization.</param>
        /// <param name="requireServerUrl">If true, missing server URL throws an error.</param>
        public RommClient(LoggingService logger, SettingsManager settingsManager, HttpClient httpClient = null, bool requireServerUrl = true)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _requireServerUrl = requireServerUrl;
            var settings = settingsManager.Load();
            _serverUrl = settings.ServerUrl?.TrimEnd('/') ?? string.Empty;
            if (_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                throw new InvalidOperationException("Server URL is not configured.");
            }

            _httpClient = httpClient ?? SharedHttpClient;
            var timeoutSeconds = Math.Max(1, settings.ConnectionTimeoutSeconds);
            if (ReferenceEquals(_httpClient, SharedHttpClient))
            {
                if (_sharedTimeoutSeconds == 0)
                {
                    _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    _sharedTimeoutSeconds = timeoutSeconds;
                }
            }
            else
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            }
            if (settings.AllowInvalidTls)
            {
                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
                _httpClient = new HttpClient(handler);
                _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            }

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Validates the session by calling the RomM heartbeat endpoint.
        /// </summary>
        public async Task<bool> ValidateSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
                {
                    return false;
                }
                await EnsureAuthenticatedAsync().ConfigureAwait(false);
                var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), "/api/heartbeat"), cancellationToken).ConfigureAwait(false);
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger?.Error("ValidateSessionAsync failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// Lists available RomM platforms.
        /// </summary>
        public async Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return new List<RommPlatform>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), "/api/platforms"), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<RommPlatform>>(json, _jsonOptions) ?? new List<RommPlatform>();
        }

        /// <summary>
        /// Lists ROMs for a specific RomM platform with paging and optional filters.
        /// </summary>
        public async Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return new PagedResult<RommRom> { Items = new List<RommRom>() };
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("PlatformId is required.", nameof(platformId));
            }

            var offset = Math.Max(0, (page - 1) * pageSize);
            var query = new List<string> { $"limit={pageSize}", $"offset={offset}" };
            if (!string.IsNullOrWhiteSpace(filters?.Search)) query.Add("search=" + Uri.EscapeDataString(filters.Search));
            if (!string.IsNullOrWhiteSpace(filters?.Md5)) query.Add("md5=" + Uri.EscapeDataString(filters.Md5));
            if (!string.IsNullOrWhiteSpace(filters?.Region)) query.Add("region=" + Uri.EscapeDataString(filters.Region));
            if (!string.IsNullOrWhiteSpace(filters?.Genre)) query.Add("genre=" + Uri.EscapeDataString(filters.Genre));

            query.Add("platform_ids=" + Uri.EscapeDataString(platformId));
            var url = $"/api/roms?{string.Join("&", query)}";
            _logger?.Debug($"ListRomsByPlatformAsync request: platformId={platformId}, page={page}, pageSize={pageSize}, offset={offset}, url={url}");
            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), url), cancellationToken).ConfigureAwait(false);
            _logger?.Debug($"ListRomsByPlatformAsync response: status={(int)response.StatusCode} {response.StatusCode} for platformId={platformId}, page={page}");
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<PagedResult<RommRom>>(json, _jsonOptions) ?? new PagedResult<RommRom> { Items = new List<RommRom>() };
            _logger?.Debug($"ListRomsByPlatformAsync parsed: platformId={platformId}, page={page}, items={result.Items?.Count ?? 0}, total={(result.Total.HasValue ? result.Total.Value.ToString() : "<null>")}, pageSize={result.PageSize}");
            return result;
        }

        /// <summary>
        /// Retrieves full ROM details by RomM id.
        /// </summary>
        public async Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return null;
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), $"/api/roms/{Uri.EscapeDataString(romId)}"), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<RommRom>(json, _jsonOptions);
        }

        /// <summary>
        /// Retrieves download payload information for a ROM.
        /// </summary>
        public async Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return null;
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), $"/api/roms/{Uri.EscapeDataString(romId)}/download"), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<RommPayload>(json, _jsonOptions);
        }

        /// <summary>
        /// Downloads the raw payload data referenced by a RomM payload object.
        /// </summary>
        public async Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return Array.Empty<byte>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (payload == null || string.IsNullOrWhiteSpace(payload.DownloadUrl))
            {
                throw new ArgumentException("Payload download URL is required.", nameof(payload));
            }

            var response = await _httpClient.GetAsync(payload.DownloadUrl, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads ROM content by RomM id and filename.
        /// </summary>
        public async Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return Array.Empty<byte>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(romId) || string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Rom ID and file name are required to download content.");
            }

            var path = $"/api/roms/{Uri.EscapeDataString(romId)}/content/{Uri.EscapeDataString(fileName)}";
            if (!string.IsNullOrWhiteSpace(fileIds))
            {
                path += $"?file_ids={Uri.EscapeDataString(fileIds)}";
            }

            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), path), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads media (cover/screenshot) from a RomM URL.
        /// </summary>
        public async Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return Array.Empty<byte>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                throw new ArgumentException("Media URL is required.", nameof(mediaUrl));
            }

            var response = await _httpClient.GetAsync(mediaUrl, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Lists save files for a ROM (optionally scoped by platform).
        /// </summary>
        public async Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return Array.Empty<RommSave>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);

            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(romId)) query.Add("rom_id=" + Uri.EscapeDataString(romId));
            if (!string.IsNullOrWhiteSpace(platformId)) query.Add("platform_id=" + Uri.EscapeDataString(platformId));
            var url = query.Count > 0 ? $"/api/saves?{string.Join("&", query)}" : "/api/saves";
            _logger?.Debug($"ListSavesAsync request: romId={romId ?? "<null>"}, platformId={platformId ?? "<null>"}, url={url}");

            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), url), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<RommSave>>(json, _jsonOptions) ?? new List<RommSave>();
        }

        /// <summary>
        /// Retrieves a single save entry by id.
        /// </summary>
        public async Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return null;
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), $"/api/saves/{saveId}"), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<RommSave>(json, _jsonOptions);
        }

        /// <summary>
        /// Downloads save content from a RomM download path or URL.
        /// </summary>
        public async Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return Array.Empty<byte>();
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(downloadPath))
            {
                throw new ArgumentException("Save download path is required.", nameof(downloadPath));
            }

            if (Uri.TryCreate(downloadPath, UriKind.Absolute, out var absolute))
            {
                var response = await _httpClient.GetAsync(absolute, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(response).ConfigureAwait(false);
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }

            var responseRelative = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), downloadPath), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(responseRelative).ConfigureAwait(false);
            return await responseRelative.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a save file to RomM, trying common field names for compatibility.
        /// </summary>
        public async Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return null;
            }
            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(romId))
            {
                throw new ArgumentException("Rom ID is required.", nameof(romId));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Save file not found.", filePath);
            }

            var query = new List<string> { "rom_id=" + Uri.EscapeDataString(romId) };
            if (!string.IsNullOrWhiteSpace(emulator))
            {
                query.Add("emulator=" + Uri.EscapeDataString(emulator));
            }

            var url = $"/api/saves?{string.Join("&", query)}";
            var fileName = Path.GetFileName(filePath);
            var fieldNames = new[] { "saveFile", "file", "save_file", "save", "files" };

            foreach (var fieldName in fieldNames)
            {
                _logger?.Debug($"UploadSaveAsync request: romId={romId ?? "<null>"}, emulator={emulator ?? "<null>"}, url={url}, field={fieldName}, file={filePath}");

                using var content = new MultipartFormDataContent();
                await using var stream = File.OpenRead(filePath);
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, fieldName, fileName);

                var response = await _httpClient.PostAsync(new Uri(new Uri(_serverUrl), url), content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonSerializer.Deserialize<RommSave>(json, _jsonOptions);
                }

                if ((int)response.StatusCode == 400)
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (body.IndexOf("No save file provided", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _logger?.Warning($"UploadSaveAsync retrying with different field name. Field={fieldName}, Response={body}");
                        continue;
                    }

                    throw new RommApiException($"Unexpected response: 400 {body}", RommApiErrorType.BadResponse);
                }

                await EnsureSuccessAsync(response).ConfigureAwait(false);
            }

            throw new RommApiException("Upload failed: server did not accept provided save field names.", RommApiErrorType.BadResponse);
        }

        /// <summary>
        /// Downloads ROM content along with the total size if provided by the server.
        /// </summary>
        public async Task<(byte[] Data, long? TotalBytes)> DownloadRomContentWithLengthAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
        {
            return await DownloadRomContentWithLengthAsync(romId, fileName, fileIds, cancellationToken, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads ROM content and reports incremental progress.
        /// </summary>
        public async Task<(byte[] Data, long? TotalBytes)> DownloadRomContentWithLengthAsync(
            string romId,
            string fileName,
            string fileIds,
            CancellationToken cancellationToken,
            IProgress<RomMbox.Models.Download.DownloadProgress> progress)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return (Array.Empty<byte>(), 0);
            }

            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(romId) || string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Rom ID and file name are required to download content.");
            }

            var path = $"/api/roms/{Uri.EscapeDataString(romId)}/content/{Uri.EscapeDataString(fileName)}";
            if (!string.IsNullOrWhiteSpace(fileIds))
            {
                path += $"?file_ids={Uri.EscapeDataString(fileIds)}";
            }

            using var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), path), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);

            var totalBytes = response.Content.Headers.ContentLength;
            progress?.Report(new RomMbox.Models.Download.DownloadProgress(0, totalBytes));

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            int bytesRead;
            long received = 0;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await memoryStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                received += bytesRead;
                progress?.Report(new RomMbox.Models.Download.DownloadProgress(received, totalBytes));
            }

            return (memoryStream.ToArray(), totalBytes ?? received);
        }

        /// <summary>
        /// Streams ROM content to disk while reporting progress.
        /// </summary>
        public async Task<long?> DownloadRomContentToFileAsync(
            string romId,
            string fileName,
            string fileIds,
            string destinationPath,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress)
        {
            if (!_requireServerUrl && (string.IsNullOrWhiteSpace(_serverUrl) || !Uri.IsWellFormedUriString(_serverUrl, UriKind.Absolute)))
            {
                return 0;
            }

            await EnsureAuthenticatedAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(romId) || string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Rom ID and file name are required to download content.");
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));
            }

            var path = $"/api/roms/{Uri.EscapeDataString(romId)}/content/{Uri.EscapeDataString(fileName)}";
            if (!string.IsNullOrWhiteSpace(fileIds))
            {
                path += $"?file_ids={Uri.EscapeDataString(fileIds)}";
            }

            using var response = await _httpClient.GetAsync(new Uri(new Uri(_serverUrl), path), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);

            var totalBytes = response.Content.Headers.ContentLength;
            progress?.Report(new DownloadProgress(0, totalBytes));

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[81920];
            int bytesRead;
            long received = 0;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                received += bytesRead;
                progress?.Report(new DownloadProgress(received, totalBytes));
            }

            return totalBytes ?? received;
        }

        Task<long?> IRommClient.DownloadRomContentToFileAsync(
            string romId,
            string fileName,
            string fileIds,
            string destinationPath,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress)
        {
            return DownloadRomContentToFileAsync(romId, fileName, fileIds, destinationPath, cancellationToken, progress);
        }

        /// <summary>
        /// Ensures Basic Auth headers are set using saved credentials.
        /// </summary>
        private async Task EnsureAuthenticatedAsync()
        {
            var settings = _settingsManager.Load();
            if (!settings.UseSavedCredentials || !settings.HasSavedCredentials)
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            var credentials = _settingsManager.GetSavedCredentials(_serverUrl);
            if (credentials == null)
            {
                throw new RommApiException("Missing credentials.", RommApiErrorType.AuthExpired);
            }

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials.Username + ":" + credentials.Password));
            if (!string.Equals(_lastAuthToken, token, StringComparison.Ordinal))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                _lastAuthToken = token;
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Throws a domain-specific exception when the response is not successful.
        /// </summary>
        private async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode == 401 || statusCode == 403)
            {
                throw new RommApiException("Authentication failed.", RommApiErrorType.AuthExpired);
            }

            if (statusCode == 404)
            {
                throw new RommApiException("Not found.", RommApiErrorType.NotFound);
            }

            if (statusCode == 429)
            {
                throw new RommApiException("Rate limited.", RommApiErrorType.RateLimited);
            }

            if (statusCode >= 500)
            {
                throw new RommApiException("Server error.", RommApiErrorType.ServerError);
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new RommApiException($"Unexpected response: {statusCode} {body}", RommApiErrorType.BadResponse);
        }
    }
}
