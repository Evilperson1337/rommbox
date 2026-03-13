using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RomM.Platforms.Abstractions.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Paths;
using PlatformOptionalContentLocation = RomM.Platforms.Abstractions.Models.Install.OptionalContentLocation;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.PlatformInstallers
{
    internal static class PlatformInstallSettingsMapper
    {
        public static PlatformInstallSettings Map(PlatformMapping mapping)
        {
            if (mapping == null)
            {
                return null;
            }

            return new PlatformInstallSettings
            {
                InstallerMode = MapInstallerMode(mapping.InstallerMode),
                InstallerSilentArgs = string.IsNullOrWhiteSpace(mapping.InstallerSilentArgs) ? null : mapping.InstallerSilentArgs,
                InstallPreReqs = mapping.InstallPreReqs,
                PreReqsRootPath = string.IsNullOrWhiteSpace(mapping.PreReqsRootPath) ? null : mapping.PreReqsRootPath,
                InstallOst = mapping.InstallOst,
                MusicRootPath = string.IsNullOrWhiteSpace(mapping.MusicRootPath) ? null : mapping.MusicRootPath,
                OstInstallLocation = MapOptionalLocation(mapping.OstInstallLocation),
                InstallBonus = mapping.InstallBonus,
                BonusRootPath = string.IsNullOrWhiteSpace(mapping.BonusRootPath) ? null : mapping.BonusRootPath,
                BonusInstallLocation = MapOptionalLocation(mapping.BonusInstallLocation),
                Ps3GameDirectory = string.IsNullOrWhiteSpace(mapping.Ps3GameDirectory) ? null : mapping.Ps3GameDirectory,
                Rpcs3ExecutablePath = string.IsNullOrWhiteSpace(mapping.Rpcs3ExecutablePath) ? null : mapping.Rpcs3ExecutablePath,
                InstallDlcAutomatically = mapping.InstallDlcAutomatically,
                InstallUpdatesAutomatically = mapping.InstallUpdatesAutomatically,
                Rpcs3LicenseDirectory = string.IsNullOrWhiteSpace(mapping.Rpcs3LicenseDirectory) ? null : mapping.Rpcs3LicenseDirectory,
                SkipRegionMismatchedDlc = mapping.SkipRegionMismatchedDlc,
                SkipUnmatchedRapFiles = mapping.SkipUnmatchedRapFiles,
                PreferMetadataBasedPackageMatching = mapping.PreferMetadataBasedPackageMatching,
                Ps4GamesDirectory = string.IsNullOrWhiteSpace(mapping.Ps4GamesDirectory) ? null : mapping.Ps4GamesDirectory,
                ShadPs4ExecutablePath = string.IsNullOrWhiteSpace(mapping.ShadPs4ExecutablePath) ? null : mapping.ShadPs4ExecutablePath,
                Ps4ExternalPkgExtractorPath = string.IsNullOrWhiteSpace(mapping.Ps4ExternalPkgExtractorPath) ? null : mapping.Ps4ExternalPkgExtractorPath,
                Ps4FailIfDirectPkgExtractorMissing = mapping.Ps4FailIfDirectPkgExtractorMissing,
                Pcsx2ExecutablePath = string.IsNullOrWhiteSpace(mapping.Pcsx2ExecutablePath) ? null : mapping.Pcsx2ExecutablePath,
                PspEmulatorMode = string.IsNullOrWhiteSpace(mapping.PspEmulatorMode) ? InferPspEmulatorMode(mapping) : mapping.PspEmulatorMode,
                PpssppExecutablePath = string.IsNullOrWhiteSpace(mapping.PpssppExecutablePath) ? null : mapping.PpssppExecutablePath,
                RetroArchExecutablePath = string.IsNullOrWhiteSpace(mapping.RetroArchExecutablePath) ? null : mapping.RetroArchExecutablePath,
                RetroArchPpssppCorePath = string.IsNullOrWhiteSpace(mapping.RetroArchPpssppCorePath) ? ResolvePspCorePath(mapping) : mapping.RetroArchPpssppCorePath,
                ValidateRetroArchPpssppAssets = mapping.ValidateRetroArchPpssppAssets,
                FailInstallIfEmulatorNotReady = mapping.FailInstallIfEmulatorNotReady,
                SwitchEdenExecutablePath = string.IsNullOrWhiteSpace(mapping.SwitchEdenExecutablePath) ? null : mapping.SwitchEdenExecutablePath,
                AzaharExecutablePath = string.IsNullOrWhiteSpace(mapping.AzaharExecutablePath) ? null : mapping.AzaharExecutablePath,
                AzaharPlusExecutablePath = string.IsNullOrWhiteSpace(mapping.AzaharPlusExecutablePath) ? null : mapping.AzaharPlusExecutablePath,
                DolphinExecutablePath = string.IsNullOrWhiteSpace(mapping.DolphinExecutablePath) ? null : mapping.DolphinExecutablePath,
                CemuExecutablePath = string.IsNullOrWhiteSpace(mapping.CemuExecutablePath) ? null : mapping.CemuExecutablePath,
                Vita3kExecutablePath = string.IsNullOrWhiteSpace(mapping.Vita3kExecutablePath) ? null : mapping.Vita3kExecutablePath,
                VitaFailIfEmulatorNotReady = mapping.VitaFailIfEmulatorNotReady,
                VitaInstallUpdatesAutomatically = mapping.VitaInstallUpdatesAutomatically,
                VitaInstallDlcAutomatically = mapping.VitaInstallDlcAutomatically,
                VitaConsolidateGameInstalls = mapping.VitaConsolidateGameInstalls
            };
        }

        public static RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings MapRomSettings(PlatformMapping mapping, object dataManager = null, string launchBoxPlatformName = null)
        {
            if (mapping == null)
            {
                return null;
            }

            var settings = new RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings
            {
                RomRootPath = string.IsNullOrWhiteSpace(mapping.RomInstallRoot) ? null : mapping.RomInstallRoot,
                ExtractArchives = mapping.ExtractAfterDownload,
                ArchiveHandlingMode = string.IsNullOrWhiteSpace(mapping.ArchiveHandlingMode) ? null : mapping.ArchiveHandlingMode,
                SupportedFileTypes = string.IsNullOrWhiteSpace(mapping.SupportedFileTypes) ? null : mapping.SupportedFileTypes,
                PreferredLaunchExtensions = string.IsNullOrWhiteSpace(mapping.PreferredLaunchExtensions) ? null : mapping.PreferredLaunchExtensions,
                UseGameSubdirectory = mapping.UseGameSubdirectory,
                InstallAllMatchingFiles = mapping.InstallAllMatchingFiles,
                InstallFromArchiveDirectly = mapping.InstallFromArchiveDirectly,
                InstallLayoutMode = string.IsNullOrWhiteSpace(mapping.InstallLayoutMode) ? null : mapping.InstallLayoutMode,
                ArtifactSelectionMode = string.IsNullOrWhiteSpace(mapping.ArtifactSelectionMode) ? null : mapping.ArtifactSelectionMode,
                EmulatorId = ResolveEmulatorId(mapping, dataManager, launchBoxPlatformName),
                EmulatorExecutablePath = ResolveEmulatorExecutablePath(mapping, dataManager, launchBoxPlatformName),
                CoreId = string.IsNullOrWhiteSpace(mapping.EmulatorCoreId) ? null : mapping.EmulatorCoreId,
                CoreName = string.IsNullOrWhiteSpace(mapping.EmulatorCoreName) ? null : mapping.EmulatorCoreName,
                CorePath = string.IsNullOrWhiteSpace(mapping.EmulatorCorePath) ? null : mapping.EmulatorCorePath,
                LaunchArguments = string.IsNullOrWhiteSpace(mapping.EmulatorLaunchArgs) ? null : mapping.EmulatorLaunchArgs
            };

            var policyValue = mapping.RomArchivePolicy;
            if (!string.IsNullOrWhiteSpace(policyValue))
            {
                try
                {
                    if (Enum.TryParse(policyValue, true, out RomM.Platforms.Abstractions.Models.Rom.RomArchivePolicy policy))
                    {
                        settings.ArchivePolicy = policy;
                    }
                }
                catch
                {
                }
            }

            return settings;
        }

        private static InstallerMode MapInstallerMode(RomMbox.Models.Install.InstallerMode mode)
        {
            return mode == RomMbox.Models.Install.InstallerMode.AutoInnoSilent
                ? InstallerMode.AutoInnoSilent
                : InstallerMode.Manual;
        }

        private static PlatformOptionalContentLocation MapOptionalLocation(RomMbox.Models.PlatformMapping.OptionalContentLocation location)
        {
            return location == RomMbox.Models.PlatformMapping.OptionalContentLocation.GameFolder
                ? PlatformOptionalContentLocation.GameFolder
                : PlatformOptionalContentLocation.Default;
        }

        private static string InferPspEmulatorMode(PlatformMapping mapping)
        {
            var combined = string.Join(" ", new[]
            {
                mapping?.AssociatedEmulatorId,
                mapping?.EmulatorCoreId,
                mapping?.EmulatorCoreName,
                mapping?.EmulatorCorePath
            });

            return combined.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0
                ? "RetroArchPPSSPP"
                : "StandalonePPSSPP";
        }

        private static string ResolvePspCorePath(PlatformMapping mapping)
        {
            if (!string.IsNullOrWhiteSpace(mapping?.EmulatorCorePath))
            {
                return mapping.EmulatorCorePath;
            }

            if (!string.IsNullOrWhiteSpace(mapping?.EmulatorCoreName))
            {
                return mapping.EmulatorCoreName;
            }

            if (!string.IsNullOrWhiteSpace(mapping?.EmulatorCoreId))
            {
                return mapping.EmulatorCoreId;
            }

            return string.Empty;
        }

        internal static string ResolveEmulatorId(PlatformMapping mapping, object dataManager, string launchBoxPlatformName)
        {
            var configuredId = mapping?.AssociatedEmulatorId;
            if (!string.IsNullOrWhiteSpace(configuredId))
            {
                return configuredId;
            }

            try
            {
                var manager = dataManager as IDataManager;
                if (manager == null || string.IsNullOrWhiteSpace(launchBoxPlatformName))
                {
                    return null;
                }

                var emulators = manager.GetAllEmulators() ?? Array.Empty<IEmulator>();
                foreach (var emulator in emulators)
                {
                    var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                    if (platforms.Any(platform => string.Equals(platform?.Platform, launchBoxPlatformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true))
                    {
                        return emulator?.Id;
                    }
                }

                foreach (var emulator in emulators)
                {
                    var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                    if (platforms.Any(platform => string.Equals(platform?.Platform, launchBoxPlatformName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return emulator?.Id;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        internal static string ResolveEmulatorExecutablePath(PlatformMapping mapping, object dataManager, string launchBoxPlatformName)
        {
            var emulatorId = ResolveEmulatorId(mapping, dataManager, launchBoxPlatformName);
            if (string.IsNullOrWhiteSpace(emulatorId))
            {
                return null;
            }

            try
            {
                if (dataManager is IDataManager manager)
                {
                    var emulator = manager.GetEmulatorById(emulatorId);
                    var applicationPath = NormalizeLaunchBoxPath(emulator?.ApplicationPath);
                    return string.IsNullOrWhiteSpace(applicationPath) ? null : applicationPath;
                }

                var method = dataManager?.GetType().GetMethod("GetEmulatorById", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var emulatorObject = method?.Invoke(dataManager, new object[] { emulatorId });
                var rawPath = emulatorObject?.GetType().GetProperty("ApplicationPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(emulatorObject) as string;
                var normalized = NormalizeLaunchBoxPath(rawPath);
                return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
            }
            catch
            {
                return null;
            }
        }

        internal static string NormalizeLaunchBoxPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(path))
                {
                    return Path.GetFullPath(path);
                }

                var launchBoxRoot = PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(launchBoxRoot))
                {
                    return path;
                }

                return Path.GetFullPath(Path.Combine(launchBoxRoot, path));
            }
            catch
            {
                return path;
            }
        }
    }
}
