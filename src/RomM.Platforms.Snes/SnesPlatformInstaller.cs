using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.RomBase;

namespace RomM.Platforms.Snes
{
    public sealed class SnesPlatformInstaller : RomPlatformInstallerBase, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".sfc",
            ".smc"
        };

        public override string PlatformKey => "snes";
        public override string DisplayName => "Super Nintendo";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "23", "snes" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "snes",
            "super nintendo",
            "super nintendo entertainment system",
            "supernintendo"
        };

        public override async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            var result = await base.InstallAsync(ctx, progress, ct).ConfigureAwait(false);
            if (result.Success)
            {
                result.Message = "SNES install completed.";
                ctx?.Logger?.Write(Abstractions.Logging.PlatformLogLevel.Info, "Configured emulator: RetroArch (snes9x)");
            }

            return result;
        }

        protected override bool IsInstalledArtifactSupported(string installedPath)
        {
            var extension = Path.GetExtension(installedPath) ?? string.Empty;
            return SupportedExtensions.Contains(extension);
        }

        protected override RomInstallProfile BuildProfile()
        {
            return new RomInstallProfile
            {
                PlatformKey = PlatformKey,
                DisplayName = DisplayName,
                PlatformFolderName = "Super Nintendo Entertainment System",
                UsePlatformSubdirectory = false,
                UseGameSubdirectory = true,
                ArchivePolicy = RomArchivePolicy.Preserve,
                RomExtensions = new List<string> { ".zip", ".sfc", ".smc" },
                Emulator = new RomEmulatorMetadata
                {
                    EmulatorName = "RetroArch",
                    CoreName = "snes9x",
                    LaunchArguments = "{rom}"
                }
            };
        }
    }
}
