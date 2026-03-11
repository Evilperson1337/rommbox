using System.IO;
using FluentAssertions;
using RomM.Platforms.N3DS.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Nintendo3DSGameInspectorTests
    {
        [Theory]
        [InlineData("game.3ds", Nintendo3DSContentFormat.ThreeDs, Nintendo3DSContentFormat.CartridgeImage, false)]
        [InlineData("game.cci", Nintendo3DSContentFormat.Cci, Nintendo3DSContentFormat.CartridgeImage, false)]
        [InlineData("game.cxi", Nintendo3DSContentFormat.Cxi, Nintendo3DSContentFormat.Cxi, false)]
        [InlineData("game.cia", Nintendo3DSContentFormat.Cia, Nintendo3DSContentFormat.Cia, true)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, Nintendo3DSContentFormat detected, Nintendo3DSContentFormat normalized, bool requiresImport)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new Nintendo3DSGameInspector();
            var result = inspector.Inspect(file, null, "Test Game", null);

            result.DetectedFormat.Should().Be(detected);
            result.NormalizedFormat.Should().Be(normalized);
            result.RequiresEmulatorImport.Should().Be(requiresImport);
            result.LaunchArtifactPath.Should().Be(requiresImport ? string.Empty : file);
            result.IsValid.Should().Be(!requiresImport);
        }

        [Fact]
        public void Inspect_ArchiveOnly_MarksExtractionRequired()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "game.zip");
            File.WriteAllText(archive, "zip");

            var inspector = new Nintendo3DSGameInspector();
            var result = inspector.Inspect(archive, null, "Game", null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(Nintendo3DSContentFormat.Archive);
        }

        [Fact]
        public void Inspect_Directory_PrefersCciOver3dsDeterministically()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var cci = Path.Combine(root, "Pokemon X.cci");
            var threeDs = Path.Combine(root, "Pokemon X.3ds");
            File.WriteAllText(cci, "cci");
            File.WriteAllText(threeDs, "3ds");

            var inspector = new Nintendo3DSGameInspector();
            var result = inspector.Inspect(null, root, "Pokemon X", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(cci);
            result.CandidateArtifacts.Should().HaveCount(2);
        }

        [Fact]
        public void Inspect_Directory_WithOnlyCia_IsDeferredImport()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Game.cia"), "cia");

            var inspector = new Nintendo3DSGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.RequiresEmulatorImport.Should().BeTrue();
            result.ErrorMessage.Should().Contain("Automatic Azahar/AzaharPlus import is not implemented");
        }

        [Fact]
        public void Inspect_Directory_WithNo3DsArtifacts_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new Nintendo3DSGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Nintendo 3DS artifacts");
        }
    }
}

