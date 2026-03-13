using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.GameCube;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class GameCubePlatformInstallerTests
    {
        [Theory]
        [InlineData("game.iso")]
        [InlineData("game.gcz")]
        [InlineData("game.rvz")]
        public async Task InstallAsync_DirectArtifact_InstallsToPerGameSubfolder(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new GameCubePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Dolphin"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Game", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.InstallRootPath.Should().Be(root);
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedArtifact_UsesInstalledArtifactForApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            var archive = Path.Combine(temp.Path, "download", "game.zip");
            var extracted = Path.Combine(temp.Path, "staging", "game");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            var image = Path.Combine(extracted, "game.gcz");
            await File.WriteAllTextAsync(image, "disc");

            var installer = new GameCubePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", "game.gcz"));
            result.ExecutablePath.Should().NotBe(archive);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MultipleCandidates_SelectsDeterministicArtifact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            var extracted = Path.Combine(temp.Path, "staging", "game");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "game.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "game.rvz"), "rvz");

            var installer = new GameCubePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", "game.rvz"));
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            var archive = Path.Combine(temp.Path, "download", "game.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new GameCubePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unable to resolve Nintendo GameCube launch artifact");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "sample.rvz");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new GameCubePlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "gamecube",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);
            detect.IsInstalled.Should().BeTrue();

            var verifyBefore = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyBefore.IsValid.Should().BeTrue();

            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Sample",
                InstalledPath = installedPath
            }, new Progress<InstallProgress>(), CancellationToken.None);
            uninstall.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();

            var verifyAfter = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyAfter.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task InstallAsync_DuplicateReinstall_ReplacesExistingArtifact()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo GameCube");
            var gameFolder = Path.Combine(installRoot, "Game");
            Directory.CreateDirectory(gameFolder);
            var existing = Path.Combine(gameFolder, "game.iso");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "game.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new GameCubePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }
    }
}

