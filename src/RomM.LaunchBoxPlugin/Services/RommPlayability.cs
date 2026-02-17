using System;
using System.Collections.Generic;
using RomMbox.Services.Settings;

namespace RomMbox.Services
{
    /// <summary>
    /// Determines whether a platform can be played via RomM based on known support lists
    /// and optional user mappings.
    /// </summary>
    internal static class RommPlayability
    {
        private static readonly HashSet<string> PlayablePlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "3DO Interactive Multiplayer",
            "Amiga",
            "Arcade",
            "Atari 2600",
            "Atari 5200",
            "Atari 7800",
            "Atari Jaguar",
            "Atari Lynx",
            "Commodore C64/128/MAX",
            "ColecoVision",
            "DOS",
            "Neo Geo Pocket",
            "Neo Geo Pocket Color",
            "Nintendo 64",
            "Nintendo Entertainment System",
            "Family Computer",
            "Nintendo DS",
            "Game Boy",
            "Game Boy Color",
            "Game Boy Advance",
            "PC-FX",
            "PlayStation",
            "PlayStation Portable",
            "Sega 32X",
            "Sega CD",
            "Sega Game Gear",
            "Sega Master System/Mark III",
            "Sega Mega Drive/Genesis",
            "Sega Saturn",
            "Super Nintendo Entertainment System",
            "Super Famicom",
            "TurboGraphx-16/PC Engine",
            "Virtual Boy",
            "WonderSwan",
            "WonderSwan Color"
        };

        private static readonly Dictionary<string, string> PlatformAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "3DO", "3DO Interactive Multiplayer" },
            { "Arcade/MAME", "Arcade" },
            { "Commodore 64", "Commodore C64/128/MAX" },
            { "MS-DOS", "DOS" },
            { "Nintendo Entertainment System (NES)", "Nintendo Entertainment System" },
            { "Nintendo Family Computer (Famicom)", "Family Computer" },
            { "PlayStation (PS)", "PlayStation" },
            { "PlayStation Portable (PSP)", "PlayStation Portable" },
            { "Sega Master System", "Sega Master System/Mark III" },
            { "Sega Genesis/Megadrive", "Sega Mega Drive/Genesis" },
            { "Super Nintendo Entertainment System (SNES)", "Super Nintendo Entertainment System" },
            { "Sony Playstation", "PlayStation" }
        };

        /// <summary>
        /// Returns true when the given platform name is known to be playable on RomM.
        /// </summary>
        public static bool IsPlayablePlatform(string platformName)
        {
            if (string.IsNullOrWhiteSpace(platformName))
            {
                return false;
            }

            var normalized = platformName.Trim();
            if (PlatformAliases.TryGetValue(normalized, out var alias))
            {
                normalized = alias;
            }

            return PlayablePlatforms.Contains(normalized);
        }

        /// <summary>
        /// Resolves the LaunchBox platform name (if configured) and checks playability.
        /// </summary>
        public static bool IsPlayablePlatform(string rommPlatformId, string rommPlatformName, SettingsManager settingsManager)
        {
            if (settingsManager == null)
            {
                return IsPlayablePlatform(rommPlatformName);
            }

            var mapping = settingsManager.GetPlatformMapping(rommPlatformId ?? string.Empty);
            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.LaunchBoxPlatformName))
            {
                return IsPlayablePlatform(mapping.LaunchBoxPlatformName);
            }

            return IsPlayablePlatform(rommPlatformName);
        }
    }
}
