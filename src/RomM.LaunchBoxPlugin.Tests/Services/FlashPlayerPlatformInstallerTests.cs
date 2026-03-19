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
using RomM.Platforms.FlashPlayer;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class FlashPlayerPlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_DirectSwf_InstallsToPerGameSubfolder_AndBuildsLaunchArgs()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Flash Player");
            var source = Path.Combine(temp.Path, "download", "game.swf");
            var ruffle = Path.Combine(temp.Path, "Emulators", "ruffle.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(ruffle) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");
            await File.WriteAllTextAsync(ruffle, "exe");
            var logger = new RecordingPlatformLogger();

            var installer = new FlashPlayerPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    RuffleExecutablePath = ruffle
                },
                RomSettings = new RomInstallSettings(),
                Logger = logger
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedPath = Path.Combine(root, "Game", "game.swf");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(expectedPath);
            result.Arguments.Should().ContainSingle().Which.Should().Be(expectedPath.Contains(" ") ? $"\"{expectedPath}\"" : expectedPath);
            result.InstallRootPath.Should().Be(root);
            File.Exists(expectedPath).Should().BeTrue();
            logger.Messages.Should().Contain(message => message.Contains("Configured emulator: Ruffle", StringComparison.OrdinalIgnoreCase));
            logger.Messages.Should().Contain(message => message.Contains("Application path set to", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task InstallAsync_MissingRufflePath_FailsClearly()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Flash Player");
            var source = Path.Combine(temp.Path, "download", "game.swf");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "content");

            var installer = new FlashPlayerPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings(),
                RomSettings = new RomInstallSettings()
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Ruffle executable could not be resolved");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Flash Player");
            Directory.CreateDirectory(platformDir);
            var installedPath = Path.Combine(platformDir, "sample.swf");
            await File.WriteAllTextAsync(installedPath, "data");

            var installer = new FlashPlayerPlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "flashplayer",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);
            detect.IsInstalled.Should().BeTrue();

            var verifyBefore = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyBefore.IsValid.Should().BeTrue();

            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Sample",
                InstalledPath = installedPath
            }, new Progress<InstallProgress>(), CancellationToken.None);
            uninstall.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();
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
