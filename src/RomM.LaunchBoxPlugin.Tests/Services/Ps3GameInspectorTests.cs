using System;
using System.IO;
using FluentAssertions;
using RomM.Platforms.PS3.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps3GameInspectorTests
    {
        [Fact]
        public void Detects_JbFolder_Format()
        {
            using var temp = new TempDirectory();
            var root = temp.Path;
            var ps3Game = Path.Combine(root, "MyGame", "PS3_GAME");
            Directory.CreateDirectory(Path.Combine(ps3Game, "USRDIR"));
            File.WriteAllText(Path.Combine(ps3Game, "USRDIR", "EBOOT.BIN"), "");

            var inspector = new Ps3GameInspector();
            var result = inspector.Inspect(root, null);

            result.Format.Should().Be(Ps3GameFormat.JbFolder);
            result.Ps3GamePath.Should().Contain("PS3_GAME");
            result.EbootPath.Should().Contain("EBOOT.BIN");
        }

        [Fact]
        public void Detects_Iso_Format()
        {
            using var temp = new TempDirectory();
            var iso = Path.Combine(temp.Path, "game.iso");
            File.WriteAllText(iso, "");

            var inspector = new Ps3GameInspector();
            var result = inspector.Inspect(temp.Path, null);

            result.Format.Should().Be(Ps3GameFormat.DecryptedIso);
            result.IsoPath.Should().Be(iso);
        }

        [Fact]
        public void Classifies_Update_Packages_By_Folder()
        {
            using var temp = new TempDirectory();
            var updateDir = Path.Combine(temp.Path, "UPDATE");
            Directory.CreateDirectory(updateDir);
            var pkg = Path.Combine(updateDir, "BLUS12345-update.pkg");
            File.WriteAllText(pkg, "");

            var inspector = new Ps3GameInspector();
            var result = inspector.Inspect(temp.Path, null);

            result.UpdatePackages.Should().ContainSingle(entry => entry.Path == pkg);
        }

        [Fact]
        public void Extracts_TitleId_From_Rap()
        {
            using var temp = new TempDirectory();
            var rap = Path.Combine(temp.Path, "BLUS12345.rap");
            File.WriteAllText(rap, "");

            var inspector = new Ps3GameInspector();
            var result = inspector.Inspect(temp.Path, null);

            result.RapFiles.Should().ContainSingle(entry => entry.TitleId == "BLUS12345");
        }
    }
}
