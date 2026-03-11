using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.PS4;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps4PlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_ExtractedFolderLayout_InstallsSerialFoldersAndResolvesApplicationPath()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var extracted = Path.Combine(temp.Path, "staging", "Journey");
            Directory.CreateDirectory(Path.Combine(extracted, "CUSA02172"));
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE", "CUSA02172"));
            Directory.CreateDirectory(Path.Combine(extracted, "DLC", "CUSA02172"));
            File.WriteAllText(Path.Combine(extracted, "CUSA02172", "eboot.bin"), "base");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Journey",
                InstallDirectory = installRoot,
                ExtractedPath = extracted,
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(installRoot, "CUSA02172"));
            Directory.Exists(Path.Combine(installRoot, "CUSA02172")).Should().BeTrue();
            Directory.Exists(Path.Combine(installRoot, "CUSA02172-patch")).Should().BeTrue();
            Directory.Exists(Path.Combine(installRoot, "CUSA02172-dlc")).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_DirectPkgWithoutExtractor_FailsWhenConfiguredToFail()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var pkg = Path.Combine(temp.Path, "download", "Bloodborne.pkg");
            Directory.CreateDirectory(Path.GetDirectoryName(pkg) ?? temp.Path);
            await File.WriteAllTextAsync(pkg, "pkg");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Bloodborne",
                InstallDirectory = installRoot,
                ArchivePath = pkg,
                Settings = new PlatformInstallSettings
                {
                    Ps4FailIfDirectPkgExtractorMissing = true,
                    Ps4ExternalPkgExtractorPath = string.Empty
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("extractor");
        }

        [Fact]
        public async Task UninstallAsync_RemovesOwnedSerialFolders_AndPreservesPlatformRoot()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            Directory.CreateDirectory(root);
            var baseDir = Path.Combine(root, "CUSA02172");
            var patchDir = Path.Combine(root, "CUSA02172-patch");
            var dlcDir = Path.Combine(root, "CUSA02172-dlc");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(patchDir);
            Directory.CreateDirectory(dlcDir);

            var installer = new Ps4PlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Journey",
                InstalledPath = baseDir,
                InstallRootPath = root
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            Directory.Exists(baseDir).Should().BeFalse();
            Directory.Exists(patchDir).Should().BeFalse();
            Directory.Exists(dlcDir).Should().BeFalse();
            Directory.Exists(root).Should().BeTrue();
        }

        [Fact]
        public async Task DetectAndVerify_InstalledBaseFolder_ReturnInstalled()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var baseDir = Path.Combine(root, "CUSA02172");
            Directory.CreateDirectory(baseDir);

            var installer = new Ps4PlatformInstaller();
            var detection = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "ps4",
                IsInstalled = true,
                InstalledPath = baseDir
            }, CancellationToken.None);

            detection.IsInstalled.Should().BeTrue();
            detection.RecommendedExecutablePath.Should().Be(baseDir);

            var verify = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = baseDir
            }, CancellationToken.None);

            verify.IsValid.Should().BeTrue();
        }
    }
}

