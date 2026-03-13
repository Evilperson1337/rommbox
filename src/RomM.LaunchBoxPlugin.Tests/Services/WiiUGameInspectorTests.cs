using System.IO;
using FluentAssertions;
using RomM.Platforms.WiiU.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class WiiUGameInspectorTests
    {
        [Theory]
        [InlineData("game.wud", WiiUContentFormat.Wud, false)]
        [InlineData("game.wux", WiiUContentFormat.Wux, false)]
        [InlineData("game.wua", WiiUContentFormat.Wua, false)]
        [InlineData("game.rpx", WiiUContentFormat.Rpx, false)]
        [InlineData("game.zip", WiiUContentFormat.Archive, true)]
        public void Inspect_File_DetectsExpectedFormat(string fileName, WiiUContentFormat normalized, bool requiresExtraction)
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.Path, fileName);
            File.WriteAllText(file, "data");

            var inspector = new WiiUGameInspector();
            var result = inspector.Inspect(file, null, "Test Game", null);

            result.NormalizedFormat.Should().Be(normalized);
            result.RequiresExtraction.Should().Be(requiresExtraction);
            if (requiresExtraction)
            {
                result.IsValid.Should().BeFalse();
                result.DetectedFormat.Should().Be(WiiUContentFormat.Archive);
            }
            else
            {
                result.IsValid.Should().BeTrue();
                result.LaunchArtifactPath.Should().Be(file);
            }
        }

        [Fact]
        public void Inspect_Directory_DetectsExtractedLayout_AndUpdateDlc()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            var baseRoot = Path.Combine(root, "BaseGame [000500001234ABCE]");
            Directory.CreateDirectory(Path.Combine(baseRoot, "code"));
            Directory.CreateDirectory(Path.Combine(baseRoot, "content"));
            Directory.CreateDirectory(Path.Combine(baseRoot, "meta"));
            File.WriteAllText(Path.Combine(baseRoot, "code", "game.rpx"), "rpx");

            var updateRoot = Path.Combine(root, "Update_v1.2.0_[0005000E1234ABCE]");
            Directory.CreateDirectory(Path.Combine(updateRoot, "code"));
            Directory.CreateDirectory(Path.Combine(updateRoot, "content"));
            Directory.CreateDirectory(Path.Combine(updateRoot, "meta"));
            File.WriteAllText(Path.Combine(updateRoot, "code", "upd.rpx"), "upd");

            var dlcRoot = Path.Combine(root, "DLC_Pack_[0005000C1234ABCE]");
            Directory.CreateDirectory(Path.Combine(dlcRoot, "code"));
            Directory.CreateDirectory(Path.Combine(dlcRoot, "content"));
            Directory.CreateDirectory(Path.Combine(dlcRoot, "meta"));
            File.WriteAllText(Path.Combine(dlcRoot, "code", "dlc.rpx"), "dlc");

            var inspector = new WiiUGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.SelectedFormat.Should().Be(WiiUContentFormat.ExtractedLayout);
            result.BaseGameCandidates.Should().HaveCount(1);
            result.UpdateCandidates.Should().HaveCount(1);
            result.DlcCandidates.Should().HaveCount(1);
            result.TitleId.Should().Be("000500001234ABCE");
            result.LaunchArtifactPath.Should().EndWith("game.rpx");
        }

        [Fact]
        public void Inspect_Directory_WithNoWiiUArtifacts_IsInvalid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "readme.txt"), "noop");

            var inspector = new WiiUGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No Wii U launch artifacts");
        }

        [Fact]
        public void Inspect_MultipleBaseCandidates_IsAmbiguousButValid()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(root);
            var wua = Path.Combine(root, "A.wua");
            var wux = Path.Combine(root, "B.wux");
            File.WriteAllText(wua, "a");
            File.WriteAllText(wux, "b");

            var inspector = new WiiUGameInspector();
            var result = inspector.Inspect(null, root, "Game", null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.LaunchArtifactPath.Should().Be(wua);
            result.Warnings.Should().Contain(message => message.Contains("Multiple Wii U game candidates"));
        }
    }
}

