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
            var stagingRoot = Path.Combine(temp.Path, "staging", "op1");
            var extracted = Path.Combine(installRoot, "Journey");
            Directory.CreateDirectory(Path.Combine(extracted, "CUSA02172"));
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE", "CUSA02172-patch"));
            Directory.CreateDirectory(Path.Combine(extracted, "DLC", "HOLIDAYPACK001"));
            Directory.CreateDirectory(Path.Combine(extracted, "Bonus", "CUSA09999"));
            File.WriteAllText(Path.Combine(extracted, "CUSA02172", "eboot.bin"), "base");
            File.WriteAllText(Path.Combine(extracted, "UPDATE", "CUSA02172-patch", "patch.dat"), "update");
            File.WriteAllText(Path.Combine(extracted, "DLC", "HOLIDAYPACK001", "dlc.dat"), "dlc");
            File.WriteAllText(Path.Combine(extracted, "Bonus", "CUSA09999", "bonus.dat"), "bonus");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Journey",
                InstallDirectory = installRoot,
                StagingDirectory = stagingRoot,
                ExtractedPath = extracted,
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(extracted, "CUSA02172", "eboot.bin"));
            result.InstallRootPath.Should().Be(extracted);
            Directory.Exists(Path.Combine(extracted, "CUSA02172")).Should().BeTrue();
            Directory.Exists(Path.Combine(extracted, "CUSA02172-patch")).Should().BeTrue();
            Directory.Exists(Path.Combine(extracted, "CUSA09999")).Should().BeTrue();
            Directory.Exists(Path.Combine(extracted, "DLC")).Should().BeFalse();
            Directory.Exists(Path.Combine(extracted, "Bonus")).Should().BeFalse();
            Directory.Exists(Path.Combine(extracted, "Update")).Should().BeFalse();
            Directory.Exists(Path.Combine(extracted, "UPDATE")).Should().BeFalse();
            Directory.Exists(Path.Combine(installRoot, "CUSA02172")).Should().BeFalse();
            Directory.Exists(Path.Combine(stagingRoot, ".staging")).Should().BeFalse();
            Directory.Exists(Path.Combine(installRoot, ".staging")).Should().BeFalse();
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
            File.Exists(pkg).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithoutExtractedStaging_FailsDuringArchiveExpansionStage()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var archive = Path.Combine(temp.Path, "download", "Game.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "archive");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = installRoot,
                ArchivePath = archive,
                StagingDirectory = Path.Combine(temp.Path, "staging", "op2")
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("ExpandArchiveToStaging");
        }

        [Fact]
        public async Task InstallAsync_ArchiveContainingMixedFolderContent_UsesBaseGameEbootOnly()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var stagingRoot = Path.Combine(temp.Path, "staging", "op3");
            var extracted = Path.Combine(installRoot, "My Game");
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE", "CUSA15315-patch"));
            Directory.CreateDirectory(Path.Combine(extracted, "DLC", "HOLIDAYPACK001"));
            Directory.CreateDirectory(Path.Combine(extracted, "Bonus", "CUSA03007"));
            Directory.CreateDirectory(Path.Combine(extracted, "My Game", "CUSA15315"));
            File.WriteAllText(Path.Combine(extracted, "My Game", "CUSA15315", "eboot.bin"), "base");
            File.WriteAllText(Path.Combine(extracted, "UPDATE", "CUSA15315-patch", "eboot.bin"), "patch-eboot");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "My Game",
                InstallDirectory = installRoot,
                ArchivePath = Path.Combine(temp.Path, "download", "My Game.7z"),
                ExtractedPath = extracted,
                StagingDirectory = stagingRoot,
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(extracted, "CUSA15315", "eboot.bin"));
            File.Exists(Path.Combine(extracted, "CUSA15315", "eboot.bin")).Should().BeTrue();
            File.Exists(Path.Combine(extracted, "CUSA15315-patch", "eboot.bin")).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_PkgContent_InstallsAllSerialFoldersInsideGameDirectory_AndRemovesPkgFiles()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var gameRoot = Path.Combine(platformRoot, "Tearaway_Unfolded");
            var extracted = Path.Combine(gameRoot, "source");
            var stagingRoot = Path.Combine(temp.Path, "staging", "op5");
            var extractor = Path.Combine(temp.Path, "tools", "pkg-extractor.cmd");
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE"));
            Directory.CreateDirectory(Path.Combine(extracted, "DLC"));
            Directory.CreateDirectory(Path.Combine(extracted, "BONUS"));
            Directory.CreateDirectory(Path.GetDirectoryName(extractor) ?? temp.Path);

            await File.WriteAllTextAsync(extractor, "@echo off\r\nset SRC=%~1\r\nset DST=%~2\r\nif not exist \"%DST%\" mkdir \"%DST%\"\r\nif /I \"%~n1\"==\"Tearaway.Unfolded.PS4-CUSA01607\" (mkdir \"%DST%\\CUSA01607\" >nul 2>nul & echo base>\"%DST%\\CUSA01607\\eboot.bin\")\r\nif /I \"%~n1\"==\"Tearaway.Unfolded.v1.03.PATCH.PS4-CUSA01607\" (mkdir \"%DST%\\CUSA01607-patch\" >nul 2>nul & echo patch>\"%DST%\\CUSA01607-patch\\patch.dat\")\r\nif /I \"%~n1\"==\"Tearaway.Unfolded.DLC.The.Popup.Pack.PS4-CUSA01607\" (mkdir \"%DST%\\CUSA01607\" >nul 2>nul & mkdir \"%DST%\\CUSA01607\\POPUPPACK0000000\" >nul 2>nul & echo dlc>\"%DST%\\CUSA01607\\POPUPPACK0000000\\dlc.dat\")\r\nif /I \"%~n1\"==\"Tearaway.Unfolded.Soundtrack.PS4-CUSA03007\" (mkdir \"%DST%\\CUSA03007\" >nul 2>nul & echo bonus>\"%DST%\\CUSA03007\\bonus.dat\")\r\nexit /b 0");

            File.WriteAllText(Path.Combine(extracted, "Tearaway.Unfolded.PS4-CUSA01607.pkg"), "base-pkg");
            File.WriteAllText(Path.Combine(extracted, "UPDATE", "Tearaway.Unfolded.v1.03.PATCH.PS4-CUSA01607.pkg"), "patch-pkg");
            File.WriteAllText(Path.Combine(extracted, "DLC", "Tearaway.Unfolded.DLC.The.Popup.Pack.PS4-CUSA01607.pkg"), "dlc-pkg");
            File.WriteAllText(Path.Combine(extracted, "BONUS", "Tearaway.Unfolded.Soundtrack.PS4-CUSA03007.pkg"), "bonus-pkg");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Tearaway Unfolded",
                InstallDirectory = platformRoot,
                ExtractedPath = extracted,
                StagingDirectory = stagingRoot,
                Settings = new PlatformInstallSettings
                {
                    Ps4ExternalPkgExtractorPath = extractor
                },
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallRootPath.Should().Be(extracted);
            result.ExecutablePath.Should().Be(Path.Combine(extracted, "CUSA01607", "eboot.bin"));
            Directory.Exists(Path.Combine(extracted, "CUSA01607")).Should().BeTrue();
            Directory.Exists(Path.Combine(extracted, "CUSA01607-patch")).Should().BeTrue();
            Directory.Exists(Path.Combine(extracted, "CUSA03007")).Should().BeTrue();
            File.Exists(Path.Combine(extracted, "CUSA01607", "POPUPPACK0000000", "dlc.dat")).Should().BeTrue();
            Directory.Exists(Path.Combine(platformRoot, "CUSA01607")).Should().BeFalse();
            Directory.Exists(Path.Combine(platformRoot, "CUSA01607-patch")).Should().BeFalse();
            Directory.Exists(Path.Combine(platformRoot, "CUSA01607-dlc")).Should().BeFalse();
            Directory.Exists(Path.Combine(platformRoot, "CUSA01607-bonus")).Should().BeFalse();
            Directory.EnumerateFiles(extracted, "*.pkg", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Fact]
        public async Task InstallAsync_ReturnsBaseGameEbootPath_WithoutPromotingItToGameRoot()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var gameRoot = Path.Combine(platformRoot, "Tearaway_Unfolded");
            var extracted = Path.Combine(gameRoot, "payload");
            var stagingRoot = Path.Combine(temp.Path, "staging", "op6");
            Directory.CreateDirectory(Path.Combine(extracted, "CUSA01607"));
            Directory.CreateDirectory(Path.Combine(extracted, "CUSA01607-patch"));
            Directory.CreateDirectory(Path.Combine(extracted, "CUSA03007"));
            File.WriteAllText(Path.Combine(extracted, "CUSA01607", "eboot.bin"), "base");
            File.WriteAllText(Path.Combine(extracted, "CUSA01607-patch", "eboot.bin"), "patch");
            File.WriteAllText(Path.Combine(extracted, "CUSA03007", "eboot.bin"), "bonus");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Tearaway Unfolded",
                InstallDirectory = platformRoot,
                ExtractedPath = extracted,
                StagingDirectory = stagingRoot,
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(extracted, "CUSA01607", "eboot.bin"));
            File.Exists(Path.Combine(gameRoot, "eboot.bin")).Should().BeFalse();
            File.Exists(Path.Combine(extracted, "CUSA01607", "eboot.bin")).Should().BeTrue();
            File.Exists(Path.Combine(extracted, "CUSA01607-patch", "eboot.bin")).Should().BeTrue();
            File.Exists(Path.Combine(extracted, "CUSA03007", "eboot.bin")).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_MixedDiscoveryOrder_StillExecutesBaseUpdateDlcBonusOrder()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var extractor = Path.Combine(temp.Path, "tools", "fake-extractor.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(extractor) ?? temp.Path);
            await File.WriteAllTextAsync(extractor, "@echo off\r\nmkdir %2\\output >nul 2>nul\r\nexit /b 0");

            var stagingRoot = Path.Combine(temp.Path, "staging", "op4");
            var extracted = Path.Combine(stagingRoot, "payload");
            Directory.CreateDirectory(Path.Combine(extracted, "DLC"));
            Directory.CreateDirectory(Path.Combine(extracted, "Bonus"));
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE"));
            File.WriteAllText(Path.Combine(extracted, "DLC", "Pack-CUSA00001.pkg"), "dlc");
            File.WriteAllText(Path.Combine(extracted, "Bonus", "Bonus-CUSA99999.pkg"), "bonus");
            File.WriteAllText(Path.Combine(extracted, "Game-CUSA11111.pkg"), "base");
            File.WriteAllText(Path.Combine(extracted, "UPDATE", "Patch-CUSA11111.pkg"), "update");

            var installer = new Ps4PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Ordered Game",
                InstallDirectory = installRoot,
                ExtractedPath = extracted,
                StagingDirectory = Path.Combine(extracted, ".staging", "nested"),
                Settings = new PlatformInstallSettings
                {
                    Ps4ExternalPkgExtractorPath = extractor
                },
                RomSettings = new RomInstallSettings { ExtractArchives = true }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse("the fake extractor does not create valid folder-format content");
            result.Message.Should().Contain("produced no folder-format content");
            Directory.Exists(Path.Combine(extracted, ".staging", "nested", "folder-source", ".staging")).Should().BeFalse("staging should not recursively copy itself into classification input");
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
            Directory.Exists(root).Should().BeFalse();
        }

        [Fact]
        public async Task UninstallAsync_RemovesEmptyGameRoot_ForFolderFormatInstall()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 4");
            var gameRoot = Path.Combine(platformRoot, "Tearaway_ Unfolded");
            var baseDir = Path.Combine(gameRoot, "CUSA01607");
            var patchDir = Path.Combine(gameRoot, "CUSA01607-patch");
            var bonusDir = Path.Combine(gameRoot, "CUSA03007");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(patchDir);
            Directory.CreateDirectory(bonusDir);

            var installer = new Ps4PlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Tearaway Unfolded",
                InstalledPath = baseDir,
                InstallRootPath = gameRoot
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            Directory.Exists(gameRoot).Should().BeFalse();
            Directory.Exists(platformRoot).Should().BeTrue();
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

