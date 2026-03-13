using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.PS3;
using RomM.Platforms.Abstractions.Models.Install;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps3PlatformInstallerIsoInstallTests
    {
        [Fact]
        public async Task InstallAsync_DecryptedIso_AlreadyInFinalFolder_DoesNotCreateRootIso()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 3");
            var gameFolder = Path.Combine(installRoot, "Warhawk");
            Directory.CreateDirectory(gameFolder);

            var isoPath = Path.Combine(gameFolder, "Warhawk [BCUS98117].iso");
            await File.WriteAllTextAsync(isoPath, "iso");

            var installer = new Ps3PlatformInstaller();
            var context = new InstallContext
            {
                GameName = "Warhawk",
                InstallDirectory = installRoot,
                ExtractedPath = gameFolder
            };

            var result = await installer.InstallAsync(context, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(isoPath);
            File.Exists(isoPath).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "Warhawk.iso")).Should().BeFalse();
        }

        [Fact]
        public async Task InstallAsync_DecryptedIso_FromStaging_MovesIntoGameFolderOnly()
        {
            using var temp = new TempDirectory();
            var installRoot = Path.Combine(temp.Path, "Games", "Sony Playstation 3");
            var stagingRoot = Path.Combine(temp.Path, "staging", "Warhawk");
            Directory.CreateDirectory(stagingRoot);

            var sourceIso = Path.Combine(stagingRoot, "Warhawk [BCUS98117].iso");
            await File.WriteAllTextAsync(sourceIso, "iso");

            var installer = new Ps3PlatformInstaller();
            var context = new InstallContext
            {
                GameName = "Warhawk",
                InstallDirectory = installRoot,
                ExtractedPath = stagingRoot
            };

            var result = await installer.InstallAsync(context, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedIso = Path.Combine(installRoot, "Warhawk", "Warhawk [BCUS98117].iso");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedIso);
            File.Exists(expectedIso).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "Warhawk.iso")).Should().BeFalse();
            File.Exists(sourceIso).Should().BeFalse();
        }
    }
}
