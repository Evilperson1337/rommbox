using System.IO;
using FluentAssertions;
using RomM.Platforms.Xbox360.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Xbox360GameInspectorTests
    {
        [Theory]
        [InlineData("Halo3.iso", Xbox360ContentFormat.Iso)]
        [InlineData("default.xex", Xbox360ContentFormat.Xex)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, Xbox360ContentFormat expected)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(file, null, "Halo 3", null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(expected);
            result.LaunchArtifactPath.Should().Be(file);
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Halo3.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(archive, null, "Halo 3", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(Xbox360ContentFormat.Archive);
        }

        [Fact]
        public void Inspect_Directory_MultipleCandidates_SelectsDeterministicIso()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var iso = Path.Combine(root, "Halo3.iso");
            var xex = Path.Combine(root, "default.xex");
            File.WriteAllText(iso, "iso");
            File.WriteAllText(xex, "xex");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(null, root, "Halo 3", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(iso);
        }

        [Fact]
        public void Inspect_GodLayoutWithoutDirectArtifact_FailsSafely()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            var godPath = Path.Combine(root, "Content", "0000000000000000", "4D5307E6");
            Directory.CreateDirectory(godPath);
            File.WriteAllText(Path.Combine(godPath, "00007000"), "god");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(null, root, "Halo 3", null);

            result.IsValid.Should().BeFalse();
            result.ContainsGodLayout.Should().BeTrue();
            result.ErrorMessage.Should().Contain("GOD/content layout");
        }

        [Fact]
        public void Inspect_ExtractsFilenameMetadata_WhenPresent()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Halo_3_4D5307E6_v1.2_USA_mediaidABCD1234.iso");
            File.WriteAllText(file, "iso");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(file, null, "Halo 3", null);

            result.IsValid.Should().BeTrue();
            result.TitleId.Should().Be("4D5307E6");
            result.Version.Should().Be("1.2");
            result.Region.Should().Be("USA");
            result.MediaId.Should().Be("ABCD1234");
        }

        [Fact]
        public void Inspect_Directory_WithDiscPattern_GroupsMultiDiscArtifacts()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var disc1 = Path.Combine(root, "Lost Odyssey (Disc 1).iso");
            var disc2 = Path.Combine(root, "Lost Odyssey (Disc 2).iso");
            File.WriteAllText(disc1, "disc1");
            File.WriteAllText(disc2, "disc2");

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(null, root, "Lost Odyssey", null);

            result.IsValid.Should().BeTrue();
            result.IsMultiDisc.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(disc1);
            result.AdditionalLaunchArtifacts.Should().ContainSingle().Which.Path.Should().Be(disc2);
        }

        [Fact]
        public void Inspect_Directory_DetectsDlcAndUpdateArchives()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var iso = Path.Combine(root, "Halo 3.iso");
            var updateZip = Path.Combine(root, "title_update.zip");
            var dlcZip = Path.Combine(root, "dlc_pack.zip");
            File.WriteAllText(iso, "iso");
            using (var archive = System.IO.Compression.ZipFile.Open(updateZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Content/4D5307E6/000B0000/tu.bin");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("update");
            }

            using (var archive = System.IO.Compression.ZipFile.Open(dlcZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Content/4D5307E6/00000002/dlc.bin");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("dlc");
            }

            var inspector = new Xbox360GameInspector();
            var result = inspector.Inspect(null, root, "Halo 3", null);

            result.PackageArchives.Should().HaveCount(2);
            result.PackageArchives.Should().Contain(candidate => candidate.PackageType == "update" && candidate.TitleId == "4D5307E6");
            result.PackageArchives.Should().Contain(candidate => candidate.PackageType == "dlc" && candidate.TitleId == "4D5307E6");
        }
    }
}

