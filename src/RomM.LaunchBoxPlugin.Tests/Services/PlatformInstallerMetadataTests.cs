using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.General;
using RomM.Platforms.GameCube;
using RomM.Platforms.N3DS;
using RomM.Platforms.N64;
using RomM.Platforms.PS1;
using RomM.Platforms.PS2;
using RomM.Platforms.PSP;
using RomM.Platforms.PS4;
using RomM.Platforms.PS3;
using RomM.Platforms.Snes;
using RomM.Platforms.Vita;
using RomM.Platforms.Wii;
using RomM.Platforms.WiiU;
using RomM.Platforms.Windows;
using RomM.Platforms.Xbox360;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerMetadataTests
    {
        [Fact]
        public void NumericRommPlatformIds_AreUniqueAcrossDedicatedInstallers()
        {
            (string DisplayName, IPlatformInstallerIdentityMetadata Identity)[] installers =
            {
                ("Nintendo 3DS", (IPlatformInstallerIdentityMetadata)new Nintendo3DSPlatformInstaller()),
                ("Nintendo 64", (IPlatformInstallerIdentityMetadata)new N64PlatformInstaller()),
                ("PlayStation", (IPlatformInstallerIdentityMetadata)new Ps1PlatformInstaller()),
                ("PlayStation 2", (IPlatformInstallerIdentityMetadata)new Ps2PlatformInstaller()),
                ("Super Nintendo", (IPlatformInstallerIdentityMetadata)new SnesPlatformInstaller())
            };

            var duplicateNumericIds = installers
                .SelectMany(installer => (installer.Identity.SupportedPlatformIds ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
                    .Select(id => new { Id = id, Installer = installer.DisplayName }))
                .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .Where(group => group.Select(entry => entry.Installer).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(group => $"{group.Key}=[{string.Join(", ", group.Select(entry => entry.Installer).Distinct(StringComparer.OrdinalIgnoreCase))}]")
                .ToArray();

            duplicateNumericIds.Should().BeEmpty("numeric RomM platform ids must map to exactly one dedicated installer");
        }

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

        [Fact]
        public void Ps2Installer_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new Ps2PlatformInstaller();

            installer.SupportedPlatformIds.Should().Contain(new[] { "ps2", "17" });

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "Pcsx2ExecutablePath", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Path
                && !field.Required);
        }

        [Fact]
        public void PspInstaller_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new PspPlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Select(field => field.Key).Should().Contain(new[]
            {
                "PspEmulatorMode",
                "PpssppExecutablePath",
                "RetroArchExecutablePath",
                "RetroArchPpssppCorePath",
                "ValidateRetroArchPpssppAssets",
                "FailInstallIfEmulatorNotReady"
            });
        }

        [Fact]
        public void GeneralInstaller_ExposesGenericRomConfigurationProvider()
        {
            var installer = new GeneralPlatformInstaller();

            installer.Should().BeAssignableTo<IPlatformConfigurationProvider>();
            var provider = (IPlatformConfigurationProvider)installer;
            provider.PluginKey.Should().Be("general");

            var schema = provider.GetConfigurationSchema();
            schema.Fields.Select(field => field.Key).Should().Contain(new[]
            {
                "ArchiveHandlingMode",
                "SupportedFileTypes",
                "InstallLayoutMode",
                "ArtifactSelectionMode"
            });

            var defaults = provider.GetDefaultValues();
            defaults["ArchiveHandlingMode"].Should().Be("NeverExtract");
            defaults["InstallLayoutMode"].Should().Be("CreatePerGameSubfolder");
            defaults["ArtifactSelectionMode"].Should().Be("ExtensionPriority");
        }

        [Fact]
        public void Ps4Installer_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new Ps4PlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().NotBeNull();
            descriptor.Fields.Select(field => field.Key).Should().BeEquivalentTo(new[]
            {
                "ShadPs4ExecutablePath",
                "Ps4GamesDirectory",
                "Ps4ExternalPkgExtractorPath",
                "Ps4FailIfDirectPkgExtractorMissing"
            });
        }

        [Fact]
        public void VitaInstaller_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new VitaPlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Select(field => field.Key).Should().Contain(new[]
            {
                "Vita3kExecutablePath",
                "VitaInstallUpdatesAutomatically",
                "VitaInstallDlcAutomatically",
                "VitaFailIfEmulatorNotReady"
            });
        }

        [Fact]
        public void WiiInstaller_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new WiiPlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsInstaller.Should().BeFalse();
            capabilities.SupportsSilentInstaller.Should().BeFalse();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "DolphinExecutablePath", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Path
                && !field.Required);
        }

        [Fact]
        public void GameCubeInstaller_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new GameCubePlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsInstaller.Should().BeFalse();
            capabilities.SupportsSilentInstaller.Should().BeFalse();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeFalse();
            capabilities.SupportsUpdates.Should().BeFalse();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "DolphinExecutablePath", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Path
                && !field.Required);
        }

        [Fact]
        public void WiiUInstaller_ExposesExpectedCapabilities_AndConfigDescriptorFields()
        {
            var installer = new WiiUPlatformInstaller();

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsInstaller.Should().BeFalse();
            capabilities.SupportsSilentInstaller.Should().BeFalse();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            var descriptor = installer.GetConfigDescriptor();
            descriptor.Should().NotBeNull();
            descriptor!.Fields.Should().ContainSingle(field =>
                string.Equals(field.Key, "CemuExecutablePath", StringComparison.Ordinal)
                && field.Type == PlatformConfigFieldType.Path
                && !field.Required);
        }

        [Fact]
        public void Xbox360Installer_ExposesExpectedCapabilities_AndNoConfigDescriptor()
        {
            var installer = new Xbox360PlatformInstaller();

            installer.SupportedPlatformAliases.Should().Contain(new[]
            {
                "xbox 360",
                "microsoft xbox 360"
            });
            installer.SupportedPlatformIds.Should().Contain(new[]
            {
                "xbox360",
                "x360"
            });

            var capabilities = installer.Capabilities;
            capabilities.SupportsArchives.Should().BeTrue();
            capabilities.SupportsDirectFiles.Should().BeTrue();
            capabilities.RequiresStagingInspection.Should().BeTrue();
            capabilities.SupportsInstaller.Should().BeFalse();
            capabilities.SupportsSilentInstaller.Should().BeFalse();
            capabilities.SupportsUninstall.Should().BeTrue();
            capabilities.SupportsInstallStateDetection.Should().BeTrue();
            capabilities.SupportsApplicationPathDiscovery.Should().BeTrue();
            capabilities.SupportsDlc.Should().BeTrue();
            capabilities.SupportsUpdates.Should().BeTrue();

            installer.GetConfigDescriptor().Should().BeNull();
        }
    }
}

