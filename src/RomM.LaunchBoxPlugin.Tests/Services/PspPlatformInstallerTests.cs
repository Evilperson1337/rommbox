using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.PSP;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class PspPlatformInstallerTests
    {
        [Theory]
        [InlineData("Crisis Core.iso")]
        [InlineData("Monster Hunter.cso")]
        [InlineData("Persona 3 Portable.chd")]
        public async Task InstallAsync_DirectArtifact_InstallsToGameDirectory(string fileName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var source = Path.Combine(temp.Path, "download", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "PPSSPP"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Game", fileName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.InstallRootPath.Should().Be(Path.Combine(root, "Game"));
            File.Exists(expectedPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedArtifact_UsesInstalledArtifactForApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            var extracted = Path.Combine(temp.Path, "staging", "Game");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            var image = Path.Combine(extracted, "Game.chd");
            await File.WriteAllTextAsync(image, "disc");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", "Game.chd"));
            result.ExecutablePath.Should().NotBe(archive);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unable to resolve PlayStation Portable launch artifact");
        }

        [Fact]
        public async Task InstallAsync_MultipleCandidates_SelectsDeterministicArtifact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var extracted = Path.Combine(temp.Path, "staging", "Game");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.chd"), "chd");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Game", "Game.chd"));
        }

        [Fact]
        public async Task InstallAsync_RetroArchMode_BuildsCoreArguments()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var source = Path.Combine(temp.Path, "download", "Game.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "iso");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    PspEmulatorMode = "RetroArchPPSSPP",
                    RetroArchPpssppCorePath = "cores\\ppsspp_libretro.dll",
                    ValidateRetroArchPpssppAssets = false
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "RetroArch"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Arguments.Should().NotBeNullOrEmpty();
            result.Arguments![0].Should().Contain("-L");
            result.Arguments[0].Should().Contain("ppsspp_libretro.dll");
        }

        [Fact]
        public async Task InstallAsync_RetroArchMode_MissingAssets_FailsWhenConfigured()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var source = Path.Combine(temp.Path, "download", "Game.iso");
            var retroArchExe = Path.Combine(temp.Path, "RetroArch", "retroarch.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(retroArchExe) ?? temp.Path);
            await File.WriteAllTextAsync(source, "iso");
            await File.WriteAllTextAsync(retroArchExe, "exe");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    PspEmulatorMode = "RetroArchPPSSPP",
                    RetroArchExecutablePath = retroArchExe,
                    FailInstallIfEmulatorNotReady = true,
                    ValidateRetroArchPpssppAssets = true
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "RetroArch"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("assets missing");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "Game.iso");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new PspPlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "psp",
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
                GameName = "Game",
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
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var existingDir = Path.Combine(installRoot, "Game");
            Directory.CreateDirectory(existingDir);
            var existing = Path.Combine(existingDir, "Game.iso");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "Game.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
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
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var source = Path.Combine(temp.Path, "download", "Game.pbp");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pbp");

            var installer = new PspPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unsupported PlayStation Portable content extension");
        }

        [Fact]
        public async Task UninstallAsync_DirectoryInstalledPath_DoesNotDeleteDirectory()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation Portable");
            var gameDir = Path.Combine(installRoot, "Game");
            Directory.CreateDirectory(gameDir);
            await File.WriteAllTextAsync(Path.Combine(gameDir, "Game.iso"), "data");

            var installer = new PspPlatformInstaller();
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

