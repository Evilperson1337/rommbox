using RomMbox.Services.Settings;
using RomMbox.Services.PlatformInstallers;

namespace RomMbox.Services
{
    /// <summary>
    /// Determines whether a platform can be played via RomM based on known support lists
    /// and optional user mappings.
    /// </summary>
    internal static class RommPlayability
    {
        /// <summary>
        /// Returns true when the given platform name is known to be playable on RomM.
        /// </summary>
        public static bool IsPlayablePlatform(string platformName)
        {
            return RommPlayEndpointResolver.Resolve(platformName).IsPlayableOnRomM;
        }

        /// <summary>
        /// Resolves the LaunchBox platform name (if configured) and checks playability.
        /// </summary>
        public static bool IsPlayablePlatform(string rommPlatformId, string rommPlatformName, SettingsManager settingsManager)
        {
            return RommPlayEndpointResolver.Resolve(rommPlatformId, rommPlatformName, settingsManager, null).IsPlayableOnRomM;
        }

        public static bool IsPlayablePlatform(string rommPlatformId, string rommPlatformName, SettingsManager settingsManager, PlatformInstallerRegistry registry)
        {
            return RommPlayEndpointResolver.Resolve(rommPlatformId, rommPlatformName, settingsManager, registry).IsPlayableOnRomM;
        }
    }
}
