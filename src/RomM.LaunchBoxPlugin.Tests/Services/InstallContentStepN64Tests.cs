using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Install;
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
using PlatformInstallResult = RomM.Platforms.Abstractions.Models.Install.InstallResult;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class InstallContentStepN64Tests
    {
        [Fact]
        public async Task ExecuteAsync_N64ArchiveWithNumericPlatformId_SkipsExtractionBeforeInstaller()
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
            platform.SetupGet(p => p.Name).Returns("Nintendo 64");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Nintendo 64")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Nintendo 64");
            game.SetupGet(g => g.Title).Returns("Mario Kart 64");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-1",
                    PlatformId = "8",
                    PlatformDisplayName = "Nintendo 64"
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Nintendo 64"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Nintendo 64", "Mario Kart 64")
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var archivePath = Path.Combine(context.DownloadDirectory, "Mario Kart 64 (USA).zip");
            await File.WriteAllTextAsync(archivePath, "zip-data");
            context.ArchivePath = archivePath;

            var installer = new TrackingInstaller("n64", "Nintendo 64");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["n64"] = installer
            });
            var platformLogger = new PlatformLoggerAdapter(logger);
            var archiveService = new ArchiveService(logger, settingsManager);
            var step = new InstallContentStep(registry, platformLogger, archiveService);

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            installer.CapturedContext.Should().NotBeNull();
            var captured = installer.CapturedContext!;
            captured.ExtractedPath.Should().BeNullOrEmpty("N64 should not be extracted when preserving archives");
            captured.ArchivePath.Should().Be(archivePath);
        }

        [Fact]
        public async Task ExecuteAsync_RomOnlyInstallerArguments_DoesNotDuplicateApplicationPathIntoCommandLine()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Nintendo 64");

            var emulatorPlatform = new Moq.Mock<IEmulatorPlatform>();
            emulatorPlatform.SetupGet(p => p.Platform).Returns("Nintendo 64");
            emulatorPlatform.SetupGet(p => p.IsDefault).Returns(true);

            var emulator = new Moq.Mock<IEmulator>();
            emulator.SetupGet(e => e.Id).Returns("emu-n64");
            emulator.Setup(e => e.GetAllEmulatorPlatforms()).Returns(new[] { emulatorPlatform.Object });

            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Nintendo 64")).Returns(platform.Object);
            dataManager.Setup(dm => dm.GetAllEmulators()).Returns(new[] { emulator.Object });

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Nintendo 64");
            game.SetupGet(g => g.Title).Returns("Mario Kart 64");
            game.SetupProperty(g => g.ApplicationPath, string.Empty);
            game.SetupProperty(g => g.CommandLine, string.Empty);
            game.SetupProperty(g => g.EmulatorId, string.Empty);
            game.SetupProperty(g => g.Installed, false);
            game.SetupProperty(g => g.Status, string.Empty);

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-1",
                    PlatformId = "8",
                    PlatformDisplayName = "Nintendo 64"
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Nintendo 64"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Nintendo 64", "Mario Kart 64")
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var archivePath = Path.Combine(context.DownloadDirectory, "Mario Kart 64 (USA).zip");
            await File.WriteAllTextAsync(archivePath, "zip-data");
            context.ArchivePath = archivePath;

            var installedPath = Path.Combine(context.DownloadDirectory, "Mario Kart 64 (USA).zip");
            context.InstalledExecutablePath = installedPath;
            context.InstallerArguments = new[] { $"\"{installedPath}\"" };

            var step = new PostProcessStep();

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            game.Object.ApplicationPath.Should().EndWith(Path.Combine("Nintendo 64", "Mario Kart 64", "Mario Kart 64 (USA).zip"));
            game.Object.CommandLine.Should().BeEmpty();
            context.InstallStateSnapshot.RommLaunchArgs.Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_PrefersConfiguredAssociatedEmulator_OverPlatformDefault()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Arcade");

            var emulatorPlatform = new Moq.Mock<IEmulatorPlatform>();
            emulatorPlatform.SetupGet(p => p.Platform).Returns("Arcade");
            emulatorPlatform.SetupGet(p => p.IsDefault).Returns(true);

            var emulator = new Moq.Mock<IEmulator>();
            emulator.SetupGet(e => e.Id).Returns("retroarch-default");
            emulator.Setup(e => e.GetAllEmulatorPlatforms()).Returns(new[] { emulatorPlatform.Object });

            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Arcade")).Returns(platform.Object);
            dataManager.Setup(dm => dm.GetAllEmulators()).Returns(new[] { emulator.Object });

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Arcade");
            game.SetupGet(g => g.Title).Returns("TMNT");
            game.SetupProperty(g => g.ApplicationPath, string.Empty);
            game.SetupProperty(g => g.CommandLine, string.Empty);
            game.SetupProperty(g => g.EmulatorId, string.Empty);
            game.SetupProperty(g => g.Installed, false);
            game.SetupProperty(g => g.Status, string.Empty);

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-arcade-1",
                    PlatformId = "arcade",
                    PlatformDisplayName = "Arcade"
                },
                PlatformMapping = new PlatformMapping
                {
                    AssociatedEmulatorId = "retroarch-fbneo"
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Arcade"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Arcade", "TMNT")
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var installedPath = Path.Combine(context.DownloadDirectory, "tmnt.zip");
            await File.WriteAllTextAsync(installedPath, "zip-data");
            context.InstalledExecutablePath = installedPath;
            context.InstallerArguments = new[] { "-L finalburnneo_libretro.dll \"D:\\LaunchBox\\Games\\Arcade\\TMNT\\tmnt.zip\"" };

            var step = new PostProcessStep();

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            game.Object.EmulatorId.Should().Be("retroarch-fbneo");
            game.Object.CommandLine.Should().Contain("finalburnneo_libretro.dll");
            context.InstallStateSnapshot.RommLaunchArgs.Should().Contain("finalburnneo_libretro.dll");
        }

        [Fact]
        public async Task FinalizeExtractedRomInstallArtifacts_DeletesArchiveAndCleansStaging()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var dataManager = new Moq.Mock<IDataManager>();

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Nintendo GameCube");
            game.SetupGet(g => g.Title).Returns("Sample Game");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-1",
                    PlatformId = "gamecube",
                    PlatformDisplayName = "Nintendo GameCube"
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Nintendo GameCube"),
                DownloadDirectory = Path.Combine(temp.Path, "Games", "Nintendo GameCube", "Sample Game"),
                OperationId = "cleanup-op"
            };

            Directory.CreateDirectory(context.DownloadDirectory);
            var archivePath = Path.Combine(context.DownloadDirectory, "Sample Game.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(Path.Combine("disc1", "Sample Game.iso"));
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync("disc-data");
            }

            context.ArchivePath = archivePath;
            context.ExtractedPath = Path.Combine(context.DownloadDirectory, ".staging", context.OperationId, "extracted", "payload");
            Directory.CreateDirectory(context.ExtractedPath);
            var stagedIso = Path.Combine(context.ExtractedPath, "Sample Game.iso");
            await File.WriteAllTextAsync(stagedIso, "disc-data");

            var finalizeMethod = typeof(InstallContentStep).GetMethod(
                "FinalizeExtractedRomInstallArtifacts",
                BindingFlags.NonPublic | BindingFlags.Static);

            finalizeMethod.Should().NotBeNull();
            var result = (string)finalizeMethod!.Invoke(null, new object[] { context, logger })!;

            result.Should().BeEmpty();
            File.Exists(archivePath).Should().BeFalse();
            Directory.Exists(Path.Combine(context.DownloadDirectory, ".staging")).Should().BeFalse();
            File.Exists(stagedIso).Should().BeFalse();
        }

        [Fact]
        public void TryDeleteOperationRootAndEmptyParents_Removes_Empty_Platform_Staging_Directory()
        {
            using var temp = new TempDirectory();

            var platformRoot = Path.Combine(temp.Path, "Games", "Sony Playstation");
            var operationRoot = Path.Combine(platformRoot, ".staging", "cleanup-op");
            var tempRoot = Path.Combine(operationRoot, "download");
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "artifact.tmp"), "data");

            InstallStagingPathHelper.TryDeleteOperationRootAndEmptyParents(tempRoot);

            Directory.Exists(operationRoot).Should().BeFalse();
            Directory.Exists(Path.Combine(platformRoot, ".staging")).Should().BeFalse();
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
            public RomM.Platforms.Abstractions.Models.Install.InstallContext? CapturedContext { get; private set; }
            public Func<RomM.Platforms.Abstractions.Models.Install.InstallContext, PlatformInstallResult>? ResultFactory { get; set; }

            public PlatformConfigDescriptor? GetConfigDescriptor() => null;

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                CapturedContext = ctx;
                return Task.FromResult(ResultFactory?.Invoke(ctx) ?? new PlatformInstallResult
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
