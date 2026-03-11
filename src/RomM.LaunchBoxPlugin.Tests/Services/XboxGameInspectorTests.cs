using System.IO;
using FluentAssertions;
using RomM.Platforms.Xbox.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class XboxGameInspectorTests
    {
        [Theory]
        [InlineData("Halo.iso", XboxContentFormat.Iso)]
        [InlineData("Halo.xiso", XboxContentFormat.Xiso)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, XboxContentFormat format)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(file, null, "Halo", null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(format);
            result.NormalizedFormat.Should().Be(XboxContentFormat.DiscImage);
            result.LaunchArtifactPath.Should().Be(file);
        }

        [Fact]
        public void Inspect_File_Cci_IsRejected_ForXemu()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Halo.cci");
            File.WriteAllText(file, "data");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(file, null, "Halo", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not supported for xemu launch");
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Halo.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(archive, null, "Halo", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(XboxContentFormat.Archive);
        }

        [Fact]
        public void Inspect_Directory_MultipleDiscCandidates_IsAmbiguous()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var xiso = Path.Combine(root, "Halo.xiso");
            var iso = Path.Combine(root, "Halo.iso");
            File.WriteAllText(xiso, "xiso");
            File.WriteAllText(iso, "iso");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(null, root, "Halo", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(xiso);
            result.CandidateArtifacts.Should().HaveCount(2);
        }

        [Fact]
        public void Inspect_UnsupportedExtractedLayout_FailsCleanly()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var xbe = Path.Combine(root, "default.xbe");
            File.WriteAllText(xbe, "xbe");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(null, root, "Halo", null);

            result.IsValid.Should().BeFalse();
            result.ContainsUnsupportedExtractedLayout.Should().BeTrue();
            result.ErrorMessage.Should().Contain("no direct xemu launch support");
        }

        [Fact]
        public void Inspect_InvalidDirectoryWithoutXboxArtifacts_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(null, root, "Halo", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Unable to resolve original Xbox launch artifact");
        }

        [Fact]
        public void Inspect_ExtractsMetadataFromFilename_WhenPresent()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Halo_4D530004_v1.2.iso");
            File.WriteAllText(file, "data");

            var inspector = new XboxGameInspector();
            var result = inspector.Inspect(file, null, "Halo", null);

            result.IsValid.Should().BeTrue();
            result.TitleId.Should().Be("4D530004");
            result.Version.Should().Be("1.2");
            result.TitleName.Should().Contain("Halo");
        }
    }
}

