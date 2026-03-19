namespace RomM.Platforms.Abstractions.Models.Metadata
{
    public sealed class PlatformInstallerCapabilities
    {
        public bool SupportsArchives { get; set; }
        public bool SupportsDirectFiles { get; set; }
        public bool RequiresStagingInspection { get; set; }
        public bool SupportsAutoFormatDetection { get; set; }
        public bool SupportsInstaller { get; set; }
        public bool SupportsSilentInstaller { get; set; }
        public bool SupportsUninstall { get; set; }
        public bool SupportsInstallStateDetection { get; set; }
        public bool SupportsApplicationPathDiscovery { get; set; }
        public bool SupportsDlc { get; set; }
        public bool SupportsUpdates { get; set; }
        public bool SupportsRaps { get; set; }
        public bool RequiresEmulatorPath { get; set; }
        public bool SupportsRomMWebPlay { get; set; }
        public string RomMWebPlayPathSuffix { get; set; } = string.Empty;
    }
}

