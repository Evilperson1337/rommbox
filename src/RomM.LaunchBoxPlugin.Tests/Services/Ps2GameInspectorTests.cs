using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.PS2.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps2GameInspectorTests
    {
        [Theory]
        [InlineData("Shadow.iso", Ps2ContentFormat.Iso)]
        [InlineData("Shadow.chd", Ps2ContentFormat.Chd)]
        [InlineData("Shadow.cso", Ps2ContentFormat.Cso)]
        [InlineData("Shadow.zso", Ps2ContentFormat.Zso)]
        [InlineData("Shadow.bin", Ps2ContentFormat.Bin)]
        public async Task Inspect_DirectFile_DetectsSupportedFormat(string fileName, Ps2ContentFormat expected)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            await File.WriteAllTextAsync(file, "disc");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Shadow", logger: null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(expected);
            result.LaunchArtifactPath.Should().Be(file);
            result.NormalizedFormat.Should().Be(Ps2ContentFormat.DiscImage);
        }

        [Fact]
        public async Task Inspect_ArchiveWithoutExtractedContent_RequiresExtraction()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Shadow.zip");
            await File.WriteAllTextAsync(archive, "zip");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(archive, extractedPath: null, gameName: "Shadow", logger: null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(Ps2ContentFormat.Archive);
            result.ErrorMessage.Should().Contain("Unable to resolve PlayStation 2 launch artifact");
        }

        [Fact]
        public async Task Inspect_ExtractedDirectoryWithMultipleCandidates_UsesDeterministicPriority()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.chd"), "chd");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.bin"), "bin");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: extracted, gameName: "Game", logger: null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(Path.Combine(extracted, "Game.chd"));
            result.Warnings.Should().Contain(message => message.Contains("Multiple PS2 game candidates"));
        }

        [Fact]
        public async Task Inspect_ExtractsMetadataFromFilename_WhenAvailable()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);
            var image = Path.Combine(extracted, "Shadow of the Colossus [SCUS-97472] (USA) rev 2.iso");
            await File.WriteAllTextAsync(image, "iso");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: extracted, gameName: "Shadow", logger: null);

            result.IsValid.Should().BeTrue();
            result.DiscId.Should().Be("SCUS-97472");
            result.Region.Should().Be("USA");
            result.Revision.Should().Be("2");
            result.TitleName.Should().Contain("Shadow of the Colossus");
        }

        [Fact]
        public async Task Inspect_UnsupportedContent_Fails()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "notes.txt");
            await File.WriteAllTextAsync(file, "text");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Unsupported PlayStation 2 content extension");
        }

        [Fact]
        public async Task Inspect_ExtractedDirectory_ParsesDiscIdFromSystemCnf_WhenFilenameLacksId()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "staging");
            var gameRoot = Path.Combine(extracted, "Shadow Folder");
            Directory.CreateDirectory(gameRoot);

            await File.WriteAllTextAsync(Path.Combine(gameRoot, "Shadow.iso"), "iso");
            await File.WriteAllTextAsync(
                Path.Combine(gameRoot, "SYSTEM.CNF"),
                "BOOT2 = cdrom0:\\SLUS_203.12;1\r\nVER = 1.00\r\n");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: extracted, gameName: "Shadow", logger: null);

            result.IsValid.Should().BeTrue();
            result.DiscId.Should().Be("SLUS-203.12");
        }

        [Theory]
        [InlineData("Game.mdf")]
        [InlineData("Game.nrg")]
        public async Task Inspect_LegacyUnsupportedDiscFormat_FailsWithConversionHint(string fileName)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            await File.WriteAllTextAsync(file, "legacy");

            var inspector = new Ps2GameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Convert to a PCSX2-supported launch format");
        }
    }
}

