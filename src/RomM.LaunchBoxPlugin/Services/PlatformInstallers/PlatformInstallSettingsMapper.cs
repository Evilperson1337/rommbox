using System;
using RomM.Platforms.Abstractions.Models.Install;
using RomMbox.Models.PlatformMapping;
using PlatformOptionalContentLocation = RomM.Platforms.Abstractions.Models.Install.OptionalContentLocation;

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
                PspEmulatorMode = InferPspEmulatorMode(mapping),
                RetroArchPpssppCorePath = ResolvePspCorePath(mapping),
                ValidateRetroArchPpssppAssets = true,
                FailInstallIfEmulatorNotReady = false,
                Vita3kExecutablePath = string.Empty,
                VitaFailIfEmulatorNotReady = false,
                VitaInstallUpdatesAutomatically = mapping.InstallUpdatesAutomatically,
                VitaInstallDlcAutomatically = mapping.InstallDlcAutomatically
            };
        }

        public static RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings MapRomSettings(PlatformMapping mapping)
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
                EmulatorId = string.IsNullOrWhiteSpace(mapping.AssociatedEmulatorId) ? null : mapping.AssociatedEmulatorId,
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
    }
}
