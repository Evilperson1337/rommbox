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

            var fallbackInstaller = new TrackingInstaller("general", "General Platform", "General");
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

            var fallbackInstaller = new TrackingInstaller("general", "General Platform", "General");
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
        public async Task ExecuteAsync_Selects_Ps1_Plugin_And_Emulator_From_Resolved_Platform()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var sink = new StubLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Sony Playstation");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Sony Playstation")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Sony Playstation");
            game.SetupGet(g => g.Title).Returns("The Grinch");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-ps1",
                    PlatformId = "22",
                    PlatformDisplayName = "PlayStation"
                },
                PlatformMapping = new PlatformMapping
                {
                    InstallScenario = InstallScenario.Basic,
                    UseGeneralFallbackInstaller = false
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Sony Playstation")
            };

            Directory.CreateDirectory(context.InstallDirectory);
            var extractedPath = Path.Combine(context.InstallDirectory, "The Grinch.chd");
            await File.WriteAllTextAsync(extractedPath, "rom-bytes");
            context.ExtractedPath = extractedPath;

            var ps1Installer = new TrackingInstaller("ps1", "PlayStation", "DuckStation");
            var pspInstaller = new TrackingInstaller("psp", "PlayStation Portable", "PPSSPP");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = ps1Installer,
                ["psp"] = pspInstaller,
                ["general"] = new TrackingInstaller("general", "General Platform", "General")
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            ps1Installer.InstallCalled.Should().BeTrue();
            pspInstaller.InstallCalled.Should().BeFalse();
            sink.Drain().Select(message => message.Message).Should().Contain(message => message.Contains("Matched platform plugin: PlayStation (ps1)", StringComparison.OrdinalIgnoreCase));
            sink.Drain().Select(message => message.Message).Should().Contain(message => message.Contains("Configured emulator: DuckStation", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ExecuteAsync_Selects_Wii_Plugin_When_WiiU_Is_A_Sibling_Candidate()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var sink = new StubLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Nintendo Wii");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Nintendo Wii")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Nintendo Wii");
            game.SetupGet(g => g.Title).Returns("New Super Mario Bros. Wii");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-wii",
                    PlatformId = "26",
                    PlatformDisplayName = "Wii"
                },
                PlatformMapping = new PlatformMapping
                {
                    InstallScenario = InstallScenario.Basic,
                    UseGeneralFallbackInstaller = false
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Nintendo Wii")
            };

            Directory.CreateDirectory(context.InstallDirectory);
            var extractedPath = Path.Combine(context.InstallDirectory, "New Super Mario Bros. Wii.rvz");
            await File.WriteAllTextAsync(extractedPath, "rom-bytes");
            context.ExtractedPath = extractedPath;

            var wiiInstaller = new TrackingInstaller("wii", "Nintendo Wii", "Dolphin");
            var wiiuInstaller = new TrackingInstaller("wiiu", "Nintendo Wii U", "Cemu");
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wii"] = wiiInstaller,
                ["wiiu"] = wiiuInstaller,
                ["general"] = new TrackingInstaller("general", "General Platform", "General")
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));

            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            wiiInstaller.InstallCalled.Should().BeTrue();
            wiiuInstaller.InstallCalled.Should().BeFalse();
            sink.Drain().Select(message => message.Message).Should().Contain(message => message.Contains("Matched platform plugin: Nintendo Wii (wii)", StringComparison.OrdinalIgnoreCase));
            sink.Drain().Select(message => message.Message).Should().Contain(message => message.Contains("Configured emulator: Dolphin", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class TrackingInstaller : IPlatformInstaller, IPlatformInstallerIdentityMetadata
        {
            public TrackingInstaller(string platformKey, string displayName, string emulatorName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
                EmulatorName = emulatorName;
                SupportedPlatformIds = BuildSupportedIds(platformKey);
                SupportedPlatformAliases = BuildSupportedAliases(platformKey, displayName);
            }

            public bool InstallCalled { get; private set; }
            public string PlatformKey { get; }
            public string DisplayName { get; }
            public string EmulatorName { get; }
            public IReadOnlyCollection<string>? SupportedPlatformIds { get; }
            public IReadOnlyCollection<string>? SupportedPlatformAliases { get; }

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                InstallCalled = true;
                ctx.Logger?.Write(RomM.Platforms.Abstractions.Logging.PlatformLogLevel.Info, $"Configured emulator: {EmulatorName}");
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

            private static IReadOnlyCollection<string> BuildSupportedIds(string platformKey)
            {
                return platformKey switch
                {
                    "ps1" => new[] { "22", "ps1" },
                    "psp" => new[] { "34", "psp" },
                    "wii" => new[] { "26", "wii" },
                    "wiiu" => new[] { "27", "wiiu" },
                    _ => new[] { platformKey }
                };
            }

            private static IReadOnlyCollection<string> BuildSupportedAliases(string platformKey, string displayName)
            {
                return platformKey switch
                {
                    "ps1" => new[] { "playstation", "sony playstation", "psx", "ps1", displayName },
                    "psp" => new[] { "psp", "playstation portable", "sony playstation portable", displayName },
                    "wii" => new[] { "wii", "nintendo wii", displayName },
                    "wiiu" => new[] { "wiiu", "wii u", "nintendo wii u", displayName },
                    _ => new[] { displayName, platformKey }
                };
            }
        }
    }
}

