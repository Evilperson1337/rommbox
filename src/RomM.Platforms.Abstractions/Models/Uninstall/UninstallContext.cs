using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Abstractions.Models.Uninstall
{
    public sealed class UninstallContext
    {
        public string? GameName { get; set; }
        public string? InstallRootPath { get; set; }
        public string? InstalledPath { get; set; }
        public string? ArchivePath { get; set; }
        public string? PlatformContentId { get; set; }
        public string? EmulatorExecutablePath { get; set; }
        public string? WindowsInstallType { get; set; }
        public IPlatformLogger? Logger { get; set; }
    }
}

