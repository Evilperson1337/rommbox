using System;
using System.Collections.Generic;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Services;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class RommPlayEndpointResolverTests
    {
        [Fact]
        public void Resolve_ShouldReturnRuffleProfile_ForFlashAliases()
        {
            var profile = RommPlayEndpointResolver.Resolve("Adobe Flash Player");

            profile.IsPlayableOnRomM.Should().BeTrue();
            profile.PathSuffix.Should().Be("ruffle");
        }

        [Fact]
        public void Resolve_ShouldReturnDefaultEjsProfile_ForLegacyPlayablePlatform()
        {
            var profile = RommPlayEndpointResolver.Resolve("Nintendo 64");

            profile.IsPlayableOnRomM.Should().BeTrue();
            profile.PathSuffix.Should().Be("ejs");
        }

        [Fact]
        public void Resolve_ShouldUsePlatformMetadata_WhenPluginDeclaresCustomEndpoint()
        {
            var registry = new PlatformInstallerRegistry(
                new Dictionary<string, IPlatformInstaller>(StringComparer.OrdinalIgnoreCase)
                {
                    ["flashplayer"] = new MetadataStubInstaller(
                        "flashplayer",
                        "Flash Player",
                        new[] { "4", "flashplayer", "flash", "swf" },
                        new[] { "flash player", "adobe flash player", "swf" },
                        new PlatformInstallerCapabilities
                        {
                            SupportsRomMWebPlay = true,
                            RomMWebPlayPathSuffix = "ruffle"
                        })
                });

            var profile = RommPlayEndpointResolver.Resolve("4", "Flash Player", null, registry, TestLogger.Create());

            profile.IsPlayableOnRomM.Should().BeTrue();
            profile.PathSuffix.Should().Be("ruffle");
        }

        private sealed class MetadataStubInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
        {
            public MetadataStubInstaller(string platformKey, string displayName, IReadOnlyCollection<string> supportedIds, IReadOnlyCollection<string> supportedAliases, PlatformInstallerCapabilities capabilities)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
                SupportedPlatformIds = supportedIds;
                SupportedPlatformAliases = supportedAliases;
                Capabilities = capabilities;
            }

            public string PlatformKey { get; }

            public string DisplayName { get; }

            public IReadOnlyCollection<string>? SupportedPlatformIds { get; }

            public IReadOnlyCollection<string>? SupportedPlatformAliases { get; }

            public PlatformInstallerCapabilities Capabilities { get; }

            public PlatformConfigDescriptor? GetConfigDescriptor() => null;

            public System.Threading.Tasks.Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, System.Threading.CancellationToken ct) => throw new NotSupportedException();

            public System.Threading.Tasks.Task<InstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, System.Threading.CancellationToken ct) => throw new NotSupportedException();

            public System.Threading.Tasks.Task<UninstallResult> UninstallAsync(RomM.Platforms.Abstractions.Models.Uninstall.UninstallContext ctx, IProgress<InstallProgress> progress, System.Threading.CancellationToken ct) => throw new NotSupportedException();

            public System.Threading.Tasks.Task<VerifyResult> VerifyAsync(RomM.Platforms.Abstractions.Models.Verify.VerifyContext ctx, System.Threading.CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
