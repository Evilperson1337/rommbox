using System.Collections.Generic;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.RomBase;

namespace RomM.Platforms.Arcade
{
    public sealed class ArcadePlatformInstaller : RomPlatformInstallerBase
    {
        public override string PlatformKey => "arcade";
        public override string DisplayName => "Arcade";

        protected override RomInstallProfile BuildProfile()
        {
            return new RomInstallProfile
            {
                PlatformKey = PlatformKey,
                DisplayName = DisplayName,
                PlatformFolderName = "Arcade",
                ArchivePolicy = RomArchivePolicy.Preserve,
                RomExtensions = new List<string> { ".zip" },
                Emulator = new RomEmulatorMetadata
                {
                    EmulatorName = "RetroArch",
                    CoreName = "MAME",
                    LaunchArguments = "{rom}"
                }
            };
        }
    }
}
