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

namespace RomM.Platforms.N64
{
    public sealed class N64PlatformInstaller : RomPlatformInstallerBase, IPlatformInstallerIdentityMetadata
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".z64",
            ".n64",
            ".v64",
            ".zip"
        };

        public override string PlatformKey => "n64";
        public override string DisplayName => "Nintendo 64";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "8", "n64" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "n64",
            "nintendo 64",
            "nintendo64"
        };

        public override async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            var result = await base.InstallAsync(ctx, progress, ct).ConfigureAwait(false);
            if (result.Success)
            {
                result.Message = "N64 install completed.";
                ctx?.Logger?.Write(Abstractions.Logging.PlatformLogLevel.Info, "Configured emulator: RetroArch (Mupen64Plus-Next)");
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
                PlatformFolderName = "Nintendo 64",
                UsePlatformSubdirectory = false,
                UseGameSubdirectory = true,
                ArchivePolicy = RomArchivePolicy.Preserve,
                RomExtensions = new List<string> { ".z64", ".n64", ".v64", ".zip" },
                Emulator = new RomEmulatorMetadata
                {
                    EmulatorName = "RetroArch",
                    CoreName = "mupen64plus-next",
                    LaunchArguments = "{rom}"
                }
            };
        }
    }
}
