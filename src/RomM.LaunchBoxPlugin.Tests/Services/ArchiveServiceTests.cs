using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using FluentAssertions;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class ArchiveServiceTests
    {
        [Theory]
        [InlineData("", false)]
        [InlineData("game.zip", true)]
        [InlineData("game.7z", true)]
        [InlineData("game.rar", true)]
        [InlineData("game.iso", false)]
        public void IsSupportedArchive_ShouldMatchKnownExtensions(string path, bool expected)
        {
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            service.IsSupportedArchive(path).Should().Be(expected);
        }

        [Fact]
        public void IsSupportedArchive_ShouldReturnFalse_WhenPathMissing()
        {
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            service.IsSupportedArchive(null).Should().BeFalse();
        }

        [Fact]
        public void DetectInstallType_ShouldHonorFileNameMarkers()
        {
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            service.DetectInstallType("game (installer).zip", null).Should().Be(InstallType.Installer);
            service.DetectInstallType("game (portable).zip", null).Should().Be(InstallType.Portable);
        }

        [Fact]
        public void DetectInstallType_ShouldDetectSetupExeInExtractedPath()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("setup.exe", new byte[] { 0x1 });
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            service.DetectInstallType("game.zip", temp.Path).Should().Be(InstallType.Installer);
        }

        [Fact]
        public void DetectInstallType_ShouldDefaultToPortable_WhenExtractedHasNoSetup()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("readme.txt", new byte[] { 0x1 });
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            service.DetectInstallType("game.zip", temp.Path).Should().Be(InstallType.Portable);
        }

        [Fact]
        public async Task ExtractAsync_ShouldSkipWhenBehaviorNone()
        {
            using var temp = new TempDirectory();
            var archivePath = CreateZipArchive(temp.Path, "game.zip");
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            var result = await service.ExtractAsync(archivePath, temp.Path, ExtractionBehavior.None, CancellationToken.None);

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ExtractAsync_ShouldExtractZipToSubfolder_WhenBehaviorSubfolder()
        {
            using var temp = new TempDirectory();
            var archivePath = CreateZipArchive(temp.Path, "game.zip");
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            var extracted = await service.ExtractAsync(archivePath, temp.Path, ExtractionBehavior.Subfolder, CancellationToken.None);

            extracted.Should().NotBeNullOrWhiteSpace();
            Directory.Exists(extracted).Should().BeTrue();
            File.Exists(Path.Combine(extracted, "content.txt")).Should().BeTrue();
        }

        [Fact]
        public async Task ExtractAsync_ShouldExtractZipDirect_WhenBehaviorDirect()
        {
            using var temp = new TempDirectory();
            var archivePath = CreateZipArchive(temp.Path, "game.zip");
            var service = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));

            var extracted = await service.ExtractAsync(archivePath, temp.Path, ExtractionBehavior.Direct, CancellationToken.None);

            extracted.Should().Be(temp.Path);
            File.Exists(Path.Combine(temp.Path, "content.txt")).Should().BeTrue();
        }

        private static string CreateZipArchive(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("content.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("data");
            }
            return path;
        }
    }
}
