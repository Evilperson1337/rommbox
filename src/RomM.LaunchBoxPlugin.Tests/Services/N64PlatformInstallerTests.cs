using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.N64;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class N64PlatformInstallerTests
    {
        [Theory]
        [InlineData("Super Mario 64.z64")]
        [InlineData("Super Mario 64.n64")]
        [InlineData("Super Mario 64.v64")]
        [InlineData("Super Mario 64.zip")]
        public async Task InstallAsync_SupportedRomFormats_InstallsWithoutExtraction(string sourceName)
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo 64");
            var source = Path.Combine(temp.Path, "download", sourceName);
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "rom-data");

            var logger = new RecordingPlatformLogger();
            var installer = new N64PlatformInstaller();
            var context = new InstallContext
            {
                GameName = "Super Mario 64",
                InstallDirectory = root,
                ArchivePath = source,
                Logger = logger
            };

            var result = await installer.InstallAsync(context, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Super Mario 64", sourceName);
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
            logger.Messages.Should().Contain(message => message.Contains("Scanning staged content for supported artifacts", StringComparison.OrdinalIgnoreCase));
            logger.Messages.Should().Contain(message => message.Contains("Application path set to", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task InstallAsync_DuplicateInstall_ReplacesExistingRomFile()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo 64");
            var gameFolder = Path.Combine(root, "Mario Kart 64");
            Directory.CreateDirectory(gameFolder);

            var existing = Path.Combine(gameFolder, "Mario Kart 64.z64");
            await File.WriteAllTextAsync(existing, "old");
            var source = Path.Combine(temp.Path, "download", "Mario Kart 64.z64");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "new");

            var installer = new N64PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Mario Kart 64",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.ReadAllText(existing).Should().Be("new");
        }

        [Fact]
        public async Task InstallAsync_UnsupportedFormat_ReturnsFailure()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo 64");
            var source = Path.Combine(temp.Path, "download", "not-a-rom.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "bad-data");

            var installer = new N64PlatformInstaller();
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
        [InlineData(".z64", true)]
        [InlineData(".n64", true)]
        [InlineData(".v64", true)]
        [InlineData(".zip", true)]
        [InlineData(".txt", false)]
        public async Task DetectAsync_UsesInstalledPathAndSupportedExtensions(string extension, bool expectedInstalled)
        {
            using var temp = new TempDirectory();
            var installedPath = Path.Combine(temp.Path, "Games", "Nintendo 64", "Test" + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath) ?? temp.Path);
            if (expectedInstalled)
            {
                await File.WriteAllTextAsync(installedPath, "rom-data");
            }

            var installer = new N64PlatformInstaller();
            var result = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "n64",
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
        public async Task UninstallAsync_RemovesInstalledRomAndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Nintendo 64");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "The Legend of Zelda - Ocarina of Time (USA).z64");
            await File.WriteAllTextAsync(installedPath, "rom-data");

            var installer = new N64PlatformInstaller();
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
        public async Task Profile_UsesRetroArchMupen64PlusNextAndPreservesArchives()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Nintendo 64");
            var source = Path.Combine(temp.Path, "download", "Wave Race 64.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "zip-data");

            var installer = new N64PlatformInstaller();

            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Wave Race 64",
                InstallDirectory = root,
                ArchivePath = source,
                RomSettings = new RomInstallSettings
                {
                    LaunchArguments = "-L mupen64plus_next_libretro.dll {rom}"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Arguments.Should().NotBeNull();
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Contain("mupen64plus_next_libretro.dll");
            result.Arguments[0].Should().Contain("Wave Race 64.zip");
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
