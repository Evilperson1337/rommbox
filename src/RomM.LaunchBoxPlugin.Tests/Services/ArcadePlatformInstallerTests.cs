using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Arcade;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class ArcadePlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_ZipRomSet_InstallsToGameSubfolder_AndKeepsArchiveIntact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var source = Path.Combine(temp.Path, "download", "metal_slug.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Metal Slug",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Metal Slug", "metal_slug.zip");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
            Directory.Exists(Path.Combine(root, "Metal Slug")).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ExtractedDirectoryWithSingleZip_SelectsCandidateRomSet()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var extracted = Path.Combine(temp.Path, "staging", "game-content");
            Directory.CreateDirectory(extracted);
            var zipPath = Path.Combine(extracted, "sf2.zip");
            await File.WriteAllTextAsync(zipPath, "zip-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Street Fighter II",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(root, "Street Fighter II", "sf2.zip"));
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_DuplicateRomSet_ReplacesExistingArchive()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var existingDirectory = Path.Combine(root, "Teenage Mutant Ninja Turtles");
            Directory.CreateDirectory(existingDirectory);
            var existing = Path.Combine(existingDirectory, "tmnt.zip");
            await File.WriteAllTextAsync(existing, "old");

            var source = Path.Combine(temp.Path, "download", "tmnt.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Teenage Mutant Ninja Turtles",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }

        [Fact]
        public async Task InstallAsync_InvalidArchiveExtension_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var source = Path.Combine(temp.Path, "download", "bad.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "bad-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Bad",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().ContainEquivalentOf(".zip");
        }

        [Fact]
        public async Task InstallAsync_MultipleRomSetCandidates_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var extracted = Path.Combine(temp.Path, "staging", "multi");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "a.zip"), "zip-a");
            await File.WriteAllTextAsync(Path.Combine(extracted, "b.zip"), "zip-b");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Multi",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().ContainEquivalentOf("Multiple Arcade ROM set archives detected");
        }

        [Fact]
        public async Task InstallAsync_MissingRomSet_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var extracted = Path.Combine(temp.Path, "staging", "empty");
            Directory.CreateDirectory(extracted);

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Missing",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
        }

        [Theory]
        [InlineData(".zip", true)]
        [InlineData(".txt", false)]
        public async Task DetectAsync_UsesInstalledPathAndZipExtension(string extension, bool expectedInstalled)
        {
            using var temp = new TempDirectory();
            var installedPath = Path.Combine(temp.Path, "Games", "Arcade", "test" + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath) ?? temp.Path);
            if (expectedInstalled)
            {
                await File.WriteAllTextAsync(installedPath, "rom-data");
            }

            var installer = new ArcadePlatformInstaller();
            var result = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "arcade",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);

            result.IsInstalled.Should().Be(expectedInstalled);
        }

        [Fact]
        public async Task VerifyAsync_InstalledZip_True_WhenMissing_False()
        {
            using var temp = new TempDirectory();
            var installedPath = Path.Combine(temp.Path, "Games", "Arcade", "mslug.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath) ?? temp.Path);
            await File.WriteAllTextAsync(installedPath, "zip-data");

            var installer = new ArcadePlatformInstaller();
            var ok = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext { InstalledPath = installedPath }, CancellationToken.None);
            var missing = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext { InstalledPath = installedPath + ".missing" }, CancellationToken.None);

            ok.IsValid.Should().BeTrue();
            missing.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task UninstallAsync_RemovesRomArchive_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Arcade");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "sf2.zip");
            await File.WriteAllTextAsync(installedPath, "rom-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Street Fighter II",
                InstalledPath = installedPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.RemovedCount.Should().Be(1);
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MameLaunchArguments_UseRomSetName()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var source = Path.Combine(temp.Path, "download", "metal_slug.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Metal Slug",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "MAME"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Contain("metal_slug");
            result.Arguments[0].Should().NotContain(".zip");
        }

        [Fact]
        public async Task InstallAsync_RetroArchLaunchArguments_UseCoreAndZipPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Arcade");
            var source = Path.Combine(temp.Path, "download", "tmnt.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new ArcadePlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "TMNT",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "RetroArch",
                    CoreName = "finalburnneo_libretro.dll"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Contain("-L");
            result.Arguments[0].Should().Contain("finalburnneo_libretro.dll");
            result.Arguments[0].Should().Contain("tmnt.zip");
        }
    }
}
