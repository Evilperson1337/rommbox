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
    }
}
