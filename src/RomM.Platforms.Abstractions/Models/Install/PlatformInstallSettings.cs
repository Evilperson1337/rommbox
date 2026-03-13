namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class PlatformInstallSettings
    {
        public InstallerMode InstallerMode { get; set; } = InstallerMode.Manual;
        public string? InstallerSilentArgs { get; set; }
        public bool InstallPreReqs { get; set; }
        public string? PreReqsRootPath { get; set; }
        public bool InstallOst { get; set; }
        public string? MusicRootPath { get; set; }
        public OptionalContentLocation OstInstallLocation { get; set; } = OptionalContentLocation.Default;
        public bool InstallBonus { get; set; }
        public string? BonusRootPath { get; set; }
        public OptionalContentLocation BonusInstallLocation { get; set; } = OptionalContentLocation.Default;
        public string? Ps3GameDirectory { get; set; }
        public string? Rpcs3ExecutablePath { get; set; }
        public bool InstallDlcAutomatically { get; set; }
        public bool InstallUpdatesAutomatically { get; set; }
        public string? Rpcs3LicenseDirectory { get; set; }
        public bool SkipRegionMismatchedDlc { get; set; }
        public bool SkipUnmatchedRapFiles { get; set; }
        public bool PreferMetadataBasedPackageMatching { get; set; }
        public string? Ps4GamesDirectory { get; set; }
        public string? ShadPs4ExecutablePath { get; set; }
        public string? Ps4ExternalPkgExtractorPath { get; set; }
        public bool Ps4FailIfDirectPkgExtractorMissing { get; set; }
        public string? Pcsx2ExecutablePath { get; set; }
        public string? PspEmulatorMode { get; set; }
        public string? PpssppExecutablePath { get; set; }
        public string? RetroArchExecutablePath { get; set; }
        public string? RetroArchPpssppCorePath { get; set; }
        public bool ValidateRetroArchPpssppAssets { get; set; } = true;
        public bool FailInstallIfEmulatorNotReady { get; set; }
        public string? SwitchEdenExecutablePath { get; set; }
        public string? AzaharExecutablePath { get; set; }
        public string? AzaharPlusExecutablePath { get; set; }
        public string? DolphinExecutablePath { get; set; }
        public string? CemuExecutablePath { get; set; }
        public string? Vita3kExecutablePath { get; set; }
        public bool VitaFailIfEmulatorNotReady { get; set; }
        public bool VitaInstallUpdatesAutomatically { get; set; }
        public bool VitaInstallDlcAutomatically { get; set; }
        public bool VitaConsolidateGameInstalls { get; set; }
    }
}

