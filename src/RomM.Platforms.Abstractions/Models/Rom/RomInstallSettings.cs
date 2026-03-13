namespace RomM.Platforms.Abstractions.Models.Rom
{
    public sealed class RomInstallSettings
    {
        public string? RomRootPath { get; set; }
        public bool? ExtractArchives { get; set; }
        public RomArchivePolicy? ArchivePolicy { get; set; }
        public string? ArchiveHandlingMode { get; set; }
        public string? SupportedFileTypes { get; set; }
        public string? PreferredLaunchExtensions { get; set; }
        public bool? UseGameSubdirectory { get; set; }
        public bool? InstallAllMatchingFiles { get; set; }
        public bool? InstallFromArchiveDirectly { get; set; }
        public string? InstallLayoutMode { get; set; }
        public string? ArtifactSelectionMode { get; set; }
        public string? EmulatorId { get; set; }
        public string? EmulatorName { get; set; }
        public string? EmulatorExecutablePath { get; set; }
        public string? CoreId { get; set; }
        public string? CoreName { get; set; }
        public string? CorePath { get; set; }
        public string? LaunchArguments { get; set; }
    }
}
