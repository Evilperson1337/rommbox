using System.IO;
using FluentAssertions;
using RomM.Platforms.Wii.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class WiiGameInspectorTests
    {
        [Theory]
        [InlineData("game.iso", WiiContentFormat.Iso)]
        [InlineData("game.wbfs", WiiContentFormat.Wbfs)]
        [InlineData("game.gcz", WiiContentFormat.Gcz)]
        [InlineData("game.ciso", WiiContentFormat.Ciso)]
        [InlineData("game.wia", WiiContentFormat.Wia)]
        [InlineData("game.rvz", WiiContentFormat.Rvz)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, WiiContentFormat format)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new WiiGameInspector();
            var result = inspector.Inspect(file, null, "Test Game", null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(format);
            result.NormalizedFormat.Should().Be(WiiContentFormat.DiscImage);
            result.LaunchArtifactPath.Should().Be(file);
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "game.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new WiiGameInspector();
            var result = inspector.Inspect(archive, null, "Game", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(WiiContentFormat.Archive);
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

            var inspector = new WiiGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(rvz);
            result.Warnings.Should().Contain(message => message.Contains("Multiple Wii game candidates"));
        }

        [Fact]
        public void Inspect_Directory_WithNoWiiArtifacts_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new WiiGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Nintendo Wii artifacts");
        }

        [Fact]
        public void Inspect_ExtractsMetadataFromFilename_WhenPresent()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Mario_Kart_Wii_RMGE01_rev2.wbfs");
            File.WriteAllText(file, "data");

            var inspector = new WiiGameInspector();
            var result = inspector.Inspect(file, null, "Mario Kart Wii", null);

            result.IsValid.Should().BeTrue();
            result.TitleId.Should().Be("RMGE01");
            result.Region.Should().Be("NTSC-U");
            result.Revision.Should().Be("2");
        }
    }
}

