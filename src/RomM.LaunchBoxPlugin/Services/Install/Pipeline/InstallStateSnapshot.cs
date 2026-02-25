using System;
using RomMbox.Models;

namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallStateSnapshot
    {
        public string LaunchBoxGameId { get; set; }
        public string RommRomId { get; set; }
        public string RommPlatformId { get; set; }
        public string ServerUrl { get; set; }
        public string InstalledPath { get; set; }
        public string RommLaunchPath { get; set; }
        public string RommLaunchArgs { get; set; }
        public string ArchivePath { get; set; }
        public string InstallRootPath { get; set; }
        public bool IsInstalled { get; set; }
        public DateTimeOffset? InstalledUtc { get; set; }
        public DateTimeOffset? LastValidatedUtc { get; set; }
        public string WindowsInstallType { get; set; }

        public InstallState ToInstallState()
        {
            return new InstallState
            {
                LaunchBoxGameId = LaunchBoxGameId,
                RommRomId = RommRomId,
                RommPlatformId = RommPlatformId,
                ServerUrl = ServerUrl,
                InstalledPath = InstalledPath,
                RommLaunchPath = RommLaunchPath,
                RommLaunchArgs = RommLaunchArgs,
                ArchivePath = ArchivePath,
                InstallRootPath = InstallRootPath,
                IsInstalled = IsInstalled,
                InstalledUtc = InstalledUtc ?? DateTimeOffset.UtcNow,
                LastValidatedUtc = LastValidatedUtc ?? DateTimeOffset.UtcNow,
                WindowsInstallType = WindowsInstallType
            };
        }
    }
}
