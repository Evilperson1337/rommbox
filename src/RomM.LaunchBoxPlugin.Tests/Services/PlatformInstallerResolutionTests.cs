using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerResolutionTests
    {
        [Fact]
        public void Resolves_PlatformKey_By_LaunchBox_Name_When_Id_Missing()
        {
            var registry = BuildRegistry(new StubInstaller("ps3", "PlayStation 3"));
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "",
                launchBoxPlatformName: "Sony Playstation 3",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps3");
        }

        private static PlatformInstallerRegistry BuildRegistry(IPlatformInstaller installer)
        {
            return new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                [installer.PlatformKey] = installer
            });
        }

        private sealed class StubInstaller : IPlatformInstaller
        {
            public StubInstaller(string platformKey, string displayName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<InstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new InstallResult { Success = true });
            }

            public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new UninstallResult { Success = true });
            }

            public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new VerifyResult { IsValid = true });
            }
        }
    }
}
