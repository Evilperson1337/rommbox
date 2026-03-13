using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Abstractions.Models
{
    public sealed class PlatformContext
    {
        public string? PlatformKey { get; set; }
        public string? GameName { get; set; }
        public string? InstallDirectory { get; set; }
        public string? InstallRootPath { get; set; }
        public string? InstalledPath { get; set; }
        public string? ArchivePath { get; set; }
        public string? ExtractedPath { get; set; }
        public string? WindowsInstallType { get; set; }
        public bool IsInstalled { get; set; }
        public IPlatformLogger? Logger { get; set; }
    }
}

