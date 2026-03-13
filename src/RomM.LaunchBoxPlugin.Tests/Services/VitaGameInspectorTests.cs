using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Vita.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class VitaGameInspectorTests
    {
        [Fact]
        public async Task Inspect_Vpk_DetectsBaseGame()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "Persona4Golden [PCSE00120].vpk");
            await File.WriteAllTextAsync(file, "pkg");

            var inspector = new VitaGameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Persona 4 Golden", logger: null);

            result.IsValid.Should().BeTrue();
            result.DetectedFormat.Should().Be(VitaContentFormat.Vpk);
            result.ContentRole.Should().Be(VitaContentRole.BaseGame);
            result.TitleId.Should().Be("PCSE00120");
            result.InstallArtifactPath.Should().Be(file);
        }

        [Fact]
        public async Task Inspect_ArchiveWithoutExtractedContent_RequiresExtraction()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Game.zip");
            await File.WriteAllTextAsync(archive, "zip");

            var inspector = new VitaGameInspector();
            var result = inspector.Inspect(archive, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.DetectedFormat.Should().Be(VitaContentFormat.Archive);
            result.ErrorMessage.Should().Contain("No Vita base game candidate detected");
        }

        [Fact]
        public async Task Inspect_Directory_WithBaseUpdateDlc_OrdersByRole()
        {
            using var temp = new TempDirectory();
            var staged = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(staged);

            var basePkg = Path.Combine(staged, "Game [PCSE00120].vpk");
            var updatePkg = Path.Combine(staged, "Game Update [PCSE00120] v1.02.vpk");
            var dlcPkg = Path.Combine(staged, "Game DLC Pack [PCSE00120].vpk");

            await File.WriteAllTextAsync(basePkg, "base");
            await File.WriteAllTextAsync(updatePkg, "upd");
            await File.WriteAllTextAsync(dlcPkg, "dlc");

            var inspector = new VitaGameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: staged, gameName: "Game", logger: null);

            result.IsValid.Should().BeTrue();
            result.OrderedInstallCandidates.Should().HaveCount(3);
            result.OrderedInstallCandidates[0].Role.Should().Be(VitaContentRole.BaseGame);
            result.OrderedInstallCandidates[1].Role.Should().Be(VitaContentRole.Update);
            result.OrderedInstallCandidates[2].Role.Should().Be(VitaContentRole.Dlc);
        }

        [Fact]
        public async Task Inspect_ExtractedLayout_DetectsCandidate()
        {
            using var temp = new TempDirectory();
            var appRoot = Path.Combine(temp.Path, "staging", "app", "PCSE00120");
            var sceSys = Path.Combine(appRoot, "sce_sys");
            Directory.CreateDirectory(sceSys);
            await File.WriteAllBytesAsync(Path.Combine(sceSys, "param.sfo"), new byte[] { 1, 2, 3 });

            var inspector = new VitaGameInspector();
            var result = inspector.Inspect(archivePath: null, extractedPath: Path.Combine(temp.Path, "staging"), gameName: "Game", logger: null);

            result.IsValid.Should().BeTrue();
            result.InstallArtifactPath.Should().NotBeNullOrWhiteSpace();
            result.CandidateArtifacts.Should().Contain(c => c.Format == VitaContentFormat.ExtractedLayout);
        }

        [Fact]
        public async Task Inspect_UnsupportedContent_Fails()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, "notes.txt");
            await File.WriteAllTextAsync(file, "text");

            var inspector = new VitaGameInspector();
            var result = inspector.Inspect(file, extractedPath: null, gameName: "Game", logger: null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Vita base game candidate detected");
        }
    }
}

