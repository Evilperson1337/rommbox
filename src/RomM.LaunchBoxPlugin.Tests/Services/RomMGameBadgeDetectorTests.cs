using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class RomMGameBadgeDetectorTests
    {
        [Fact]
        public void Detect_ShouldMatchSourceField()
        {
            var detector = CreateDetector();
            var game = CreateGame(source: "RomM");

            detector.AppliesTo(game).Should().BeTrue();
        }

        [Fact]
        public void Detect_ShouldMatchManagedAdditionalApplicationVersion()
        {
            var detector = CreateDetector();
            var game = CreateGame(additionalApplications: new[]
            {
                CreateAdditionalApplication(name: "Play Local Version...", version: "RomM", status: "Imported")
            });

            detector.AppliesTo(game).Should().BeTrue();
        }

        [Fact]
        public void Detect_ShouldMatchManagedAdditionalApplicationStatus()
        {
            var detector = CreateDetector();
            var game = CreateGame(additionalApplications: new[]
            {
                CreateAdditionalApplication(name: "Install Local Version...", version: "Other", status: "Managed by RomMbox (Disc)")
            });

            detector.AppliesTo(game).Should().BeTrue();
        }

        [Fact]
        public void Detect_ShouldMatchAdditionalApplicationName()
        {
            var detector = CreateDetector();
            var game = CreateGame(additionalApplications: new[]
            {
                CreateAdditionalApplication(name: "Play RomM Version...", version: "Other", status: "Imported")
            });

            detector.AppliesTo(game).Should().BeTrue();
        }

        [Fact]
        public void Detect_ShouldMatchKnownRommEndpointPattern()
        {
            var detector = CreateDetector();
            var game = CreateGame(additionalApplications: new[]
            {
                CreateAdditionalApplication(name: "Browser", applicationPath: "https://romm.example.com/rom/platform/game/ruffle")
            });

            detector.AppliesTo(game).Should().BeTrue();
        }

        [Fact]
        public void Detect_ShouldReturnFalse_WhenNoSignalsPresent()
        {
            var detector = CreateDetector();
            var game = CreateGame(source: "Steam", additionalApplications: new[]
            {
                CreateAdditionalApplication(name: "Play Steam Version...", version: "Steam", status: "Imported")
            });

            detector.AppliesTo(game).Should().BeFalse();
        }

        private static RomMGameBadgeDetector CreateDetector()
        {
            return new RomMGameBadgeDetector(new LoggingService(LogLevel.Debug, new StubLogSink()));
        }

        private static IGame CreateGame(string source = "", IEnumerable<IAdditionalApplication>? additionalApplications = null)
        {
            var mock = new Mock<IGame>();
            mock.SetupGet(game => game.Id).Returns(Guid.NewGuid().ToString("N"));
            mock.SetupGet(game => game.Title).Returns("Test Game");
            mock.SetupGet(game => game.Platform).Returns("Windows");
            mock.SetupGet(game => game.Source).Returns(source ?? string.Empty);
            var apps = additionalApplications == null
                ? Array.Empty<IAdditionalApplication>()
                : new List<IAdditionalApplication>(additionalApplications).ToArray();
            mock.Setup(game => game.GetAllAdditionalApplications()).Returns(apps);
            return mock.Object;
        }

        private static IAdditionalApplication CreateAdditionalApplication(string name, string version = "", string status = "", string applicationPath = "", string commandLine = "")
        {
            var mock = new Mock<IAdditionalApplication>();
            mock.SetupGet(app => app.Id).Returns(Guid.NewGuid().ToString("N"));
            mock.SetupGet(app => app.GameId).Returns(Guid.NewGuid().ToString("N"));
            mock.SetupGet(app => app.Name).Returns(name ?? string.Empty);
            mock.SetupGet(app => app.Version).Returns(version ?? string.Empty);
            mock.SetupGet(app => app.Status).Returns(status ?? string.Empty);
            mock.SetupGet(app => app.ApplicationPath).Returns(applicationPath ?? string.Empty);
            mock.SetupGet(app => app.CommandLine).Returns(commandLine ?? string.Empty);
            return mock.Object;
        }
    }
}
