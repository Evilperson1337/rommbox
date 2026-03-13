using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using RomM.Platforms.DolphinInternal;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class DolphinInstallHelpersTests
    {
        [Fact]
        public void BuildLaunchArguments_DefaultTemplate_QuotesRomPath()
        {
            var romPath = @"C:\Games\Nintendo Wii\Mario Kart Wii.rvz";

            var args = DolphinInstallHelpers.BuildLaunchArguments(null, romPath);

            args.Should().Be("\"C:\\Games\\Nintendo Wii\\Mario Kart Wii.rvz\"");
        }

        [Fact]
        public void DeleteInstalledArtifactOnly_DirectoryPath_IsPreserved()
        {
            using var temp = new TempDirectory();
            var installedDirectory = Path.Combine(temp.Path, "Nintendo Wii");
            Directory.CreateDirectory(installedDirectory);
            var notes = new List<string>();

            var removed = DolphinInstallHelpers.DeleteInstalledArtifactOnly(installedDirectory, "Nintendo Wii", null, notes);

            removed.Should().Be(0);
            Directory.Exists(installedDirectory).Should().BeTrue();
            notes.Should().ContainSingle(note => note.Contains("refusing to delete directory"));
        }
    }
}

