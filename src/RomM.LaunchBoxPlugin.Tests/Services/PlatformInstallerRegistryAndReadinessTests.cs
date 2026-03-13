using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.PlatformInstallers;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerRegistryAndReadinessTests
    {
        [Fact]
        public void Registry_ReturnsMetadata_WhenInstallerImplementsMetadata()
        {
            var expectedCapabilities = new PlatformInstallerCapabilities
            {
                RequiresEmulatorPath = true,
                RequiresStagingInspection = true
            };
            var expectedDescriptor = new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new PlatformConfigFieldDescriptor { Key = "Rpcs3ExecutablePath", Type = PlatformConfigFieldType.Path }
                }
            };

            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps3"] = new MetadataStubInstaller("ps3", expectedCapabilities, expectedDescriptor)
            });

            var capabilities = registry.GetCapabilities("ps3");
            var descriptor = registry.GetConfigDescriptor("ps3");

            capabilities.Should().BeSameAs(expectedCapabilities);
            descriptor.Should().BeSameAs(expectedDescriptor);
        }

        [Fact]
        public void Registry_ReturnsDefaults_WhenInstallerMissingOrNoMetadata()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["arcade"] = new SimpleStubInstaller("arcade")
            });

            registry.GetCapabilities("missing").Should().NotBeNull();
            registry.GetCapabilities("missing").RequiresStagingInspection.Should().BeFalse();
            registry.GetCapabilities("arcade").RequiresStagingInspection.Should().BeFalse();
            registry.GetConfigDescriptor("missing").Should().BeNull();
            registry.GetConfigDescriptor("arcade").Should().BeNull();
        }

        [Fact]
        public void Readiness_ReturnsNeedsConfiguration_WhenEmulatorPathRequiredAndMissing()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps3"] = new MetadataStubInstaller(
                    "ps3",
                    new PlatformInstallerCapabilities { RequiresEmulatorPath = true },
                    descriptor: null)
            });
            var service = new PlatformReadinessService(registry);

            var mapping = new PlatformMapping
            {
                AssociatedEmulatorId = string.Empty,
                Rpcs3ExecutablePath = string.Empty
            };

            var result = service.Evaluate("ps3", mapping);

            result.IsReady.Should().BeFalse();
            result.Status.Should().Be("Needs Configuration");
            result.Message.Should().Contain("requires emulator configuration");
        }

        [Fact]
        public void Readiness_ReturnsReady_WhenEmulatorPathRequiredButConfigured()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps3"] = new MetadataStubInstaller(
                    "ps3",
                    new PlatformInstallerCapabilities { RequiresEmulatorPath = true },
                    descriptor: null)
            });
            var service = new PlatformReadinessService(registry);

            var mapping = new PlatformMapping
            {
                Rpcs3ExecutablePath = @"C:\Emulators\rpcs3.exe"
            };

            var result = service.Evaluate("ps3", mapping);

            result.IsReady.Should().BeTrue();
            result.Status.Should().Be("Ready");
        }

        private sealed class MetadataStubInstaller : IPlatformInstaller, IPlatformInstallerMetadata
        {
            public MetadataStubInstaller(string platformKey, PlatformInstallerCapabilities capabilities, PlatformConfigDescriptor? descriptor)
            {
                PlatformKey = platformKey;
                DisplayName = platformKey;
                Capabilities = capabilities;
                _descriptor = descriptor;
            }

            private readonly PlatformConfigDescriptor? _descriptor;

            public string PlatformKey { get; }
            public string DisplayName { get; }
            public PlatformInstallerCapabilities Capabilities { get; }

            public PlatformConfigDescriptor? GetConfigDescriptor() => _descriptor;

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

        private sealed class SimpleStubInstaller : IPlatformInstaller
        {
            public SimpleStubInstaller(string platformKey)
            {
                PlatformKey = platformKey;
                DisplayName = platformKey;
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

