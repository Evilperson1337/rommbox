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
using RomMbox.Services.Install.Pipeline;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;

using PipelineInstallContext = RomMbox.Services.Install.Pipeline.InstallContext;
using PipelineInstallResult = RomMbox.Services.Install.Pipeline.InstallResult;
using PlatformInstallResult = RomM.Platforms.Abstractions.Models.Install.InstallResult;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class InstallContentStepGeneralFallbackTests
    {
        [Fact]
        public async Task ExecuteAsync_FallbackInstallerDisabled_UsesGeneralInstaller()
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
                    UseGeneralFallbackInstaller = false
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Unknown Console")
            };

            Directory.CreateDirectory(context.InstallDirectory);
            var extractedPath = Path.Combine(context.InstallDirectory, "mystery.rom");
            await File.WriteAllTextAsync(extractedPath, "rom-bytes");
            context.ExtractedPath = extractedPath;

            var fallbackInstaller = new TrackingInstaller("general", "General Platform");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["general"] = fallbackInstaller
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            fallbackInstaller.InstallCalled.Should().BeTrue();
            context.InstalledExecutablePath.Should().Be(extractedPath);
        }

        [Fact]
        public async Task ExecuteAsync_FallbackInstallerEnabled_UsesGeneralInstaller()
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
                    UseGeneralFallbackInstaller = true
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Unknown Console")
            };

            Directory.CreateDirectory(context.InstallDirectory);
            var extractedPath = Path.Combine(context.InstallDirectory, "mystery.rom");
            await File.WriteAllTextAsync(extractedPath, "rom-bytes");
            context.ExtractedPath = extractedPath;

            var fallbackInstaller = new TrackingInstaller("general", "General Platform");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["general"] = fallbackInstaller
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            fallbackInstaller.InstallCalled.Should().BeTrue();
            context.InstalledExecutablePath.Should().Be(extractedPath);
        }

        private sealed class TrackingInstaller : IPlatformInstaller
        {
            public TrackingInstaller(string platformKey, string displayName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
            }

            public bool InstallCalled { get; private set; }
            public string PlatformKey { get; }
            public string DisplayName { get; }

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                InstallCalled = true;
                return Task.FromResult(new PlatformInstallResult
                {
                    Success = true,
                    ExecutablePath = ctx.ExtractedPath,
                    InstallRootPath = ctx.InstallDirectory,
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

