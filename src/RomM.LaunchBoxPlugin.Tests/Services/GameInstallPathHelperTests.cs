using System.IO;
using FluentAssertions;
using RomM.Platforms.Abstractions.Install;

namespace RomMbox.Tests.Services
{
    public sealed class GameInstallPathHelperTests
    {
        [Fact]
        public void ResolveGameDirectory_BuildsCanonicalPlatformGameDirectory()
        {
            var platformRoot = Path.Combine("D:\\LaunchBox", "Games", "Microsoft Xbox 360");

            var result = GameInstallPathHelper.ResolveGameDirectory(platformRoot, "Banjo-Kazooie: Nuts & Bolts");

            result.Should().Be(Path.Combine(platformRoot, "Banjo-Kazooie_ Nuts & Bolts"));
        }

        [Fact]
        public void ResolveTargetFilePath_BuildsCanonicalFinalFilePath()
        {
            var platformRoot = Path.Combine("D:\\LaunchBox", "Games", "Microsoft Xbox 360");
            var sourceFile = Path.Combine(platformRoot, "Banjo-Kazooie - Nuts & Bolts.iso");

            var result = GameInstallPathHelper.ResolveTargetFilePath(platformRoot, sourceFile, "Banjo-Kazooie: Nuts & Bolts");

            result.Should().Be(Path.Combine(platformRoot, "Banjo-Kazooie_ Nuts & Bolts", "Banjo-Kazooie - Nuts & Bolts.iso"));
        }
    }
}
