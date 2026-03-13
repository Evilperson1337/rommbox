using System.IO;
using FluentAssertions;
using RomM.Platforms.PS4.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Ps4GameInspectorTests
    {
        [Fact]
        public void Inspect_ExtractedFolderLayout_ClassifiesBaseUpdateAndDlc()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Journey");
            Directory.CreateDirectory(Path.Combine(root, "CUSA02172"));
            Directory.CreateDirectory(Path.Combine(root, "UPDATE", "CUSA02172"));
            Directory.CreateDirectory(Path.Combine(root, "DLC", "HOLIDAYPACK001"));
            Directory.CreateDirectory(Path.Combine(root, "Bonus", "CUSA09999"));
            File.WriteAllText(Path.Combine(root, "CUSA02172", "eboot.bin"), "base");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: null,
                extractedPath: root,
                gameName: "Journey",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = true,
                    DirectPkgSupportEnabled = false,
                    AllowExtractedPkgInstall = false
                },
                logger: null);

            result.IsValid.Should().BeTrue();
            result.TitleId.Should().Be("CUSA02172");
            result.BaseGameItems.Should().ContainSingle();
            result.UpdateItems.Should().ContainSingle();
            result.DlcItems.Should().ContainSingle();
            result.BonusItems.Should().ContainSingle();
        }

        [Fact]
        public void Inspect_DirectPkg_WithExtractorEnabled_IsSupported()
        {
            using var temp = new TempDirectory();
            var pkg = Path.Combine(temp.Path, "Bloodborne_CUSA03173.pkg");
            File.WriteAllText(pkg, "pkg");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: pkg,
                extractedPath: null,
                gameName: "Bloodborne",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = false,
                    DirectPkgSupportEnabled = true,
                    AllowExtractedPkgInstall = false
                },
                logger: null);

            result.IsValid.Should().BeTrue();
            result.IsDirectPkgDownload.Should().BeTrue();
            result.BaseGameItems.Should().ContainSingle();
            result.BaseGameItems[0].ContentFormat.Should().Be(Ps4ContentFormat.Pkg);
            result.BaseGameItems[0].IsSupportedForInstall.Should().BeTrue();
            result.TitleId.Should().Be("CUSA03173");
        }

        [Fact]
        public void Inspect_DirectPkg_WithoutExtractor_IsRejected()
        {
            using var temp = new TempDirectory();
            var pkg = Path.Combine(temp.Path, "Bloodborne.pkg");
            File.WriteAllText(pkg, "pkg");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: pkg,
                extractedPath: null,
                gameName: "Bloodborne",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = false,
                    DirectPkgSupportEnabled = false,
                    AllowExtractedPkgInstall = false
                },
                logger: null);

            result.IsValid.Should().BeFalse();
            result.Warnings.Should().Contain(message => message.Contains("PKG support is disabled"));
        }

        [Fact]
        public void Inspect_ExtractedArchivePkg_IsNotAutomaticallySupported()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "Bloodborne");
            Directory.CreateDirectory(Path.Combine(extracted, "DLC"));
            File.WriteAllText(Path.Combine(extracted, "Bloodborne.pkg"), "pkg");
            File.WriteAllText(Path.Combine(extracted, "DLC", "dlc_1.pkg"), "pkg");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: Path.Combine(temp.Path, "Bloodborne.zip"),
                extractedPath: extracted,
                gameName: "Bloodborne",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = true,
                    DirectPkgSupportEnabled = true,
                    AllowExtractedPkgInstall = false
                },
                logger: null);

            result.IsValid.Should().BeFalse();
            result.DetectedItems.Should().NotBeEmpty();
            result.DetectedItems.Should().OnlyContain(item => !item.IsSupportedForInstall);
        }

        [Fact]
        public void Inspect_ExtractedArchivePkg_WithExtractorEnabled_IsSupported()
        {
            using var temp = new TempDirectory();
            var extracted = Path.Combine(temp.Path, "MetalSlugXX");
            Directory.CreateDirectory(extracted);
            File.WriteAllText(Path.Combine(extracted, "Metal Slug XX [CUSA11740].pkg"), "pkg");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: Path.Combine(temp.Path, "Metal Slug XX.rar"),
                extractedPath: extracted,
                gameName: "Metal Slug XX",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = true,
                    DirectPkgSupportEnabled = true,
                    AllowExtractedPkgInstall = true
                },
                logger: null);

            result.IsValid.Should().BeTrue();
            result.BaseGameItems.Should().ContainSingle();
            result.BaseGameItems[0].ContentFormat.Should().Be(Ps4ContentFormat.Pkg);
            result.BaseGameItems[0].IsSupportedForInstall.Should().BeTrue();
            result.TitleId.Should().Be("CUSA11740");
        }

        [Fact]
        public void Inspect_ArchiveWithExtractedMixedContent_ReclassifiesFoldersAndPkgs()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "Game Collection.7z");
            var extracted = Path.Combine(temp.Path, "extracted");
            Directory.CreateDirectory(Path.Combine(extracted, "UPDATE", "CUSA15315-patch"));
            Directory.CreateDirectory(Path.Combine(extracted, "DLC", "HOLIDAYPACK001"));
            Directory.CreateDirectory(Path.Combine(extracted, "Bonus", "CUSA03007"));
            Directory.CreateDirectory(Path.Combine(extracted, "My Game", "CUSA15315"));
            File.WriteAllText(Path.Combine(archive), "archive");
            File.WriteAllText(Path.Combine(extracted, "My Game", "CUSA15315", "eboot.bin"), "base");
            File.WriteAllText(Path.Combine(extracted, "UPDATE", "Patch.pkg"), "pkg");

            var inspector = new Ps4GameInspector();
            var result = inspector.Inspect(
                archivePath: archive,
                extractedPath: extracted,
                gameName: "My Game",
                options: new Ps4InspectorOptions
                {
                    ArchiveExtractionEnabled = true,
                    DirectPkgSupportEnabled = true,
                    AllowExtractedPkgInstall = true
                },
                logger: null);

            result.SourceContentFormat.Should().Be(Ps4ContentFormat.Folder);
            result.BaseGameItems.Should().ContainSingle(item => item.ContentFormat == Ps4ContentFormat.Folder);
            result.UpdateItems.Should().Contain(item => item.ContentFormat == Ps4ContentFormat.Folder);
            result.UpdateItems.Should().Contain(item => item.ContentFormat == Ps4ContentFormat.Pkg);
            result.DlcItems.Should().ContainSingle();
            result.BonusItems.Should().ContainSingle();
        }
    }
}

