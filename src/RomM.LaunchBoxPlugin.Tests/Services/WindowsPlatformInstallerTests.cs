using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Windows;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class WindowsPlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_PortableWithStagingDirectory_PreservesStagingForPipelineCommit()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Windows");
            var stagingRoot = Path.Combine(platformRoot, ".staging", "op-portable");
            var extractedRoot = Path.Combine(stagingRoot, "download", "extracted", "Sample Game (Portable)");
            var stagedGameRoot = Path.Combine(stagingRoot, "Sample Game");
            var archivePath = Path.Combine(stagingRoot, "download", "downloads", "Sample Game (Portable).rar");

            Directory.CreateDirectory(extractedRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? stagingRoot);
            await File.WriteAllTextAsync(Path.Combine(extractedRoot, "Sample Game.exe"), "exe");
            await File.WriteAllTextAsync(archivePath, "archive");

            var installer = new WindowsPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Sample Game",
                InstallDirectory = platformRoot,
                StagingDirectory = stagingRoot,
                ArchivePath = archivePath,
                ExtractedPath = extractedRoot,
                Logger = new NullPlatformLogger()
            }, new Progress<InstallProgress>(_ => { }), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallType.Should().Be(InstallType.Portable);
            result.InstallRootPath.Should().Be(stagedGameRoot);
            result.ExecutablePath.Should().Be(Path.Combine(stagedGameRoot, "Sample Game.exe"));
            Directory.Exists(stagingRoot).Should().BeTrue("portable installs staged for the pipeline must remain available for final commit");
            Directory.Exists(stagedGameRoot).Should().BeTrue();
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_PortableWithoutStagingDirectory_DeploysToInstallDirectoryAndCleansTempRoot()
        {
            using var temp = new TempDirectory();
            var platformRoot = Path.Combine(temp.Path, "Games", "Windows");
            var operationRoot = Path.Combine(platformRoot, ".staging", "op-finalize");
            var extractedRoot = Path.Combine(operationRoot, "download", "extracted", "Sample Game (Portable)");
            var archivePath = Path.Combine(operationRoot, "download", "downloads", "Sample Game (Portable).rar");
            var installedGameRoot = Path.Combine(platformRoot, "Sample Game");

            Directory.CreateDirectory(extractedRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? operationRoot);
            await File.WriteAllTextAsync(Path.Combine(extractedRoot, "Sample Game.exe"), "exe");
            await File.WriteAllTextAsync(archivePath, "archive");

            var installer = new WindowsPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Sample Game",
                InstallDirectory = platformRoot,
                ArchivePath = archivePath,
                ExtractedPath = extractedRoot,
                Logger = new NullPlatformLogger()
            }, new Progress<InstallProgress>(_ => { }), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallType.Should().Be(InstallType.Portable);
            result.InstallRootPath.Should().Be(installedGameRoot);
            result.ExecutablePath.Should().Be(Path.Combine(installedGameRoot, "Sample Game.exe"));
            Directory.Exists(installedGameRoot).Should().BeTrue();
            Directory.Exists(operationRoot).Should().BeFalse("temporary staging should be removed after portable content is deployed into the final install directory");
        }

        private sealed class NullPlatformLogger : IPlatformLogger
        {
            public void Write(PlatformLogLevel level, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            {
            }
        }
    }
}
