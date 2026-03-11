using System.IO;
using FluentAssertions;
using Moq;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallSettingsMapperTests
    {
        [Fact]
        public void MapRomSettings_ResolvesDefaultPlatformEmulator_WhenMappingHasNoAssociatedEmulator()
        {
            var mapping = new PlatformMapping();
            var emulatorPlatform = new Mock<IEmulatorPlatform>();
            emulatorPlatform.SetupGet(p => p.Platform).Returns("Microsoft Xbox 360");
            emulatorPlatform.SetupGet(p => p.IsDefault).Returns(true);

            var emulator = new Mock<IEmulator>();
            emulator.SetupGet(e => e.Id).Returns("xenia-default");
            emulator.SetupGet(e => e.ApplicationPath).Returns(@"D:\LaunchBox\Emulators\Xenia\xenia_canary.exe");
            emulator.Setup(e => e.GetAllEmulatorPlatforms()).Returns(new[] { emulatorPlatform.Object });

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetAllEmulators()).Returns(new[] { emulator.Object });
            dataManager.Setup(dm => dm.GetEmulatorById("xenia-default")).Returns(emulator.Object);

            var settings = PlatformInstallSettingsMapper.MapRomSettings(mapping, dataManager.Object, "Microsoft Xbox 360");

            settings.EmulatorId.Should().Be("xenia-default");
            settings.EmulatorExecutablePath.Should().Be(@"D:\LaunchBox\Emulators\Xenia\xenia_canary.exe");
        }

        [Fact]
        public void MapRomSettings_NormalizesRelativeEmulatorPath_AgainstLaunchBoxRoot()
        {
            using var temp = new TempDirectory();
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", temp.Path);
            try
            {
                var mapping = new PlatformMapping
                {
                    AssociatedEmulatorId = "xenia-relative"
                };

                var emulator = new Mock<IEmulator>();
                emulator.SetupGet(e => e.Id).Returns("xenia-relative");
                emulator.SetupGet(e => e.ApplicationPath).Returns(Path.Combine("Emulators", "Xenia Canary", "xenia_canary.exe"));

                var dataManager = new Mock<IDataManager>();
                dataManager.Setup(dm => dm.GetEmulatorById("xenia-relative")).Returns(emulator.Object);

                var settings = PlatformInstallSettingsMapper.MapRomSettings(mapping, dataManager.Object, "Microsoft Xbox 360");

                settings.EmulatorExecutablePath.Should().Be(Path.GetFullPath(Path.Combine(temp.Path, "Emulators", "Xenia Canary", "xenia_canary.exe")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", null);
            }
        }

        [Fact]
        public void Map_UsesExplicitPluginSpecificFields_WhenProvided()
        {
            var mapping = new PlatformMapping
            {
                Pcsx2ExecutablePath = @"D:\Emulators\PCSX2\pcsx2.exe",
                PspEmulatorMode = "RetroArchPPSSPP",
                PpssppExecutablePath = @"D:\Emulators\PPSSPP\ppsspp.exe",
                RetroArchExecutablePath = @"D:\Emulators\RetroArch\retroarch.exe",
                RetroArchPpssppCorePath = @"cores\ppsspp_libretro.dll",
                ValidateRetroArchPpssppAssets = false,
                FailInstallIfEmulatorNotReady = true,
                Vita3kExecutablePath = @"D:\Emulators\Vita3K\Vita3K.exe",
                VitaFailIfEmulatorNotReady = true,
                VitaInstallUpdatesAutomatically = true,
                VitaInstallDlcAutomatically = true,
                SwitchEdenExecutablePath = @"D:\Emulators\Eden\eden.exe",
                AzaharExecutablePath = @"D:\Emulators\Azahar\azahar.exe",
                AzaharPlusExecutablePath = @"D:\Emulators\AzaharPlus\azaharplus.exe",
                DolphinExecutablePath = @"D:\Emulators\Dolphin\dolphin.exe",
                CemuExecutablePath = @"D:\Emulators\Cemu\cemu.exe"
            };

            var settings = PlatformInstallSettingsMapper.Map(mapping);

            settings.Pcsx2ExecutablePath.Should().Be(mapping.Pcsx2ExecutablePath);
            settings.PspEmulatorMode.Should().Be("RetroArchPPSSPP");
            settings.PpssppExecutablePath.Should().Be(mapping.PpssppExecutablePath);
            settings.RetroArchExecutablePath.Should().Be(mapping.RetroArchExecutablePath);
            settings.RetroArchPpssppCorePath.Should().Be(mapping.RetroArchPpssppCorePath);
            settings.ValidateRetroArchPpssppAssets.Should().BeFalse();
            settings.FailInstallIfEmulatorNotReady.Should().BeTrue();
            settings.Vita3kExecutablePath.Should().Be(mapping.Vita3kExecutablePath);
            settings.VitaFailIfEmulatorNotReady.Should().BeTrue();
            settings.VitaInstallUpdatesAutomatically.Should().BeTrue();
            settings.VitaInstallDlcAutomatically.Should().BeTrue();
            settings.SwitchEdenExecutablePath.Should().Be(mapping.SwitchEdenExecutablePath);
            settings.AzaharExecutablePath.Should().Be(mapping.AzaharExecutablePath);
            settings.AzaharPlusExecutablePath.Should().Be(mapping.AzaharPlusExecutablePath);
            settings.DolphinExecutablePath.Should().Be(mapping.DolphinExecutablePath);
            settings.CemuExecutablePath.Should().Be(mapping.CemuExecutablePath);
        }

        [Fact]
        public void Map_InfersPspModeAndCore_WhenExplicitValuesAreMissing()
        {
            var mapping = new PlatformMapping
            {
                AssociatedEmulatorId = "retroarch",
                EmulatorCorePath = "cores\\ppsspp_libretro.dll",
                ValidateRetroArchPpssppAssets = true,
                FailInstallIfEmulatorNotReady = false
            };

            var settings = PlatformInstallSettingsMapper.Map(mapping);

            settings.PspEmulatorMode.Should().Be("RetroArchPPSSPP");
            settings.RetroArchPpssppCorePath.Should().Be("cores\\ppsspp_libretro.dll");
            settings.ValidateRetroArchPpssppAssets.Should().BeTrue();
            settings.FailInstallIfEmulatorNotReady.Should().BeFalse();
        }
    }
}
