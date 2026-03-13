using System.IO;
using FluentAssertions;
using RomM.Platforms.PS1.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps1GameInspectorTests
    {
        [Fact]
        public async Task Inspect_DetectsSingleFileChd()
        {
            using var temp = new TempDirectory();
            var chd = Path.Combine(temp.Path, "Castlevania.chd");
            await File.WriteAllTextAsync(chd, "disc");

            var inspector = new Ps1GameInspector();
            var result = inspector.Inspect(chd, "Castlevania", logger: null);

            result.Format.Should().Be(Ps1ContentFormat.Chd);
            result.LaunchFilePath.Should().Be(chd);
            result.IsMultiDisc.Should().BeFalse();
        }

        [Fact]
        public async Task Inspect_DetectsCueBinMultiDisc_AndWarningsForMissingCompanions()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "ff7");
            Directory.CreateDirectory(root);

            var cue1 = Path.Combine(root, "Final Fantasy VII (Disc 1).cue");
            var bin1 = Path.Combine(root, "Final Fantasy VII (Disc 1).bin");
            await File.WriteAllTextAsync(bin1, "bin1");
            await File.WriteAllTextAsync(cue1, "FILE \"Final Fantasy VII (Disc 1).bin\" BINARY");

            var cue2 = Path.Combine(root, "Final Fantasy VII (Disc 2).cue");
            await File.WriteAllTextAsync(cue2, "FILE \"Final Fantasy VII (Disc 2).bin\" BINARY");

            var inspector = new Ps1GameInspector();
            var result = inspector.Inspect(root, "Final Fantasy VII", logger: null);

            result.Format.Should().Be(Ps1ContentFormat.CueBin);
            result.IsMultiDisc.Should().BeTrue();
            result.DiscFiles.Should().HaveCount(2);
            result.Warnings.Should().Contain(message => message.Contains("companion", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Inspect_DetectsPlaylistM3u()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "mgs");
            Directory.CreateDirectory(root);

            var cue1 = Path.Combine(root, "Metal Gear Solid (Disc 1).cue");
            var cue2 = Path.Combine(root, "Metal Gear Solid (Disc 2).cue");
            await File.WriteAllTextAsync(cue1, "dummy");
            await File.WriteAllTextAsync(cue2, "dummy");

            var m3u = Path.Combine(root, "Metal Gear Solid.m3u");
            await File.WriteAllLinesAsync(m3u, new[]
            {
                "Metal Gear Solid (Disc 1).cue",
                "Metal Gear Solid (Disc 2).cue"
            });

            var inspector = new Ps1GameInspector();
            var result = inspector.Inspect(root, "Metal Gear Solid", logger: null);

            result.Format.Should().Be(Ps1ContentFormat.PlaylistM3u);
            result.LaunchFilePath.Should().Be(m3u);
            result.IsMultiDisc.Should().BeTrue();
            result.DiscFiles.Should().HaveCount(2);
        }
    }
}
