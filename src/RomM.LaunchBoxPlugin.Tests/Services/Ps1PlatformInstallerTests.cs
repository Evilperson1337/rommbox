using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.PS1;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps1PlatformInstallerTests
    {
        [Theory]
        [InlineData("Castlevania.chd")]
        [InlineData("Castlevania.iso")]
        [InlineData("Castlevania.pbp")]
        public async Task InstallAsync_SingleFileFormats_InstallToGameDirectory(string fileName)
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony PlayStation");
            var source = Path.Combine(temp.Path, "staging", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "disc");

            var installer = new Ps1PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Castlevania",
                InstallDirectory = installRoot,
                ExtractedPath = source,
                RomSettings = new RomInstallSettings
                {
                    LaunchArguments = "{rom}"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            var expected = Path.Combine(installRoot, "Castlevania", fileName);
            result.ExecutablePath.Should().Be(expected);
            File.Exists(expected).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_BinCueMultiDisc_GeneratesM3uAndUsesAsApplicationPath()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony PlayStation");
            var sourceRoot = Path.Combine(temp.Path, "staging", "ff7");
            Directory.CreateDirectory(sourceRoot);

            var cue1 = Path.Combine(sourceRoot, "Final Fantasy VII (Disc 1).cue");
            var bin1 = Path.Combine(sourceRoot, "Final Fantasy VII (Disc 1).bin");
            var cue2 = Path.Combine(sourceRoot, "Final Fantasy VII (Disc 2).cue");
            var bin2 = Path.Combine(sourceRoot, "Final Fantasy VII (Disc 2).bin");

            await File.WriteAllTextAsync(cue1, "FILE \"Final Fantasy VII (Disc 1).bin\" BINARY");
            await File.WriteAllTextAsync(bin1, "disc1");
            await File.WriteAllTextAsync(cue2, "FILE \"Final Fantasy VII (Disc 2).bin\" BINARY");
            await File.WriteAllTextAsync(bin2, "disc2");

            var installer = new Ps1PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Final Fantasy VII",
                InstallDirectory = installRoot,
                ExtractedPath = sourceRoot
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith("Final Fantasy VII.m3u");
            File.Exists(result.ExecutablePath).Should().BeTrue();
            File.ReadAllText(result.ExecutablePath).Should().Contain("Disc 1");
            File.ReadAllText(result.ExecutablePath).Should().Contain("Disc 2");
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithoutExtractedPath_ReturnsFailure()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony PlayStation");
            var archive = Path.Combine(temp.Path, "downloads", "Game.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new Ps1PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("must be extracted");
        }

        [Fact]
        public async Task DetectAsync_M3uWithMissingDisc_ReturnsNotInstalled()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony PlayStation", "Metal Gear Solid");
            Directory.CreateDirectory(root);
            var m3u = Path.Combine(root, "Metal Gear Solid.m3u");
            await File.WriteAllTextAsync(m3u, "Missing Disc.cue");

            var installer = new Ps1PlatformInstaller();
            var detection = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "ps1",
                IsInstalled = true,
                InstalledPath = m3u
            }, CancellationToken.None);

            detection.IsInstalled.Should().BeFalse();
            detection.Warnings.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task UninstallAsync_MultiFileInstall_RemovesGameDirectoryOnly()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Sony PlayStation");
            var gameRoot = Path.Combine(platformRoot, "Final Fantasy VII");
            Directory.CreateDirectory(gameRoot);
            var appPath = Path.Combine(gameRoot, "Final Fantasy VII.m3u");
            await File.WriteAllTextAsync(appPath, "disc1.cue");
            await File.WriteAllTextAsync(Path.Combine(gameRoot, "disc1.cue"), "cue");

            var installer = new Ps1PlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                InstalledPath = appPath,
                InstallRootPath = gameRoot
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            Directory.Exists(gameRoot).Should().BeFalse();
            Directory.Exists(platformRoot).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_UsesRetroArchCoreOrDuckStationArgs_WhenConfigured()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony PlayStation");
            var source = Path.Combine(temp.Path, "staging", "Ridge Racer Type 4.chd");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "disc");

            var installer = new Ps1PlatformInstaller();
            var retroResult = await installer.InstallAsync(new InstallContext
            {
                GameName = "Ridge Racer Type 4",
                InstallDirectory = installRoot,
                ExtractedPath = source,
                RomSettings = new RomInstallSettings
                {
                    LaunchArguments = "-L beetle_psx_hw_libretro.dll {rom}"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            retroResult.Success.Should().BeTrue();
            retroResult.Arguments.Should().ContainSingle();
            retroResult.Arguments[0].Should().Contain("beetle_psx_hw_libretro.dll");

            var duckResult = await installer.InstallAsync(new InstallContext
            {
                GameName = "Ridge Racer Type 4",
                InstallDirectory = installRoot,
                ExtractedPath = retroResult.ExecutablePath,
                RomSettings = new RomInstallSettings
                {
                    LaunchArguments = "{rom} --batch"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            duckResult.Success.Should().BeTrue();
            duckResult.Arguments.Should().ContainSingle();
            duckResult.Arguments[0].Should().Contain("--batch");
        }
    }
}
