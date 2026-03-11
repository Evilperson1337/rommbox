using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.PSP.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class PspGameInspectorTests
    {
        [Theory]
        [InlineData("Crisis Core.iso", PspContentFormat.Iso)]
        [InlineData("Monster Hunter.cso", PspContentFormat.Cso)]
        [InlineData("Persona 3 Portable.chd", PspContentFormat.Chd)]
        public async Task Inspect_DirectFile_DetectsSupportedFormat(string fileName, PspContentFormat expected)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            await File.WriteAllTextAsync(file, "disc");

            var inspector = new PspGameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(expected);
            result.LaunchArtifactPath.Should().Be(file);
            result.NormalizedFormat.Should().Be(PspContentFormat.DiscImage);
        }

        [Fact]
        public async Task Inspect_ArchiveWithoutExtractedContent_RequiresExtraction()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Game.zip");
            await File.WriteAllTextAsync(archive, "zip");

            var inspector = new PspGameInspector();
            var result = inspector.Inspect(archive, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(PspContentFormat.Archive);
            result.ErrorMessage.Should().Contain("Unable to resolve PlayStation Portable launch artifact");
        }

        [Fact]
        public async Task Inspect_ExtractedDirectoryWithMultipleCandidates_UsesDeterministicPriority()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.iso"), "iso");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game.chd"), "chd");

            var inspector = new PspGameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: extracted, gameName: "Game", logger: null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(Path.Combine(extracted, "Game.chd"));
            result.Warnings.Should().Contain(message => message.Contains("Multiple PSP game candidates"));
        }

        [Fact]
        public async Task Inspect_ExtractsMetadataFromFilename_WhenAvailable()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);
            var image = Path.Combine(extracted, "Crisis Core [ULUS-10336] (USA) rev 2.iso");
            await File.WriteAllTextAsync(image, "iso");

            var inspector = new PspGameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: extracted, gameName: "Crisis Core", logger: null);

            result.IsValid.Should().BeTrue();
            result.GameId.Should().Be("ULUS-10336");
            result.Region.Should().Be("USA");
            result.Revision.Should().Be("2");
            result.TitleName.Should().Contain("Crisis Core");
        }

        [Fact]
        public async Task Inspect_UnsupportedContent_Fails()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "notes.txt");
            await File.WriteAllTextAsync(file, "text");

            var inspector = new PspGameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Unsupported PlayStation Portable content extension");
        }
    }
}

