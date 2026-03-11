using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.PS2;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps2PlatformInstallerTests
    {
        [Theory]
        [InlineData("Shadow.iso")]
        [InlineData("Shadow.chd")]
        [InlineData("Shadow.cso")]
        [InlineData("Shadow.zso")]
        [InlineData("Shadow.bin")]
        public async Task InstallAsync_DirectArtifact_InstallsToGameDirectory(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "PCSX2"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Shadow", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.InstallRootPath.Should().Be(Path.Combine(root, "Shadow"));
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedArtifact_UsesInstalledArtifactForApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var archive = Path.Combine(temp.Path, "download", "Shadow.zip");
            var extracted = Path.Combine(temp.Path, "staging", "Shadow");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            var image = Path.Combine(extracted, "Shadow.chd");
            await File.WriteAllTextAsync(image, "disc");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Shadow", "Shadow.chd"));
            result.ExecutablePath.Should().NotBe(archive);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var archive = Path.Combine(temp.Path, "download", "Shadow.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unable to resolve PlayStation 2 launch artifact");
        }

        [Fact]
        public async Task InstallAsync_MultipleCandidates_SelectsDeterministicArtifact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var extracted = Path.Combine(temp.Path, "staging", "Shadow");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Shadow.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Shadow.chd"), "chd");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Shadow", "Shadow.chd"));
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "Shadow.iso");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new Ps2PlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "ps2",
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
                GameName = "Shadow",
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
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            Directory.CreateDirectory(installRoot);
            var existingDir = Path.Combine(installRoot, "Shadow");
            Directory.CreateDirectory(existingDir);
            var existing = Path.Combine(existingDir, "Shadow.iso");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "Shadow.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }

        [Fact]
        public async Task InstallAsync_UnsupportedContent_Fails()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var source = Path.Combine(temp.Path, "download", "Shadow.mdf");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "mdf");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unsupported PlayStation 2 content extension");
        }

        [Fact]
        public async Task InstallAsync_NrgContent_FailsWithConversionHint()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var source = Path.Combine(temp.Path, "download", "Shadow.nrg");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "nrg");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Shadow",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Convert to a PCSX2-supported launch format");
        }

        [Fact]
        public async Task UninstallAsync_DirectoryInstalledPath_DoesNotDeleteDirectory()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 2");
            var gameDir = Path.Combine(installRoot, "Shadow");
            Directory.CreateDirectory(gameDir);
            await File.WriteAllTextAsync(Path.Combine(gameDir, "Shadow.iso"), "data");

            var installer = new Ps2PlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Shadow",
                InstalledPath = gameDir
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            Directory.Exists(gameDir).Should().BeTrue();
            result.Notes.Should().Contain(note => note.Contains("refusing to delete directory", StringComparison.OrdinalIgnoreCase));
        }
    }
}

