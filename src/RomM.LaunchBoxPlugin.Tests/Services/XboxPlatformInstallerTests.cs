using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Xbox;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class XboxPlatformInstallerTests
    {
        [Theory]
        [InlineData("Halo.iso")]
        [InlineData("Halo.xiso")]
        public async Task InstallAsync_DirectArtifact_InstallsToPerGameSubfolder(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "xemu"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedGameDir = Path.Combine(root, "Halo");
            var expectedPath = Path.Combine(expectedGameDir, fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.InstallRootPath.Should().Be(expectedGameDir);
            File.Exists(expectedPath).Should().BeTrue();
            Directory.Exists(expectedGameDir).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_Cci_Fails_BecauseXemuDoesNotSupportIt()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var source = Path.Combine(temp.Path, "download", "Halo.cci");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("not supported for xemu launch");
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedArtifact_UsesInstalledArtifactForApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var archive = Path.Combine(temp.Path, "download", "Halo.zip");
            var extracted = Path.Combine(temp.Path, "staging", "Halo");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            var image = Path.Combine(extracted, "Halo.xiso");
            await File.WriteAllTextAsync(image, "disc");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Halo", "Halo.xiso"));
            result.ExecutablePath.Should().NotBe(archive);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var archive = Path.Combine(temp.Path, "download", "Halo.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unable to resolve original Xbox launch artifact");
        }

        [Fact]
        public async Task InstallAsync_UnsupportedExtractedLayout_FailsTransparently()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var extracted = Path.Combine(temp.Path, "staging", "Halo");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "default.xbe"), "xbe");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("no direct xemu launch support");
        }

        [Fact]
        public async Task InstallAsync_MultipleCandidates_SelectsDeterministicArtifact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var extracted = Path.Combine(temp.Path, "staging", "Halo");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Halo.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Halo.xiso"), "xiso");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Halo", "Halo.xiso"));
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "Halo.iso");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new XboxPlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "xbox",
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
                GameName = "Halo",
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
            var installRoot = Path.Combine(temp.Path, "Games", "Microsoft Xbox");
            var gameRoot = Path.Combine(installRoot, "Halo");
            Directory.CreateDirectory(gameRoot);
            var existing = Path.Combine(gameRoot, "Halo.iso");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "Halo.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new XboxPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
            result.InstallRootPath.Should().Be(gameRoot);
        }
    }
}

