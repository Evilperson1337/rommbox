using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;

namespace RomM.Platforms.Abstractions
{
    public interface IPlatformInstaller
    {
        string PlatformKey { get; }
        string DisplayName { get; }

        Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct);

        Task<InstallResult> InstallAsync(
            InstallContext ctx,
            IProgress<InstallProgress> progress,
            CancellationToken ct);

        Task<UninstallResult> UninstallAsync(
            UninstallContext ctx,
            IProgress<InstallProgress> progress,
            CancellationToken ct);

        Task<VerifyResult> VerifyAsync(
            VerifyContext ctx,
            CancellationToken ct);
    }
}
