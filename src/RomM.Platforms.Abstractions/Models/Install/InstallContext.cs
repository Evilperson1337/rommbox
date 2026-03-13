using System;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Logging;

namespace RomM.Platforms.Abstractions.Models.Install
{
    public sealed class InstallContext
    {
        public string? GameName { get; set; }
        public string? InstallDirectory { get; set; }
        public string? StagingDirectory { get; set; }
        public string? ArchivePath { get; set; }
        public string? ExtractedPath { get; set; }
        public PlatformInstallSettings? Settings { get; set; }
        public Rom.RomInstallSettings? RomSettings { get; set; }
        public Func<ExecutableSelectionRequest, Task<ExecutableSelectionResult>>? SelectExecutableAsync { get; set; }
        public Func<ConfirmationRequest, Task<bool>>? ConfirmAsync { get; set; }
        public IPlatformLogger? Logger { get; set; }
    }
}

