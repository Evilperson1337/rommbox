using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Vita;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class VitaPlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_BaseVpk_CreatesTokenApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game [PCSE00120].vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Vita3K"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith(".vita3k.json");
            File.Exists(result.ExecutablePath).Should().BeTrue();
            result.Arguments.Should().NotBeNullOrEmpty();
            result.Arguments![0].Should().Contain("--title-id");
            result.Arguments[0].Should().Contain("PCSE00120");
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedVpk_UsesExtractedCandidate()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game [PCSE00120].vpk"), "pkg");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith(".vita3k.json");
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("No Vita base game candidate detected");
        }

        [Fact]
        public async Task InstallAsync_MissingTitleId_FailsSafely()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game.vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("title id");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Sony Playstation Vita", "Game");
            Directory.CreateDirectory(platformDir);
            var tokenPath = Path.Combine(platformDir, "PCSE00120.vita3k.json");
            await File.WriteAllTextAsync(tokenPath, "{\"titleId\":\"PCSE00120\",\"cachedArtifacts\":[]}");

            var installer = new VitaPlatformInstaller();
            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "psvita",
                IsInstalled = true,
                InstalledPath = tokenPath
            }, CancellationToken.None);
            detect.IsInstalled.Should().BeTrue();

            var verifyBefore = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = tokenPath
            }, CancellationToken.None);
            verifyBefore.IsValid.Should().BeTrue();

            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Game",
                InstalledPath = tokenPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            File.Exists(tokenPath).Should().BeFalse();

            var verifyAfter = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = tokenPath
            }, CancellationToken.None);
            verifyAfter.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task InstallAsync_WithUpdateAndDlcFlags_ProducesToken()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);

            await File.WriteAllTextAsync(Path.Combine(extracted, "Game [PCSE00120].vpk"), "base");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game Update [PCSE00120] v1.02.vpk"), "upd");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game DLC [PCSE00120].vpk"), "dlc");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted,
                Settings = new PlatformInstallSettings
                {
                    VitaInstallUpdatesAutomatically = true,
                    VitaInstallDlcAutomatically = true
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(result.ExecutablePath).Should().BeTrue();
            var tokenText = await File.ReadAllTextAsync(result.ExecutablePath);
            tokenText.Should().Contain("\"updatesImported\": 1");
            tokenText.Should().Contain("\"dlcImported\": 1");
        }
    }
}

