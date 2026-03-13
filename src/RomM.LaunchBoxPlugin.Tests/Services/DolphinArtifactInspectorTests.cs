using System.IO;
using FluentAssertions;
using RomM.Platforms.DolphinInternal;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class DolphinArtifactInspectorTests
    {
        [Fact]
        public void Inspect_Directory_SelectsPreferredExtensionDeterministically()
        {
            using var temp = new TempDirectory();
            var staged = Path.Combine(temp.Path, "staged");
            Directory.CreateDirectory(staged);
            var iso = Path.Combine(staged, "A.iso");
            var rvz = Path.Combine(staged, "B.rvz");
            File.WriteAllText(iso, "iso");
            File.WriteAllText(rvz, "rvz");

            var policy = new DolphinPlatformPolicy
            {
                SupportedExtensions = new[] { ".iso", ".rvz" },
                ArchiveExtensions = new[] { ".zip", ".7z", ".rar" },
                PreferredExtensions = new[] { ".rvz", ".iso" },
                AllowRecursiveSearch = true,
                RequireSingleCanonicalArtifact = false
            };

            var result = new DolphinArtifactInspector().Inspect(null, staged, policy, null);

            result.IsValid.Should().BeTrue();
            result.IsAmbiguous.Should().BeTrue();
            result.CanonicalArtifactPath.Should().Be(rvz);
            result.Warnings.Should().Contain(message => message.Contains("Multiple candidate artifacts detected"));
        }

        [Fact]
        public void Inspect_ArchiveWithoutExtraction_RequiresExtraction()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "game.zip");
            File.WriteAllText(archive, "zip");

            var policy = new DolphinPlatformPolicy
            {
                SupportedExtensions = new[] { ".iso", ".rvz" },
                ArchiveExtensions = new[] { ".zip", ".7z", ".rar" },
                PreferredExtensions = new[] { ".rvz", ".iso" },
                AllowRecursiveSearch = true,
                RequireSingleCanonicalArtifact = false
            };

            var result = new DolphinArtifactInspector().Inspect(archive, null, policy, null);

            result.IsValid.Should().BeFalse();
            result.RequiresExtraction.Should().BeTrue();
            result.ErrorMessage.Should().Be("Unable to resolve canonical Dolphin launch artifact.");
        }
    }
}

