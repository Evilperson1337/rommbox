using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Snes;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class SnesPlatformInstallerTests
    {
        [Theory]
        [InlineData("Super Metroid.zip")]
        [InlineData("Super Metroid.sfc")]
        [InlineData("Super Metroid.smc")]
        public async Task InstallAsync_SupportedRomFormats_InstallsIntoGameDirectory(string sourceName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            var source = Path.Combine(temp.Path, "download", sourceName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "rom-data");

            var logger = new RecordingPlatformLogger();
            var installer = new SnesPlatformInstaller();
            var context = new InstallContext
            {
                GameName = "Super Metroid",
                InstallDirectory = root,
                ArchivePath = source,
                Logger = logger
            };

            var result = await installer.InstallAsync(context, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Super Metroid", sourceName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
            Directory.Exists(Path.Combine(root, "Super Metroid")).Should().BeTrue();
            logger.Messages.Should().Contain(message => message.Contains("Scanning staged content for supported artifacts", StringComparison.OrdinalIgnoreCase));
            logger.Messages.Should().Contain(message => message.Contains("Application path set to", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task InstallAsync_ZipRom_UsesArchiveAsApplicationPath_AndKeepsArchiveIntact()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            var source = Path.Combine(temp.Path, "download", "Chrono Trigger.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var logger = new RecordingPlatformLogger();
            var installer = new SnesPlatformInstaller();
            var context = new InstallContext
            {
                GameName = "Chrono Trigger",
                InstallDirectory = root,
                ArchivePath = source,
                Logger = logger
            };

            var result = await installer.InstallAsync(context, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Chrono Trigger", "Chrono Trigger.zip");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
            logger.Messages.Should().Contain(message => message.Contains("Configured emulator: RetroArch (snes9x)", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task InstallAsync_DuplicateInstall_ReplacesExistingRomFile()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            Directory.CreateDirectory(root);

            var existingDir = Path.Combine(root, "EarthBound");
            Directory.CreateDirectory(existingDir);
            var existing = Path.Combine(existingDir, "EarthBound.sfc");
            await File.WriteAllTextAsync(existing, "old");
            var source = Path.Combine(temp.Path, "download", "EarthBound.sfc");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new SnesPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "EarthBound",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }

        [Fact]
        public async Task InstallAsync_WhenSourceAlreadyInInstallDirectory_DoesNotDeleteFile()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            Directory.CreateDirectory(root);
            var sourceDir = Path.Combine(root, "Mega Man X");
            Directory.CreateDirectory(sourceDir);
            var source = Path.Combine(sourceDir, "Mega Man X.sfc");
            await File.WriteAllTextAsync(source, "rom-data");

            var installer = new SnesPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Mega Man X",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(source);
            File.Exists(source).Should().BeTrue();
            File.ReadAllText(source).Should().Be("rom-data");
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveName_StillInstallsUsingSourceFilename()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            var sourceName = "bad__name__(rev 1) [!].zip";
            var source = Path.Combine(temp.Path, "download", sourceName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new SnesPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Bad Name",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            var installedName = Path.GetFileName(result.ExecutablePath);
            installedName.Should().Be(sourceName);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_UnsupportedFormat_ReturnsFailure()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            var source = Path.Combine(temp.Path, "download", "not-a-rom.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "bad-data");

            var installer = new SnesPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Unsupported",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("No compatible ROM files found");
        }

        [Theory]
        [InlineData(".zip", true)]
        [InlineData(".sfc", true)]
        [InlineData(".smc", true)]
        [InlineData(".7z", false)]
        public async Task DetectAsync_UsesInstalledPathAndSupportedExtensions(string extension, bool expectedInstalled)
        {
            using var temp = new TempDirectory();
            var installedPath = Path.Combine(temp.Path, "Games", "The Adventures of Batman & Robin", "Test" + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath) ?? temp.Path);
            if (expectedInstalled)
            {
                await File.WriteAllTextAsync(installedPath, "rom-data");
            }

            var installer = new SnesPlatformInstaller();
            var result = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "snes",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);

            result.IsInstalled.Should().Be(expectedInstalled);
            if (expectedInstalled)
            {
                result.RecommendedExecutablePath.Should().Be(installedPath);
                result.CandidateExecutablePaths.Should().Contain(installedPath);
            }
            else
            {
                result.RecommendedExecutablePath.Should().BeNullOrEmpty();
            }
        }

        [Fact]
        public async Task DetectAsync_ExistingUnsupportedExtension_ReturnsNotInstalled()
        {
            using var temp = new TempDirectory();
            var installedPath = Path.Combine(temp.Path, "Games", "The Adventures of Batman & Robin", "Test.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath) ?? temp.Path);
            await File.WriteAllTextAsync(installedPath, "not-rom");

            var installer = new SnesPlatformInstaller();
            var result = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "snes",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);

            result.IsInstalled.Should().BeFalse();
            result.Warnings.Should().Contain(warning => warning.Code == "unsupported_extension");
        }

        [Fact]
        public async Task UninstallAsync_RemovesInstalledRomAndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "The Adventures of Batman & Robin");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "Zelda.sfc");
            await File.WriteAllTextAsync(installedPath, "rom-data");

            var installer = new SnesPlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Zelda",
                InstalledPath = installedPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.RemovedCount.Should().Be(1);
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();
        }

        [Fact]
        public async Task Profile_UsesRetroArchSnes9xAndPreservesArchives()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games");
            var source = Path.Combine(temp.Path, "download", "Yoshi.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new SnesPlatformInstaller();

            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Yoshi's Island",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            // This validates the surfaced launch config (application path + args) from base profile behavior.
            result.Success.Should().BeTrue();
            result.Arguments.Should().NotBeNull();
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Contain("Yoshi.zip");
        }

        private sealed class RecordingPlatformLogger : IPlatformLogger
        {
            public List<string> Messages { get; } = new List<string>();

            public void Write(PlatformLogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            {
                Messages.Add($"[{level}] {message}");
            }
        }
    }
}
