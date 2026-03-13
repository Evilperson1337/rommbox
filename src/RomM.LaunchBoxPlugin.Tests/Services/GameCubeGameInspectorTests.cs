using System.IO;
using FluentAssertions;
using RomM.Platforms.GameCube.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class GameCubeGameInspectorTests
    {
        [Theory]
        [InlineData("game.iso", GameCubeContentFormat.Iso)]
        [InlineData("game.gcz", GameCubeContentFormat.Gcz)]
        [InlineData("game.ciso", GameCubeContentFormat.Ciso)]
        [InlineData("game.wia", GameCubeContentFormat.Wia)]
        [InlineData("game.rvz", GameCubeContentFormat.Rvz)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, GameCubeContentFormat format)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new GameCubeGameInspector();
            var result = inspector.Inspect(file, null, "Test Game", null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(format);
            result.NormalizedFormat.Should().Be(GameCubeContentFormat.DiscImage);
            result.LaunchArtifactPath.Should().Be(file);
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "game.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new GameCubeGameInspector();
            var result = inspector.Inspect(archive, null, "Game", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(GameCubeContentFormat.Archive);
        }

        [Fact]
        public void Inspect_Directory_MultipleCandidates_IsAmbiguousAndDeterministic()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var iso = Path.Combine(root, "A.iso");
            var rvz = Path.Combine(root, "B.rvz");
            File.WriteAllText(iso, "a");
            File.WriteAllText(rvz, "b");

            var inspector = new GameCubeGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(rvz);
            result.Warnings.Should().Contain(message => message.Contains("Multiple GameCube game candidates"));
        }

        [Fact]
        public void Inspect_Directory_WithNoGameCubeArtifacts_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new GameCubeGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Nintendo GameCube artifacts");
        }

        [Fact]
        public void Inspect_ExtractsMetadataFromFilename_WhenPresent()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Super_Mario_Sunshine_GMSE01_rev1.gcz");
            File.WriteAllText(file, "data");

            var inspector = new GameCubeGameInspector();
            var result = inspector.Inspect(file, null, "Super Mario Sunshine", null);

            result.IsValid.Should().BeTrue();
            result.GameId.Should().Be("GMSE01");
            result.Region.Should().Be("NTSC-U");
            result.Revision.Should().Be("1");
        }
    }
}

