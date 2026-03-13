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

        [Fact]
        public async Task DeleteOrUninstallAsync_ArcadeRomOnlyRemovesSelectedGameAssets()
        {
            using var temp = new TempDirectory();
            using var scope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var platformRoot = Path.Combine(temp.Path, "Games", "Arcade");
            var selectedGameFolder = Path.Combine(platformRoot, "1942");
            var selectedRomPath = Path.Combine(selectedGameFolder, "1942.zip");
            var selectedArchiveFolder = Path.Combine(platformRoot, "1942");
            var selectedArchivePath = Path.Combine(selectedArchiveFolder, "1942.zip");
            var otherGameFolder = Path.Combine(platformRoot, "Donkey Kong");
            var otherRomPath = Path.Combine(otherGameFolder, "dkong.zip");
            var otherArchiveFolder = Path.Combine(platformRoot, "Donkey Kong");
            var otherArchivePath = Path.Combine(otherArchiveFolder, "dkong.zip");

            Directory.CreateDirectory(platformRoot);
            Directory.CreateDirectory(selectedGameFolder);
            Directory.CreateDirectory(otherGameFolder);
            await File.WriteAllTextAsync(selectedRomPath, "1942-rom");
            await File.WriteAllTextAsync(selectedArchivePath, "1942-archive");
            await File.WriteAllTextAsync(otherRomPath, "dkong-rom");
            await File.WriteAllTextAsync(otherArchivePath, "dkong-archive");

            var gameId = Guid.NewGuid().ToString("N");
            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-1942",
                RommPlatformId = "arcade",
                WindowsInstallType = "Portable",
                InstallRootPath = selectedGameFolder,
                InstalledPath = selectedRomPath,
                ArchivePath = selectedArchivePath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns("Arcade");
            game.SetupGet(g => g.Title).Returns("1942");
            game.SetupProperty(g => g.ApplicationPath, "Games\\Arcade\\1942\\1942.zip");
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Arcade");
            platform.SetupGet(p => p.Folder).Returns(string.Empty);

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Arcade")).Returns(platform.Object);
            dataManager
                .Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>()))
                .Callback<Action>(callback => callback?.Invoke());

            var service = new RomMDeleteService(logger, installStateService);
            var result = await service.DeleteOrUninstallAsync(game.Object, dataManager.Object, CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(selectedRomPath).Should().BeFalse();
            File.Exists(selectedArchivePath).Should().BeFalse();
            Directory.Exists(selectedArchiveFolder).Should().BeFalse();
            Directory.Exists(platformRoot).Should().BeTrue();
            File.Exists(otherRomPath).Should().BeTrue();
            File.Exists(otherArchivePath).Should().BeTrue();
            Directory.Exists(otherArchiveFolder).Should().BeTrue();
            game.Object.ApplicationPath.Should().BeEmpty();
        }

        [Theory]
        [InlineData("Nintendo 64", "Army Men: Air Combat", "Army Men_ Air Combat", "Army Men - Air Combat (USA) (igdb-47694).zip")]
        [InlineData("Nintendo 3DS", "Angry Birds: Star Wars", "Angry Birds_ Star Wars", "Angry Birds Star Wars (USA) (En,Fr,Es,Pt) (igdb-4674).cci")]
        public async Task DeleteOrUninstallAsync_PerGameFolderInstallWithSanitizedTitle_RemovesEmptyGameFolderButPreservesPlatformRoot(
            string platformName,
            string gameTitle,
            string gameFolderName,
            string fileName)
        {
            using var temp = new TempDirectory();
            using var scope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var platformRoot = Path.Combine(temp.Path, "Games", platformName);
            var gameFolder = Path.Combine(platformRoot, gameFolderName);
            var installedPath = Path.Combine(gameFolder, fileName);
            var siblingFolder = Path.Combine(platformRoot, "Sibling Game");
            var siblingPath = Path.Combine(siblingFolder, "sibling.rom");

            Directory.CreateDirectory(gameFolder);
            Directory.CreateDirectory(siblingFolder);
            await File.WriteAllTextAsync(installedPath, "installed-content");
            await File.WriteAllTextAsync(siblingPath, "sibling-content");

            var gameId = Guid.NewGuid().ToString("N");
            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-1",
                RommPlatformId = platformName,
                WindowsInstallType = "Portable",
                InstallRootPath = platformRoot,
                InstalledPath = installedPath,
                ArchivePath = installedPath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns(platformName);
            game.SetupGet(g => g.Title).Returns(gameTitle);
            game.SetupProperty(g => g.ApplicationPath, Path.Combine("Games", platformName, gameFolderName, fileName));
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns(platformName);
            platform.SetupGet(p => p.Folder).Returns(string.Empty);

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName(platformName)).Returns(platform.Object);
            dataManager
                .Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>()))
                .Callback<Action>(callback => callback?.Invoke());

            var service = new RomMDeleteService(logger, installStateService);
            var result = await service.DeleteOrUninstallAsync(game.Object, dataManager.Object, CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(gameFolder).Should().BeFalse();
            Directory.Exists(platformRoot).Should().BeTrue();
            File.Exists(siblingPath).Should().BeTrue();
            Directory.Exists(siblingFolder).Should().BeTrue();
            game.Object.ApplicationPath.Should().BeEmpty();
        }

        [Fact]
        public async Task DeleteOrUninstallAsync_PerGameFolderInstallWithExtraFiles_PreservesNonEmptyGameFolder()
        {
            using var temp = new TempDirectory();
            using var scope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var platformRoot = Path.Combine(temp.Path, "Games", "Nintendo 64");
            var gameFolder = Path.Combine(platformRoot, "Army Men_ Air Combat");
            var installedPath = Path.Combine(gameFolder, "Army Men - Air Combat (USA) (igdb-47694).zip");
            var extraFilePath = Path.Combine(gameFolder, "readme.txt");

            Directory.CreateDirectory(gameFolder);
            await File.WriteAllTextAsync(installedPath, "installed-content");
            await File.WriteAllTextAsync(extraFilePath, "keep-me");

            var gameId = Guid.NewGuid().ToString("N");
            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-2",
                RommPlatformId = "n64",
                WindowsInstallType = "Portable",
                InstallRootPath = gameFolder,
                InstalledPath = installedPath,
                ArchivePath = installedPath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns("Nintendo 64");
            game.SetupGet(g => g.Title).Returns("Army Men: Air Combat");
            game.SetupProperty(g => g.ApplicationPath, Path.Combine("Games", "Nintendo 64", "Army Men_ Air Combat", "Army Men - Air Combat (USA) (igdb-47694).zip"));
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Nintendo 64");
            platform.SetupGet(p => p.Folder).Returns(string.Empty);

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Nintendo 64")).Returns(platform.Object);
            dataManager
                .Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>()))
                .Callback<Action>(callback => callback?.Invoke());

            var service = new RomMDeleteService(logger, installStateService);
            var result = await service.DeleteOrUninstallAsync(game.Object, dataManager.Object, CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            File.Exists(extraFilePath).Should().BeTrue();
            Directory.Exists(gameFolder).Should().BeTrue();
            Directory.Exists(platformRoot).Should().BeTrue();
        }
    }
}
