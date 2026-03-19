using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using RomMbox.Plugin;
using RomMbox.Services.Auth;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.UI.Infrastructure;

namespace RomMbox.UI.ViewModels;

/// <summary>
/// View model that manages RomM connection settings and status.
/// </summary>
public sealed class ConnectionViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly LoggingService _logger;
    private readonly SettingsManager _settingsManager;
    private readonly AuthService _authService;
    private PluginSettings _settings;

    private string _serverAddress = "";
    private int _port = 443;
    private string _username = "";
    private string _password = "";
    private string _accessToken = "";
    private string _refreshToken = "";
    private bool _ignoreCertificate;
    private AuthMode _selectedAuthMode = AuthMode.Basic;

    private bool _isConnected;

    /// <summary>
    /// Creates the connection view model and loads persisted settings.
    /// </summary>
    /// <param name="shell">The main window view model shell.</param>
    public ConnectionViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        _logger = LoggingServiceFactory.Create();
        _settingsManager = new SettingsManager(_logger);
        _authService = new AuthService(_logger);

        LoadSettings();

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());
        SaveCommand = new RelayCommand(async () => await SaveAsync(showMessages: true), () => CanSaveConnection);
        OpenRommCommand = new RelayCommand(OpenRommInBrowser);
        StartSsoCommand = new RelayCommand(StartSsoSignIn);
        PluginEntry.BackgroundConnectionCompleted += OnBackgroundConnectionCompleted;
        UpdateStatus();
        CanSaveConnection = false;

        if (_settings.UseSavedCredentials && _settings.HasSavedCredentials)
        {
            if (!ApplyBackgroundConnectionResult())
            {
                _ = TryAutoConnectAsync();
            }
        }
    }

    /// <summary>
    /// Gets or sets the RomM server address or host.
    /// </summary>
    public string ServerAddress { get => _serverAddress; set => SetProperty(ref _serverAddress, value); }
    /// <summary>
    /// Gets or sets the RomM server port.
    /// </summary>
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    /// <summary>
    /// Gets or sets the username used for authentication.
    /// </summary>
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    /// <summary>
    /// Gets or sets the password used for authentication.
    /// </summary>
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    /// <summary>
    /// Gets or sets the pasted OIDC access token.
    /// </summary>
    public string AccessToken { get => _accessToken; set => SetProperty(ref _accessToken, value); }
    /// <summary>
    /// Gets or sets the pasted OIDC refresh token.
    /// </summary>
    public string RefreshToken { get => _refreshToken; set => SetProperty(ref _refreshToken, value); }
    /// <summary>
    /// Gets or sets whether TLS certificate errors should be ignored.
    /// </summary>
    public bool IgnoreCertificate { get => _ignoreCertificate; set => SetProperty(ref _ignoreCertificate, value); }

    /// <summary>
    /// Gets the supported auth mode options.
    /// </summary>
    public IReadOnlyList<AuthMode> AuthModeOptions { get; } = Enum.GetValues(typeof(AuthMode)).Cast<AuthMode>().ToArray();

    /// <summary>
    /// Gets or sets the selected auth mode.
    /// </summary>
    public AuthMode SelectedAuthMode
    {
        get => _selectedAuthMode;
        set
        {
            if (SetProperty(ref _selectedAuthMode, value))
            {
                RaisePropertyChanged(nameof(ShowBasicAuthFields));
                RaisePropertyChanged(nameof(ShowOidcFields));
            }
        }
    }

    public bool ShowBasicAuthFields => SelectedAuthMode == AuthMode.Basic;

    public bool ShowOidcFields => SelectedAuthMode == AuthMode.Oidc;

    /// <summary>
    /// Gets whether the connection is currently validated.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                UpdateStatus();
                _shell.IsConnected = value;
            }
        }
    }

    private string _statusText = "Status: Not Connected";
    /// <summary>
    /// Gets the status text displayed in the UI.
    /// </summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private Brush _statusBrush = Brushes.IndianRed;
    /// <summary>
    /// Gets the brush used for the status label.
    /// </summary>
    public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    private Brush _statusDotBrush = Brushes.IndianRed;
    /// <summary>
    /// Gets the brush used for the status indicator dot.
    /// </summary>
    public Brush StatusDotBrush { get => _statusDotBrush; private set => SetProperty(ref _statusDotBrush, value); }

    /// <summary>
    /// Command that tests the current connection settings.
    /// </summary>
    public RelayCommand TestConnectionCommand { get; }
    /// <summary>
    /// Command that saves settings after validating the connection.
    /// </summary>
    public RelayCommand SaveCommand { get; }
    /// <summary>
    /// Command that opens the RomM server in a browser.
    /// </summary>
    public RelayCommand OpenRommCommand { get; }
    /// <summary>
    /// Command that starts browser-based SSO against RomM.
    /// </summary>
    public RelayCommand StartSsoCommand { get; }

    private bool _canSaveConnection;
    public bool CanSaveConnection
    {
        get => _canSaveConnection;
        private set
        {
            if (SetProperty(ref _canSaveConnection, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                if (_shell is MainWindowViewModel mainWindow)
                {
                    mainWindow.SaveCommand.RaiseCanExecuteChanged();
                }
            }
        }
    }

    /// <summary>
    /// Tests connectivity against the configured server and updates UI state.
    /// </summary>
    private async Task TestConnectionAsync()
    {
        try
        {
            var url = BuildServerUrl();
            var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.ConnectionTimeoutSeconds));
            var result = SelectedAuthMode == AuthMode.Oidc
                ? await TestOidcConnectionInternalAsync(url, timeout).ConfigureAwait(false)
                : await _authService.TestConnectionAsync(url, Username, Password, timeout, IgnoreCertificate, CancellationToken.None).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsConnected = result.Status == ConnectionTestStatus.Success;
                StatusText = result.Message;
                StatusBrush = IsConnected ? new SolidColorBrush(Color.FromRgb(70, 220, 140)) : new SolidColorBrush(Color.FromRgb(255, 86, 86));
                StatusDotBrush = StatusBrush;
                CanSaveConnection = IsConnected;
                if (IsConnected && _settings.HasSavedCredentials && _settings.UseSavedCredentials && _shell is MainWindowViewModel mainWindow)
                {
                    mainWindow.IsServerConfigured = true;
                }
            });
            _logger.Info("Connection test completed.");
        }
        catch (Exception ex)
        {
            _logger.Error("Connection test failed.", ex);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                StatusText = "Connection failed.";
                StatusBrush = new SolidColorBrush(Color.FromRgb(255, 86, 86));
                StatusDotBrush = StatusBrush;
                CanSaveConnection = false;
                MessageBox.Show("Connection failed.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    /// <summary>
    /// Attempts an automatic connection using saved credentials.
    /// </summary>
    private async Task TryAutoConnectAsync()
    {
        try
        {
            var url = BuildServerUrl();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                return;
            }

            var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.ConnectionTimeoutSeconds));
            var result = SelectedAuthMode == AuthMode.Oidc
                ? await TestOidcConnectionInternalAsync(url, timeout).ConfigureAwait(false)
                : await _authService.TestConnectionAsync(url, Username, Password, timeout, IgnoreCertificate, CancellationToken.None).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsConnected = result.Status == ConnectionTestStatus.Success;
                StatusText = result.Message;
                StatusBrush = IsConnected ? new SolidColorBrush(Color.FromRgb(70, 220, 140)) : new SolidColorBrush(Color.FromRgb(255, 86, 86));
                StatusDotBrush = StatusBrush;
                CanSaveConnection = IsConnected;
                if (IsConnected && _settings.HasSavedCredentials && _settings.UseSavedCredentials && _shell is MainWindowViewModel mainWindow)
                {
                    mainWindow.IsServerConfigured = true;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Auto connect failed.", ex);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                StatusText = "Status: Not Connected";
                StatusBrush = new SolidColorBrush(Color.FromRgb(255, 86, 86));
                StatusDotBrush = StatusBrush;
                CanSaveConnection = false;
            });
        }
    }

    /// <summary>
    /// Applies the background connection test result once it completes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="result">The connection test result.</param>
    private void OnBackgroundConnectionCompleted(object sender, ConnectionTestResult result)
    {
        ApplyConnectionResult(result);
    }

    /// <summary>
    /// Attempts to apply any cached background connection result.
    /// </summary>
    /// <returns><c>true</c> if a cached result was applied.</returns>
    private bool ApplyBackgroundConnectionResult()
    {
        if (!PluginEntry.TryGetBackgroundConnectionResult(out var result))
        {
            return false;
        }

        ApplyConnectionResult(result);
        return true;
    }

    /// <summary>
    /// Updates UI state based on a connection test result.
    /// </summary>
    /// <param name="result">The connection test result.</param>
    private void ApplyConnectionResult(ConnectionTestResult result)
    {
        if (result == null)
        {
            return;
        }

        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsConnected = result.Status == ConnectionTestStatus.Success;
            StatusText = result.Message;
            StatusBrush = IsConnected ? new SolidColorBrush(Color.FromRgb(70, 220, 140)) : new SolidColorBrush(Color.FromRgb(255, 86, 86));
            StatusDotBrush = StatusBrush;
            CanSaveConnection = IsConnected;
            if (IsConnected && _settings.HasSavedCredentials && _settings.UseSavedCredentials && _shell is MainWindowViewModel mainWindow)
            {
                mainWindow.IsServerConfigured = true;
            }
        });
    }

    /// <summary>
    /// Saves connection settings after validating the connection.
    /// </summary>
    /// <param name="showMessages">Whether to show success or failure dialogs.</param>
    private async Task SaveAsync(bool showMessages)
    {
        try
        {
            if (!IsConnected)
            {
                await TestConnectionAsync().ConfigureAwait(false);
                if (!IsConnected)
                {
                    if (showMessages)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show("Test the connection before saving.", "RomM", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    return;
                }
            }

            var url = BuildServerUrl();
            _settings.ServerUrl = url;
            _settings.AllowInvalidTls = IgnoreCertificate;
            _settings.AuthModeName = SelectedAuthMode.ToString();
            _settings.HasSavedCredentials = SelectedAuthMode == AuthMode.Basic
                ? !string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password)
                : !string.IsNullOrWhiteSpace(AccessToken) || !string.IsNullOrWhiteSpace(RefreshToken);
            _settings.UseSavedCredentials = _settings.HasSavedCredentials;
            _settingsManager.Save(_settings);
            if (SelectedAuthMode == AuthMode.Oidc)
            {
                _settingsManager.DeleteSavedCredentials(url);
                _settingsManager.SaveOidcTokens(url, new OidcTokenInfo
                {
                    AccessToken = AccessToken ?? string.Empty,
                    RefreshToken = RefreshToken ?? string.Empty,
                    TokenType = "Bearer"
                });
            }
            else
            {
                _settingsManager.DeleteSavedOidcTokens(url);
                _settingsManager.SaveCredentials(url, Username, Password);
            }
            _logger.Info("Connection settings saved.");
            if (showMessages)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new RomMbox.UI.Views.InfoDialog("RomM", "Connection Details Saved")
                    {
                        Owner = Application.Current?.Windows
                            ?.OfType<MainWindow>()
                            .FirstOrDefault(),
                        Topmost = true
                    };
                    dialog.Activate();
                    dialog.ShowDialog();
                });
            }
            if (_shell != null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_shell is MainWindowViewModel mainWindow)
                    {
                        mainWindow.IsServerConfigured = true;
                    }
                    await _shell.Platforms.ReloadMappingsAsync().ConfigureAwait(false);
                    await _shell.Import.ReloadPlatformsAsync().ConfigureAwait(false);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save connection settings.", ex);
            if (showMessages)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Failed to save connection settings.", "RomM", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }

    /// <summary>
    /// Saves settings without showing dialogs.
    /// </summary>
    public Task SaveSilentlyAsync() => SaveAsync(showMessages: false);

    /// <summary>
    /// Loads persisted settings and credentials into the view model.
    /// </summary>
    private void LoadSettings()
    {
        _settings = _settingsManager.Load();
        SelectedAuthMode = _settings.GetAuthMode();
        var serverUrl = _settings.ServerUrl ?? string.Empty;
        if (SelectedAuthMode == AuthMode.Basic
            && string.IsNullOrWhiteSpace(serverUrl)
            && _settingsManager.TryGetAnySavedCredentials(out var discoveredUrl, out var discoveredCredentials))
        {
            _settings.ServerUrl = discoveredUrl;
            _settings.HasSavedCredentials = true;
            _settings.UseSavedCredentials = true;
            serverUrl = discoveredUrl;
            if (discoveredCredentials != null)
            {
                Username = discoveredCredentials.Username;
                Password = discoveredCredentials.Password;
            }
            _settingsManager.Save(_settings);
        }

        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            ServerAddress = uri.Host;
            Port = uri.Port;
        }
        else
        {
            ServerAddress = serverUrl;
        }

        if (Port <= 0)
        {
            Port = 443;
        }

        IgnoreCertificate = _settings.AllowInvalidTls;

        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            if (SelectedAuthMode == AuthMode.Oidc)
            {
                var tokens = _settingsManager.GetSavedOidcTokens(serverUrl);
                if (tokens != null)
                {
                    AccessToken = tokens.AccessToken;
                    RefreshToken = tokens.RefreshToken;
                }
            }
            else
            {
                var credentials = _settingsManager.GetSavedCredentials(serverUrl);
                if (credentials != null)
                {
                    Username = credentials.Username;
                    Password = credentials.Password;
                }
            }
        }
    }

    private async Task<ConnectionTestResult> TestOidcConnectionInternalAsync(string serverUrl, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(AccessToken) && string.IsNullOrWhiteSpace(RefreshToken))
        {
            return new ConnectionTestResult(ConnectionTestStatus.AuthenticationFailed, "Paste an access token or refresh token first.");
        }

        var existing = _settingsManager.GetSavedOidcTokens(serverUrl);
        try
        {
            _settingsManager.SaveOidcTokens(serverUrl, new OidcTokenInfo
            {
                AccessToken = AccessToken ?? string.Empty,
                RefreshToken = RefreshToken ?? string.Empty,
                TokenType = "Bearer"
            });

            var result = await _authService.TestOidcConnectionAsync(serverUrl, _settingsManager, timeout, IgnoreCertificate, CancellationToken.None).ConfigureAwait(false);
            var refreshed = _settingsManager.GetSavedOidcTokens(serverUrl);
            if (refreshed != null)
            {
                AccessToken = refreshed.AccessToken;
                RefreshToken = refreshed.RefreshToken;
            }

            return result;
        }
        finally
        {
            if (!IsConnected)
            {
                if (existing != null)
                {
                    _settingsManager.SaveOidcTokens(serverUrl, existing);
                }
                else
                {
                    _settingsManager.DeleteSavedOidcTokens(serverUrl);
                }
            }
        }
    }

    private void OpenRommInBrowser()
    {
        var serverUrl = BuildServerUrl();
        var launcher = new ExternalLauncherService(_logger);
        if (!launcher.TryOpenUrl(serverUrl))
        {
            MessageBox.Show("Failed to open RomM server URL in browser.", "RomM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartSsoSignIn()
    {
        var loginUrl = BuildServerUrl().TrimEnd('/') + "/api/login/openid";
        var launcher = new ExternalLauncherService(_logger);
        if (!launcher.TryOpenUrl(loginUrl))
        {
            MessageBox.Show("Failed to open RomM OIDC sign-in in the browser.", "RomM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Builds a fully qualified server URL from the configured address and port.
    /// </summary>
    private string BuildServerUrl()
    {
        var server = (ServerAddress ?? string.Empty).Trim();
        var portText = Port > 0 ? Port.ToString() : string.Empty;
        if (server.Contains("://"))
        {
            return server.TrimEnd('/');
        }

        var scheme = string.Equals(portText, "443", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        if (!string.IsNullOrWhiteSpace(portText))
        {
            return $"{scheme}://{server}:{portText}".TrimEnd('/');
        }

        return $"{scheme}://{server}".TrimEnd('/');
    }

    /// <summary>
    /// Updates the status text and brushes based on <see cref="IsConnected"/>.
    /// </summary>
    private void UpdateStatus()
    {
        if (IsConnected)
        {
            StatusText = "Status: Connected";
            StatusBrush = new SolidColorBrush(Color.FromRgb(70, 220, 140));
            StatusDotBrush = StatusBrush;
        }
        else
        {
            StatusText = "Status: Not Connected";
            StatusBrush = new SolidColorBrush(Color.FromRgb(255, 86, 86));
            StatusDotBrush = StatusBrush;
        }
    }
}
