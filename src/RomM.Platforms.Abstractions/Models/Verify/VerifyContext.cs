using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Abstractions.Models.Verify
{
    public sealed class VerifyContext
    {
        public string? InstallRootPath { get; set; }
        public string? InstalledPath { get; set; }
        public string? WindowsInstallType { get; set; }
        public IPlatformLogger? Logger { get; set; }
    }
}

