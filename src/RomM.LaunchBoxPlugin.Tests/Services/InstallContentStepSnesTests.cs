using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
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
    public sealed class InstallContentStepSnesTests
    {
        [Fact]
        public async Task ExecuteAsync_SnesArchiveWithNumericPlatformId_SkipsExtractionBeforeInstaller()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            var settings = TestSettingsStore.CreateSettings(new PlatformMapping());
            settingsStore.WriteSettings(settings);
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Super Nintendo Entertainment System");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Super Nintendo Entertainment System")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Super Nintendo Entertainment System");
            game.SetupGet(g => g.Title).Returns("The Adventures of Batman & Robin");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "16124",
                    PlatformId = "23",
                    PlatformDisplayName = "Super Nintendo Entertainment System"
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Super Nintendo Entertainment System"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Super Nintendo Entertainment System", "The Adventures of Batman & Robin")
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var archivePath = Path.Combine(context.DownloadDirectory, "Adventures of Batman & Robin, The (USA) (igdb-5346).zip");
            await File.WriteAllTextAsync(archivePath, "zip-data");
            context.ArchivePath = archivePath;

            var installer = new TrackingInstaller("snes", "Super Nintendo");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["snes"] = installer
            });
            var platformLogger = new PlatformLoggerAdapter(logger);
            var archiveService = new ArchiveService(logger, settingsManager);
            var step = new InstallContentStep(registry, platformLogger, archiveService);

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            installer.CapturedContext.Should().NotBeNull();
            var captured = installer.CapturedContext!;
            captured.ExtractedPath.Should().BeNullOrEmpty("SNES should not be extracted even when RomM platform id is numeric and resolved by name");
            captured.ArchivePath.Should().Be(archivePath);
        }

        private sealed class TrackingInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
        {
            public TrackingInstaller(string platformKey, string displayName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
                SupportedPlatformIds = new[] { platformKey, "23" };
                SupportedPlatformAliases = new[] { displayName, "Super Nintendo Entertainment System" };
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }
            public PlatformInstallerCapabilities Capabilities { get; } = new PlatformInstallerCapabilities();
            public IReadOnlyCollection<string>? SupportedPlatformIds { get; }
            public IReadOnlyCollection<string>? SupportedPlatformAliases { get; }
            public RomM.Platforms.Abstractions.Models.Install.InstallContext? CapturedContext { get; private set; }

            public PlatformConfigDescriptor? GetConfigDescriptor() => null;

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                CapturedContext = ctx;
                return Task.FromResult(new PlatformInstallResult
                {
                    Success = true,
                    ExecutablePath = ctx.ArchivePath,
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
