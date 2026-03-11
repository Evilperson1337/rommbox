using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.WiiU;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class WiiUPlatformInstallerTests
    {
        [Theory]
        [InlineData("game.wud")]
        [InlineData("game.wux")]
        [InlineData("game.wua")]
        [InlineData("game.rpx")]
        public async Task InstallAsync_DirectArtifact_InstallsToPerGameSubfolder(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Wii U");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new WiiUPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Cemu"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Game", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.InstallRootPath.Should().Be(root);
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ExtractedLayout_InstallsLayoutAndSetsRpxAsApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Wii U");
            var extracted = Path.Combine(temp.Path, "staging", "BaseGame [000500001234ABCE]");
            Directory.CreateDirectory(Path.Combine(extracted, "code"));
            Directory.CreateDirectory(Path.Combine(extracted, "content"));
            Directory.CreateDirectory(Path.Combine(extracted, "meta"));
            var rpx = Path.Combine(extracted, "code", "game.rpx");
            await File.WriteAllTextAsync(rpx, "rpx");
            await File.WriteAllTextAsync(Path.Combine(extracted, "content", "dummy.bin"), "dummy");

            var installer = new WiiUPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", "BaseGame [000500001234ABCE]", "code", "game.rpx"));
            File.Exists(result.ExecutablePath).Should().BeTrue();
            Directory.Exists(Path.Combine(root, "Game", "BaseGame [000500001234ABCE]", "content")).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_UpdateOnlyContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Wii U");
            var extracted = Path.Combine(temp.Path, "staging", "Update_v1.2.0_[0005000E1234ABCE]");
            Directory.CreateDirectory(Path.Combine(extracted, "code"));
            Directory.CreateDirectory(Path.Combine(extracted, "content"));
            Directory.CreateDirectory(Path.Combine(extracted, "meta"));
            await File.WriteAllTextAsync(Path.Combine(extracted, "code", "upd.rpx"), "upd");

            var installer = new WiiUPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("base game artifact");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo Wii U");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "sample.wua");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new WiiUPlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "wiiu",
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
        public async Task UninstallAsync_DirectoryInstalledPath_DoesNotDeleteDirectory()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo Wii U");
            var gameDir = Path.Combine(installRoot, "Game");
            Directory.CreateDirectory(gameDir);
            await File.WriteAllTextAsync(Path.Combine(gameDir, "code.rpx"), "data");

            var installer = new WiiUPlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Game",
                InstalledPath = gameDir
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            Directory.Exists(gameDir).Should().BeTrue();
            result.Notes.Should().Contain(note => note.Contains("refusing to delete directory", StringComparison.OrdinalIgnoreCase));
        }
    }
}

