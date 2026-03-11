using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Install;
using RomMbox.Services.Install.Pipeline;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;

using PipelineInstallContext = RomMbox.Services.Install.Pipeline.InstallContext;
using PlatformInstallResult = RomM.Platforms.Abstractions.Models.Install.InstallResult;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class InstallContentStepCanonicalPathTests
    {
        [Fact]
        public async Task ExecuteAsync_ReconcilesPlatformRootArtifactIntoCanonicalGameDirectory_BeforeSuccess()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Unknown Console");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Unknown Console")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Unknown Console");
            game.SetupGet(g => g.Title).Returns("Mystery Game");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var platformRoot = Path.Combine(temp.Path, "Games", "Unknown Console");
            var gameRoot = Path.Combine(platformRoot, "Mystery Game");
            Directory.CreateDirectory(gameRoot);

            var archivePath = Path.Combine(gameRoot, "Mystery Game.rar");
            await File.WriteAllTextAsync(archivePath, "archive-bytes");

            var installedAtPlatformRoot = Path.Combine(platformRoot, "Mystery Game.iso");
            await File.WriteAllTextAsync(installedAtPlatformRoot, "iso-bytes");

            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-unknown",
                    PlatformId = "unknown-platform",
                    PlatformDisplayName = "Unknown Console"
                },
                PlatformMapping = new PlatformMapping
                {
                    InstallScenario = InstallScenario.Basic,
                    UseGeneralFallbackInstaller = true,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = platformRoot,
                DownloadDirectory = gameRoot,
                ArchivePath = archivePath
            };

            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["general"] = new TrackingInstaller(installedAtPlatformRoot)
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            var canonicalPath = Path.Combine(gameRoot, "Mystery Game.iso");
            result.Success.Should().BeTrue();
            context.InstalledExecutablePath.Should().Be(canonicalPath);
            context.InstallStateSnapshot.InstallRootPath.Should().Be(gameRoot);
            File.Exists(canonicalPath).Should().BeTrue();
            File.Exists(installedAtPlatformRoot).Should().BeFalse();
            File.Exists(archivePath).Should().BeFalse();
        }

        private sealed class TrackingInstaller : IPlatformInstaller
        {
            private readonly string _installedPath;

            public TrackingInstaller(string installedPath)
            {
                _installedPath = installedPath;
            }

            public string PlatformKey => "general";
            public string DisplayName => "General Platform";

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new PlatformInstallResult
                {
                    Success = true,
                    ExecutablePath = _installedPath,
                    InstallRootPath = Path.GetDirectoryName(_installedPath),
                    InstallType = RomM.Platforms.Abstractions.Models.Install.InstallType.Portable
                });
            }

            public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new UninstallResult { Success = true });
            }

            public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new VerifyResult { IsValid = true });
            }
        }
    }
}
