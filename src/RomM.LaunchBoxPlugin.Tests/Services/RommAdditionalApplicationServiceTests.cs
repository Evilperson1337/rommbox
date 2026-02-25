using System;
using System.IO;
using FluentAssertions;
using RomMbox.Models;
using RomMbox.Services;
using RomMbox.Services.Logging;
using Moq;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class RommAdditionalApplicationServiceTests
    {
        [Fact]
        public void UpsertRommAdditionalApplicationXml_ShouldBeIdempotentForExistingId()
        {
            using var temp = new TempDirectory();
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var xml = WrapXml(BuildAdditionalAppXml("base-game", "app-id", "Play Steam Version...", 2));
            var game = CreateGame("base-game", "Windows");
            var state = new InstallState { IsInstalled = false };

            var result = RommAdditionalApplicationService.UpsertRommAdditionalApplicationXml(
                xml,
                game,
                state,
                "app-id",
                updateExisting: false,
                operationId: Guid.NewGuid().ToString("N"),
                logger: logger);

            result.Changed.Should().BeFalse();
            result.Existed.Should().BeTrue();
            result.Priority.Should().Be(2);
        }

        [Fact]
        public void UpsertRommAdditionalApplicationXml_AssignsNextPriority()
        {
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var xml = WrapXml(
                BuildAdditionalAppXml("base-game", "steam-app", "Play Steam Version...", 1)
                + BuildAdditionalAppXml("base-game", "gog-app", "Play GOG Version...", 2));
            var game = CreateGame("base-game", "Windows");
            var state = new InstallState { IsInstalled = false };

            var result = RommAdditionalApplicationService.UpsertRommAdditionalApplicationXml(
                xml,
                game,
                state,
                "romm-app",
                updateExisting: false,
                operationId: Guid.NewGuid().ToString("N"),
                logger: logger);

            result.Changed.Should().BeTrue();
            result.Priority.Should().Be(3);
            result.Xml.Should().Contain("Play RomM Version...");
        }

        [Fact]
        public void UpsertRommAdditionalApplicationXml_UpdatesExistingWhenInstalled()
        {
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var xml = WrapXml(BuildAdditionalAppXml("base-game", "romm-app", "Play RomM Version...", 1));
            var game = CreateGame("base-game", "Windows");
            var state = new InstallState
            {
                IsInstalled = true,
                RommLaunchPath = "Games\\Windows\\Game.exe",
                RommLaunchArgs = "-fullscreen"
            };

            var result = RommAdditionalApplicationService.UpsertRommAdditionalApplicationXml(
                xml,
                game,
                state,
                "romm-app",
                updateExisting: true,
                operationId: Guid.NewGuid().ToString("N"),
                logger: logger);

            result.Changed.Should().BeTrue();
            result.Xml.Should().Contain("<ApplicationPath>Games\\Windows\\Game.exe</ApplicationPath>");
            result.Xml.Should().Contain("<CommandLine>-fullscreen</CommandLine>");
            result.Xml.Should().Contain("<Installed>true</Installed>");
        }

        [Fact]
        public void InstallStateService_UsesDatabaseAdditionalAppId()
        {
            using var temp = new TempDirectory();
            using var scope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settings = new SettingsManager(logger);
            var service = new InstallStateService(logger, settings);
            service.InitializeAsync(default).GetAwaiter().GetResult();

            var gameId = Guid.NewGuid().ToString();
            var expected = Guid.NewGuid().ToString();
            service.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommAdditionalAppId = expected,
                IsInstalled = false
            }, default).GetAwaiter().GetResult();

            var actual = service.GetRommAdditionalAppIdAsync(gameId, default)
                .GetAwaiter()
                .GetResult();

            actual.Should().Be(expected);
        }

        [Fact]
        public void WritePlatformXmlSafely_CreatesBackupFile()
        {
            using var temp = new TempDirectory();
            var platformPath = temp.CreateTextFile("Data\\Platforms\\Windows.xml", WrapXml(BuildAdditionalAppXml("base-game", "steam-app", "Play Steam Version...", 1)));
            var backupPath = RommAdditionalApplicationService.CreatePlatformXmlBackup(platformPath);

            RommAdditionalApplicationService.WritePlatformXmlSafely(platformPath, WrapXml(BuildAdditionalAppXml("base-game", "steam-app", "Play Steam Version...", 2)), backupPath);

            File.Exists(backupPath).Should().BeTrue();
        }

        private static string BuildAdditionalAppXml(string gameId, string appId, string name, int priority)
        {
            return $@"
  <AdditionalApplication>
    <GogAppId />
    <OriginAppId />
    <OriginInstallPath />
    <Id>{appId}</Id>
    <PlayCount>0</PlayCount>
    <PlayTime>0</PlayTime>
    <GameID>{gameId}</GameID>
    <ApplicationPath />
    <AutoRunAfter>false</AutoRunAfter>
    <AutoRunBefore>false</AutoRunBefore>
    <CommandLine />
    <Name>{name}</Name>
    <UseDosBox>false</UseDosBox>
    <UseEmulator>false</UseEmulator>
    <WaitForExit>false</WaitForExit>
    <ReleaseDate />
    <Developer />
    <Publisher />
    <Region />
    <Version>Steam</Version>
    <Status>Imported</Status>
    <EmulatorId />
    <SideA>false</SideA>
    <SideB>false</SideB>
    <Priority>{priority}</Priority>
    <Installed>false</Installed>
    <HasCloudSynced>false</HasCloudSynced>
  </AdditionalApplication>";
        }

        private static string WrapXml(string inner)
        {
            return $"<?xml version=\"1.0\" standalone=\"yes\"?>\n<LaunchBox>\n{inner}\n</LaunchBox>";
        }

        private static IGame CreateGame(string id, string platform)
        {
            var mock = new Mock<IGame>();
            mock.SetupGet(game => game.Id).Returns(id);
            mock.SetupGet(game => game.Platform).Returns(platform);
            mock.SetupProperty(game => game.Title, "Test Game");
            mock.SetupProperty(game => game.Developer, string.Empty);
            mock.SetupProperty(game => game.Publisher, string.Empty);
            mock.SetupProperty(game => game.Region, string.Empty);
            mock.SetupProperty(game => game.ReleaseDate, null);
            return mock.Object;
        }
    }
}
