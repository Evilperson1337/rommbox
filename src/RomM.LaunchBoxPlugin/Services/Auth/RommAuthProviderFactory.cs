using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Services.Auth
{
    /// <summary>
    /// Creates the correct auth provider for the currently configured mode.
    /// </summary>
    internal static class RommAuthProviderFactory
    {
        public static IRommAuthProvider Create(LoggingService logger, SettingsManager settingsManager, PluginSettings settings)
        {
            return settings.GetAuthMode() == AuthMode.Oidc
                ? new OidcRommAuthProvider(logger, settingsManager)
                : new BasicRommAuthProvider(settingsManager);
        }
    }
}
