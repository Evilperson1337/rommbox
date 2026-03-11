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
using RomMbox.Services.Install;
using RomMbox.Services.Install.Pipeline;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

using PipelineInstallContext = RomMbox.Services.Install.Pipeline.InstallContext;
using PlatformInstallResult = RomM.Platforms.Abstractions.Models.Install.InstallResult;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class InstallContentStepArcadeTests
    {
        [Fact]
        public async Task ExecuteAsync_ArcadeArchive_SkipsExtractionBeforeInstaller()
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
            platform.SetupGet(p => p.Name).Returns("Arcade");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Arcade")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Arcade");
            game.SetupGet(g => g.Title).Returns("Metal Slug");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-a1",
                    PlatformId = "arcade",
                    PlatformDisplayName = "Arcade"
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Arcade"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Arcade", "Metal Slug")
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var archivePath = Path.Combine(context.DownloadDirectory, "metal_slug.zip");
            await File.WriteAllTextAsync(archivePath, "zip-data");
            context.ArchivePath = archivePath;

            var installer = new TrackingInstaller("arcade", "Arcade");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["arcade"] = installer
            });
            var platformLogger = new PlatformLoggerAdapter(logger);
            var archiveService = new ArchiveService(logger, settingsManager);
            var step = new InstallContentStep(registry, platformLogger, archiveService);

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            installer.CapturedContext.Should().NotBeNull();
            installer.CapturedContext.ExtractedPath.Should().BeNullOrEmpty("Arcade ROM sets must remain archived");
            installer.CapturedContext.ArchivePath.Should().Be(archivePath);
        }

        [Fact]
        public async Task ExecuteAsync_ArcadeDestination_UsesGameSubfolder()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            var mappingStore = new PlatformMappingStore(logger);
            var destinationService = new InstallDestinationService(logger, settingsManager);

            var platformRoot = Path.Combine(temp.Path, "Games", "Arcade");
            Directory.CreateDirectory(platformRoot);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Arcade");
            platform.SetupGet(p => p.Folder).Returns(platformRoot);
            platform.Setup(p => p.GetAllGames(true, true)).Returns(Array.Empty<IGame>());

            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Arcade")).Returns(platform.Object);

            using var pluginScope = new PluginDataManagerScope(dataManager.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Arcade");
            game.SetupGet(g => g.Title).Returns("1942");

            var context = new PipelineInstallContext(new InstallRequest(game.Object, dataManager.Object), logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-1942",
                    PlatformId = "arcade",
                    PlatformDisplayName = "Arcade"
                }
            };

            var step = new ResolveDestinationStep(destinationService, mappingStore);
            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            context.InstallDirectory.Should().Be(platformRoot);
            context.DownloadDirectory.Should().Be(Path.Combine(platformRoot, "1942"));
            Directory.Exists(Path.Combine(platformRoot, "1942")).Should().BeTrue();
        }

        private sealed class PluginDataManagerScope : IDisposable
        {
            private readonly IDataManager _prior;

            public PluginDataManagerScope(IDataManager manager)
            {
                _prior = PluginHelper.DataManager;
                SetStaticProperty(typeof(PluginHelper), "DataManager", manager);
            }

            public void Dispose()
            {
                SetStaticProperty(typeof(PluginHelper), "DataManager", _prior);
            }

            private static void SetStaticProperty(Type type, string propertyName, object value)
            {
                var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                property?.SetValue(null, value);
            }
        }

        private sealed class TrackingInstaller : IPlatformInstaller, IPlatformInstallerMetadata
        {
            public TrackingInstaller(string platformKey, string displayName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }
            public PlatformInstallerCapabilities Capabilities { get; } = new PlatformInstallerCapabilities();
            public RomM.Platforms.Abstractions.Models.Install.InstallContext CapturedContext { get; private set; }

            public PlatformConfigDescriptor GetConfigDescriptor() => null;

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
