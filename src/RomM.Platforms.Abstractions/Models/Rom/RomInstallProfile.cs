using System.Collections.Generic;

namespace RomM.Platforms.Abstractions.Models.Rom
{
    public sealed class RomInstallProfile
    {
        public string PlatformKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? PlatformFolderName { get; set; }
        public bool UsePlatformSubdirectory { get; set; } = true;
        public bool UseGameSubdirectory { get; set; }
        public RomArchivePolicy ArchivePolicy { get; set; } = RomArchivePolicy.AllowExtraction;
        public IReadOnlyList<string> RomExtensions { get; set; } = new List<string>();
        public bool AllowMultipleRoms { get; set; }
        public RomEmulatorMetadata Emulator { get; set; } = new RomEmulatorMetadata();
    }
}
