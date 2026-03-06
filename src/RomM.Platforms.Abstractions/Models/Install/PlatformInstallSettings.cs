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
    }
}

