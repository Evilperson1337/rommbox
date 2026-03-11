using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Xbox360;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Xbox360PlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_DirectIso_InstallsToGameDirectory()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var source = Path.Combine(temp.Path, "download", "Halo3.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "iso");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedRoot = Path.Combine(root, "Halo 3");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(expectedRoot, "Halo3.iso"));
            result.InstallRootPath.Should().Be(expectedRoot);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ExtractedLayoutWithDefaultXex_InstallsFolderAndLaunchesXex()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Halo 3");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "default.xex"), "xex");
            await File.WriteAllTextAsync(Path.Combine(extracted, "readme.txt"), "meta");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var installedRoot = Path.Combine(root, "Halo 3");
            var installedXex = Path.Combine(installedRoot, "default.xex");
            result.Success.Should().BeTrue();
            result.InstallRootPath.Should().Be(installedRoot);
            result.ExecutablePath.Should().Be(installedXex);
            File.Exists(installedXex).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_GodLayout_Unsupported_FailsTransparently()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Halo3");
            var godPath = Path.Combine(extracted, "Content", "0000000000000000", "4D5307E6");
            Directory.CreateDirectory(godPath);
            await File.WriteAllTextAsync(Path.Combine(godPath, "00007000"), "god");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("GOD/content layout");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Halo 3");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "default.xex");
            await File.WriteAllTextAsync(installedPath, "xex");

            var installer = new Xbox360PlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "xbox360",
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
                GameName = "Halo 3",
                InstalledPath = installedPath,
                InstallRootPath = gameDir
            }, new Progress<InstallProgress>(), CancellationToken.None);
            uninstall.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(gameDir).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();

            var verifyAfter = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyAfter.IsValid.Should().BeFalse();
        }
    }
}

