using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RomMbox.Models;
using RomMbox.Services;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;
using Xunit;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class RomMDeleteServiceTests
    {
        [Fact]
        public async Task DeleteOrUninstallAsync_InstallerInstallRootAlreadyGone_ReportsSuccess()
        {
            using var temp = new TempDirectory();
            using var scope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var gameId = Guid.NewGuid().ToString("N");
            var installRoot = Path.Combine(temp.Path, "Games", "Windows", "Bridge Constructor");
            var installedPath = Path.Combine(installRoot, "BridgeConstructor.exe");

            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-1",
                RommPlatformId = "windows",
                WindowsInstallType = "Installer",
                InstallRootPath = installRoot,
                InstalledPath = installedPath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns("Windows");
            game.SetupGet(g => g.Title).Returns("Bridge Constructor");
            game.SetupProperty(g => g.ApplicationPath, "Games\\Windows\\Bridge Constructor\\BridgeConstructor.exe");
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Windows");
            platform.SetupGet(p => p.Folder).Returns(Path.Combine(temp.Path, "Games", "Windows"));

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Windows")).Returns(platform.Object);
            dataManager
                .Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>()))
                .Callback<Action>(callback => callback?.Invoke());

            var service = new RomMDeleteService(logger, installStateService);
            var result = await service
                .DeleteOrUninstallAsync(game.Object, dataManager.Object, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Uninstalled.Should().BeTrue();
            game.Object.ApplicationPath.Should().BeEmpty();
            game.Object.Installed.Should().BeFalse();
            game.Object.Status.Should().Be("Available Remotely");
        }
    }
}
