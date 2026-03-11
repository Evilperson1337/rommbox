using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Switch;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class SwitchPlatformInstallerTests
    {
        [Theory]
        [InlineData("game.nsp")]
        [InlineData("game.xci")]
        public async Task InstallAsync_DirectArtifact_InstallsToGameDirectory(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Switch");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new SwitchPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Eden"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Game", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Theory]
        [InlineData("game.nsz", "game.nsp")]
        [InlineData("game.xcz", "game.xci")]
        public async Task InstallAsync_CompressedArtifact_ProducesNormalizedExtension(string sourceName, string expectedInstalledName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Switch");
            var staging = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(staging);
            var source = Path.Combine(temp.Path, "download", sourceName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "compressed");

            var installer = new SwitchPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                StagingDirectory = staging,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", expectedInstalledName));
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_UpdateOnlyContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo Switch");
            var extracted = Path.Combine(temp.Path, "staging", "content");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "update_v1.0.0_0100ABCDEF123456.nsp"), "upd");

            var installer = new SwitchPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("base");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo Switch");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "sample.nsp");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new SwitchPlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "switch",
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
    }
}

