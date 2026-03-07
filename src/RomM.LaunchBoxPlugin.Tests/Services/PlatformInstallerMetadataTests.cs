using FluentAssertions;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.PS3;
using RomM.Platforms.Windows;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerMetadataTests
    {
        [Fact]
        public void WindowsInstaller_ExposesExpectedCapabilities_AndNoConfigDescriptor()
        {
            var installer = new WindowsPlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsInstaller.Should().BeTrue();
            capabilities.SupportsSilentInstaller.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();
            capabilities.SupportsRaps.Should().BeFalse();

            installer.GetConfigDescriptor().Should().BeNull();
        }

        [Fact]
        public void Ps3Installer_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new Ps3PlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();
            capabilities.SupportsRaps.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().NotBeNull();
            descriptor.Fields.Should().HaveCount(5);
            descriptor.Fields.Select(field => field.Key).Should().BeEquivalentTo(new[]
            {
                "Rpcs3ExecutablePath",
                "Rpcs3LicenseDirectory",
                "SkipRegionMismatchedDlc",
                "SkipUnmatchedRapFiles",
                "PreferMetadataBasedPackageMatching"
            });

            descriptor.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "Rpcs3ExecutablePath", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Path
                && !field.Required
                && !field.Advanced);

            descriptor.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "SkipRegionMismatchedDlc", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Boolean
                && string.Equals(field.DefaultValue, "false", StringComparison.OrdinalIgnoreCase));
        }
    }
}

