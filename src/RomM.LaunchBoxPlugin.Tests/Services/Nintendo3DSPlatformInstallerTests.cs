using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.N3DS;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Nintendo3DSPlatformInstallerTests
    {
        [Fact]
        public void Metadata_DoesNotClaim_PlayStation2_RommPlatformId()
        {
            var installer = new Nintendo3DSPlatformInstaller();

            installer.SupportedPlatformIds.Should().NotContain("17");
        }

        [Theory]
        [InlineData("Pokemon X.cci")]
        [InlineData("Pokemon X.3ds")]
        [InlineData("Pokemon X.cxi")]
        public async Task InstallAsync_DirectArtifact_InstallsToPerGameSubfolder(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Pokemon X",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Azahar"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Pokemon X", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ArchiveContainingCci_InstallsSelectedArtifact()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            var archive = Path.Combine(temp.Path, "download", "PokemonX.zip");
            var extracted = Path.Combine(temp.Path, "staging", "PokemonX");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            var cci = Path.Combine(extracted, "Pokemon X.cci");
            await File.WriteAllTextAsync(cci, "cci");

            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Pokemon X",
                InstallDirectory = installRoot,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(installRoot, "Pokemon X", "Pokemon X.cci"));
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_DuplicateReinstall_ReplacesExistingArtifact()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            var gameFolder = Path.Combine(installRoot, "Pokemon X");
            Directory.CreateDirectory(gameFolder);
            var existing = Path.Combine(gameFolder, "Pokemon X.cci");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "Pokemon X.cci");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Pokemon X",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }

        [Fact]
        public async Task InstallAsync_CiaOnly_FailsWithoutFakeInstall()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            var source = Path.Combine(temp.Path, "download", "Pokemon X.cia");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "cia");

            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Pokemon X",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Automatic Azahar/AzaharPlus import is not implemented");
        }

        [Theory]
        [InlineData("Pokemon X.cci", true)]
        [InlineData("Pokemon X.cxi", true)]
        [InlineData("Pokemon X.txt", false)]
        public async Task DetectAndVerify_RespectSupportedInstalledArtifacts(string fileName, bool expectedInstalled)
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, fileName);
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new Nintendo3DSPlatformInstaller();
            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "3ds",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);

            detect.IsInstalled.Should().Be(expectedInstalled);

            var verify = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verify.IsValid.Should().Be(expectedInstalled);
        }

        [Fact]
        public async Task Uninstall_RemovesArtifact_PreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "Pokemon X.cci");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new Nintendo3DSPlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Pokemon X",
                InstalledPath = installedPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MissingInstallDirectory_Fails()
        {
            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Pokemon X",
                InstallDirectory = string.Empty,
                ArchivePath = "missing.cci"
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("install directory");
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithMixedFormats_PrefersDirectLaunchArtifactOverCia()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Nintendo 3DS");
            var extracted = Path.Combine(temp.Path, "staging", "Mixed");
            Directory.CreateDirectory(extracted);

            var direct = Path.Combine(extracted, "Game.cci");
            var cia = Path.Combine(extracted, "Game.cia");
            await File.WriteAllTextAsync(direct, "direct");
            await File.WriteAllTextAsync(cia, "cia");

            var installer = new Nintendo3DSPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(installRoot, "Game", "Game.cci"));
        }
    }
}

