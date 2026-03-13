using System.IO;
using FluentAssertions;
using RomM.Platforms.Switch.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class SwitchGameInspectorTests
    {
        [Theory]
        [InlineData("game.nsp", SwitchContentFormat.Nsp, false)]
        [InlineData("game.xci", SwitchContentFormat.Xci, false)]
        [InlineData("game.nsz", SwitchContentFormat.Nsp, true)]
        [InlineData("game.xcz", SwitchContentFormat.Xci, true)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, SwitchContentFormat normalized, bool requiresDecompression)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new SwitchGameInspector();
            var result = inspector.Inspect(file, null, "Test Game", null);

            result.IsValid.Should().BeTrue();
            result.NormalizedFormat.Should().Be(normalized);
            result.RequiresDecompression.Should().Be(requiresDecompression);
            result.LaunchArtifactPath.Should().Be(file);
        }

        [Fact]
        public void Inspect_Directory_DetectsBaseUpdateAndDlc()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Base Game [0100123412341234].nsp"), "base");
            File.WriteAllText(Path.Combine(root, "update_v1.2.0_0100123412341234.nsp"), "upd");
            File.WriteAllText(Path.Combine(root, "dlc_pack_0100123412341234.nsp"), "dlc");

            var inspector = new SwitchGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.BaseGameCandidates.Should().HaveCount(1);
            result.UpdateCandidates.Should().HaveCount(1);
            result.DlcCandidates.Should().HaveCount(1);
            result.TitleId.Should().Be("0100123412341234");
            result.UpdateCandidates[0].Version.Should().Be("1.2.0");
        }

        [Fact]
        public void Inspect_Directory_WithNoSwitchPackages_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new SwitchGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Switch packages");
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "game.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new SwitchGameInspector();
            var result = inspector.Inspect(archive, null, "Game", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(SwitchContentFormat.Archive);
        }

        [Fact]
        public void Inspect_MultipleBaseCandidates_IsAmbiguousButValid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var nsp = Path.Combine(root, "A.nsp");
            var xci = Path.Combine(root, "B.xci");
            File.WriteAllText(nsp, "a");
            File.WriteAllText(xci, "b");

            var inspector = new SwitchGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(nsp);
            result.Warnings.Should().Contain(message => message.Contains("Multiple base game candidates"));
        }
    }
}

