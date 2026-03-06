using System.Collections.Generic;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.RomBase;

namespace RomM.Platforms.Snes
{
    public sealed class SnesPlatformInstaller : RomPlatformInstallerBase
    {
        public override string PlatformKey => "snes";
        public override string DisplayName => "Super Nintendo";

        protected override RomInstallProfile BuildProfile()
        {
            return new RomInstallProfile
            {
                PlatformKey = PlatformKey,
                DisplayName = DisplayName,
                PlatformFolderName = "SNES",
                ArchivePolicy = RomArchivePolicy.AllowExtraction,
                RomExtensions = new List<string> { ".sfc", ".smc", ".zip" },
                Emulator = new RomEmulatorMetadata
                {
                    EmulatorName = "RetroArch",
                    CoreName = "SNES",
                    LaunchArguments = "{rom}"
                }
            };
        }
    }
}
