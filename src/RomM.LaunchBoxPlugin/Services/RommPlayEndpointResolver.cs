using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;

namespace RomMbox.Services
{
    /// <summary>
    /// Resolves whether a platform is playable on RomM and which endpoint it uses.
    /// </summary>
    internal static class RommPlayEndpointResolver
    {
        private const string DefaultPlayPathSuffix = "ejs";

        private static readonly RommPlayEndpointProfile DefaultPlayableProfile = new()
        {
            IsPlayableOnRomM = true,
            PathSuffix = DefaultPlayPathSuffix
        };

        private static readonly RommPlayEndpointProfile NotPlayableProfile = new()
        {
            IsPlayableOnRomM = false,
            PathSuffix = string.Empty
        };

        private static readonly HashSet<string> PlayablePlatforms = new(StringComparer.OrdinalIgnoreCase)
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
            "WonderSwan Color",
            "Flash Player"
        };

        private static readonly Dictionary<string, string> PlatformAliases = new(StringComparer.OrdinalIgnoreCase)
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
            { "Sony Playstation", "PlayStation" },
            { "Flash", "Flash Player" },
            { "Adobe Flash", "Flash Player" },
            { "Adobe Flash Player", "Flash Player" },
            { "Macromedia Flash", "Flash Player" },
            { "SWF", "Flash Player" },
            { "Browser (Flash/HTML5)", "Flash Player" },
            { "Browser Flash Html5", "Flash Player" },
            { "Flash HTML5", "Flash Player" }
        };

        private static readonly Dictionary<string, RommPlayEndpointProfile> ExplicitProfiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Flash Player"] = new RommPlayEndpointProfile
            {
                IsPlayableOnRomM = true,
                PathSuffix = "ruffle"
            }
        };

        public static RommPlayEndpointProfile Resolve(string platformName)
        {
            var normalized = NormalizePlatformName(platformName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return NotPlayableProfile;
            }

            if (ExplicitProfiles.TryGetValue(normalized, out var explicitProfile))
            {
                return explicitProfile;
            }

            return PlayablePlatforms.Contains(normalized)
                ? DefaultPlayableProfile
                : NotPlayableProfile;
        }

        public static RommPlayEndpointProfile Resolve(
            string rommPlatformId,
            string rommPlatformName,
            SettingsManager settingsManager,
            PlatformInstallerRegistry registry,
            LoggingService logger = null)
        {
            var evidence = BuildResolutionEvidence(rommPlatformId, rommPlatformName, settingsManager, logger);

            var pluginProfile = ResolveFromPlatformMetadata(registry, evidence, logger);
            if (pluginProfile != null)
            {
                return pluginProfile;
            }

            if (!string.IsNullOrWhiteSpace(evidence.LaunchBoxPlatformName))
            {
                var mappedProfile = Resolve(evidence.LaunchBoxPlatformName);
                if (mappedProfile.IsPlayableOnRomM)
                {
                    return mappedProfile;
                }
            }

            return Resolve(evidence.RomMPlatformName);
        }

        private static RommPlayEndpointProfile ResolveFromPlatformMetadata(
            PlatformInstallerRegistry registry,
            PlaybackResolutionEvidence evidence,
            LoggingService logger)
        {
            if (registry == null)
            {
                return null;
            }

            var resolution = PlatformIdentityResolver.Resolve(
                registry,
                new PlatformResolutionEvidence
                {
                    PlatformKey = evidence.RomMPlatformId,
                    PlatformDisplayName = evidence.RomMPlatformName,
                    LaunchBoxPlatformName = evidence.LaunchBoxPlatformName
                });

            if (string.IsNullOrWhiteSpace(resolution.ResolvedPlatformKey))
            {
                return null;
            }

            var capabilities = registry.GetCapabilities(resolution.ResolvedPlatformKey);
            if (!capabilities.SupportsRomMWebPlay)
            {
                return null;
            }

            var suffix = string.IsNullOrWhiteSpace(capabilities.RomMWebPlayPathSuffix)
                ? DefaultPlayPathSuffix
                : capabilities.RomMWebPlayPathSuffix.Trim('/');

            logger?.Debug($"Resolved RomM play endpoint from platform metadata. PlatformKey='{resolution.ResolvedPlatformKey}', PathSuffix='{suffix}'.");

            return new RommPlayEndpointProfile
            {
                IsPlayableOnRomM = true,
                PathSuffix = suffix
            };
        }

        private static PlaybackResolutionEvidence BuildResolutionEvidence(
            string rommPlatformId,
            string rommPlatformName,
            SettingsManager settingsManager,
            LoggingService logger)
        {
            var launchBoxPlatformName = string.Empty;
            if (settingsManager != null && !string.IsNullOrWhiteSpace(rommPlatformId))
            {
                var mappingStore = new PlatformMappingStore(LoggingServiceFactory.Create());
                var mapping = mappingStore
                    .GetPlatformMappingAsync(rommPlatformId, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                launchBoxPlatformName = mapping?.LaunchBoxPlatformName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(launchBoxPlatformName))
                {
                    logger?.Debug($"Resolved LaunchBox platform mapping for RomM playback. RomMPlatformId='{rommPlatformId}', LaunchBoxPlatform='{launchBoxPlatformName}'.");
                }
            }

            return new PlaybackResolutionEvidence
            {
                RomMPlatformId = rommPlatformId?.Trim() ?? string.Empty,
                RomMPlatformName = rommPlatformName?.Trim() ?? string.Empty,
                LaunchBoxPlatformName = launchBoxPlatformName
            };
        }

        private static string NormalizePlatformName(string platformName)
        {
            if (string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            var normalized = platformName.Trim();
            return PlatformAliases.TryGetValue(normalized, out var alias)
                ? alias
                : normalized;
        }

        private sealed class PlaybackResolutionEvidence
        {
            public string RomMPlatformId { get; init; } = string.Empty;

            public string RomMPlatformName { get; init; } = string.Empty;

            public string LaunchBoxPlatformName { get; init; } = string.Empty;
        }
    }
}
