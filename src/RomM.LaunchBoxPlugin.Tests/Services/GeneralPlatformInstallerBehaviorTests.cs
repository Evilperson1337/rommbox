using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.General;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class GeneralPlatformInstallerBehaviorTests
    {
        [Fact]
        public async Task InstallAsync_UsesDownloadedZipAsFinalArtifact_AndApplicationPath()
        {
            using var temp = new TempDirectory();
            var download = Path.Combine(temp.Path, "download", "Adventure Island.zip");
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo Entertainment System");
            Directory.CreateDirectory(Path.GetDirectoryName(download) ?? temp.Path);
            await File.WriteAllTextAsync(download, "zip-content");

            var installer = new GeneralPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Adventure Island",
                InstallDirectory = installRoot,
                ArchivePath = download,
                RomSettings = new RomInstallSettings
                {
                    SupportedFileTypes = ".zip,.nes",
                    InstallLayoutMode = "UsePlatformRoot"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expected = Path.Combine(installRoot, "Adventure Island", "Adventure Island.zip");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expected);
            File.Exists(expected).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_UsesDownloadedRomAsFinalArtifact_AndApplicationPath()
        {
            using var temp = new TempDirectory();
            var download = Path.Combine(temp.Path, "download", "Adventure Island.nes");
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo Entertainment System");
            Directory.CreateDirectory(Path.GetDirectoryName(download) ?? temp.Path);
            await File.WriteAllTextAsync(download, "rom-content");

            var installer = new GeneralPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Adventure Island",
                InstallDirectory = installRoot,
                ArchivePath = download,
                RomSettings = new RomInstallSettings
                {
                    SupportedFileTypes = ".nes,.zip",
                    InstallLayoutMode = "UsePlatformRoot"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expected = Path.Combine(installRoot, "Adventure Island", "Adventure Island.nes");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expected);
            File.Exists(expected).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_RejectsArtifact_WhenExtensionIsNotSupported()
        {
            using var temp = new TempDirectory();
            var download = Path.Combine(temp.Path, "download", "Game.iso");
            var installRoot = Path.Combine(temp.Path, "Games", "Platform");
            Directory.CreateDirectory(Path.GetDirectoryName(download) ?? temp.Path);
            await File.WriteAllTextAsync(download, "iso-content");

            var installer = new GeneralPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = download,
                RomSettings = new RomInstallSettings
                {
                    SupportedFileTypes = ".nes,.zip"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("not allowed");
        }

        [Fact]
        public async Task InstallAsync_PrefersArchivePath_WhenExtractedPathAlsoProvided()
        {
            using var temp = new TempDirectory();
            var download = Path.Combine(temp.Path, "download", "Game.zip");
            var extracted = Path.Combine(temp.Path, "staging", "Game.nes");
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo Entertainment System");
            Directory.CreateDirectory(Path.GetDirectoryName(download) ?? temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(extracted) ?? temp.Path);
            await File.WriteAllTextAsync(download, "zip-content");
            await File.WriteAllTextAsync(extracted, "rom-content");

            var installer = new GeneralPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = download,
                ExtractedPath = extracted,
                RomSettings = new RomInstallSettings
                {
                    SupportedFileTypes = ".zip,.nes",
                    ArchiveHandlingMode = "ExtractAlways"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith("Game.zip");
            File.Exists(Path.Combine(installRoot, "Game", "Game.zip")).Should().BeTrue();
        }
    }
}

