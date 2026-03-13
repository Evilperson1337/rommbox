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
using RomMbox.Models.Download;
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
using PipelineInstallRequest = RomMbox.Services.Install.Pipeline.InstallRequest;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class DownloadStepCapabilityTests
    {
        [Fact]
        public async Task ExecuteAsync_ForcesExtraction_WhenCapabilitiesRequireStagingInspection()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var (context, request, logger) = BuildContext(temp.Path, "ps3", "Nintendo Entertainment System");
            var platformInstallers = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps3"] = new MetadataStubInstaller(
                    platformKey: "ps3",
                    capabilities: new PlatformInstallerCapabilities { RequiresStagingInspection = true })
            });

            var archiveService = new ArchiveService(logger, context.SettingsManager);
            var downloadService = new DownloadService(logger, new DownloadToFileRommClient(), archiveService, context.SettingsManager);
            var step = new DownloadStep(downloadService, platformInstallers);
            var events = new List<InstallProgressEvent>();
            var progress = new Progress<InstallProgressEvent>(evt => events.Add(evt));

            var result = await step.ExecuteAsync(context, progress, CancellationToken.None);

            result.Success.Should().BeTrue();
            events.Should().Contain(evt => evt.Phase == InstallPhase.Extracting && evt.Message.Contains("Extraction skipped", StringComparison.OrdinalIgnoreCase),
                "plugin metadata requiring staging inspection should force extraction reporting");
            context.TempRoot.Should().StartWith(Path.Combine(temp.Path, ".staging"));
        }

        [Fact]
        public async Task ExecuteAsync_DisablesExtraction_ForRomPreservePolicy_WhenNotWindowsAndNoCapabilityRequirement()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var (context, request, logger) = BuildContext(temp.Path, "snes", "Super Nintendo Entertainment System");
            context.PlatformMapping.ExtractAfterDownload = true;
            context.PlatformMapping.RomArchivePolicy = "Preserve";

            var platformInstallers = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["snes"] = new MetadataStubInstaller(
                    platformKey: "snes",
                    capabilities: new PlatformInstallerCapabilities { RequiresStagingInspection = false })
            });

            var archiveService = new ArchiveService(logger, context.SettingsManager);
            var downloadService = new DownloadService(logger, new DownloadToFileRommClient(), archiveService, context.SettingsManager);
            var step = new DownloadStep(downloadService, platformInstallers);
            var events = new List<InstallProgressEvent>();
            var progress = new Progress<InstallProgressEvent>(evt => events.Add(evt));

            var result = await step.ExecuteAsync(context, progress, CancellationToken.None);

            result.Success.Should().BeTrue();
            events.Should().NotContain(evt => evt.Phase == InstallPhase.Extracting,
                "ROM preserve policy on non-Windows platforms should suppress extraction reporting");
        }

        [Fact]
        public async Task ExecuteAsync_DisablesExtraction_ForN64Platform_WhenExtractAfterDownloadIsTrue()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var (context, request, logger) = BuildContext(temp.Path, "n64", "Nintendo 64");
            context.PlatformMapping.ExtractAfterDownload = true;
            context.PlatformMapping.RomArchivePolicy = string.Empty;

            var platformInstallers = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["n64"] = new MetadataStubInstaller(
                    platformKey: "n64",
                    capabilities: new PlatformInstallerCapabilities { RequiresStagingInspection = false })
            });

            var archiveService = new ArchiveService(logger, context.SettingsManager);
            var downloadService = new DownloadService(logger, new DownloadToFileRommClient(), archiveService, context.SettingsManager);
            var step = new DownloadStep(downloadService, platformInstallers);
            var events = new List<InstallProgressEvent>();
            var progress = new Progress<InstallProgressEvent>(evt => events.Add(evt));

            var result = await step.ExecuteAsync(context, progress, CancellationToken.None);

            result.Success.Should().BeTrue();
            events.Should().NotContain(evt => evt.Phase == InstallPhase.Extracting,
                "N64 should preserve archives and skip extraction reporting");
        }

        private static (PipelineInstallContext Context, PipelineInstallRequest Request, LoggingService Logger) BuildContext(string tempRoot, string platformId, string launchBoxPlatformName)
        {
            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(tempRoot);
            var settings = TestSettingsStore.CreateSettings(new PlatformMapping
            {
                RommPlatformId = platformId,
                LaunchBoxPlatformName = launchBoxPlatformName,
                ExtractAfterDownload = false,
                ExtractionBehavior = ExtractionBehavior.Subfolder,
                InstallScenario = RomMbox.Models.Install.InstallScenario.Basic,
                RomArchivePolicy = string.Empty
            });
            settings.ServerUrl = "http://localhost";
            settingsStore.WriteSettings(settings);

            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var gameMock = new Moq.Mock<IGame>();
            gameMock.SetupGet(game => game.Platform).Returns(launchBoxPlatformName);
            gameMock.SetupGet(game => game.Title).Returns("Test Game");

            var dataManagerMock = new Moq.Mock<IDataManager>();
            var request = new PipelineInstallRequest(gameMock.Object, dataManagerMock.Object);

            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "rom-1",
                    PlatformId = platformId,
                    Payload = new RommPayload { FileName = "test.bin", Extension = ".bin" },
                    FileIds = new List<int> { 1 }
                },
                PlatformMapping = new PlatformMapping
                {
                    ExtractAfterDownload = false,
                    ExtractionBehavior = ExtractionBehavior.Subfolder,
                    InstallScenario = RomMbox.Models.Install.InstallScenario.Basic,
                    RomArchivePolicy = string.Empty
                },
                DownloadDirectory = tempRoot
            };

            return (context, request, logger);
        }

        private sealed class DownloadToFileRommClient : IRommClient
        {
            public Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RommPlatform>>(new List<RommPlatform>());
            }

            public Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
            {
                return Task.FromResult(new PagedResult<RommRom> { Items = new List<RommRom>() });
            }

            public Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken)
            {
                return Task.FromResult(new RommRom { Id = romId });
            }

            public Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken)
            {
                return Task.FromResult(new RommPayload { FileName = "test.bin", Extension = ".bin" });
            }

            public Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken)
            {
                return Task.FromResult(new byte[] { 1, 2, 3 });
            }

            public Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
            {
                return Task.FromResult(new byte[] { 1, 2, 3, 4 });
            }

            public Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<DownloadProgress> progress)
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var bytes = new byte[] { 1, 2, 3, 4, 5 };
                File.WriteAllBytes(destinationPath, bytes);
                progress?.Report(new DownloadProgress(bytes.Length, bytes.Length));
                return Task.FromResult<long?>(bytes.Length);
            }

            public Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken)
            {
                return Task.FromResult(new byte[] { 1 });
            }

            public Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RommSave>>(new List<RommSave>());
            }

            public Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken)
            {
                return Task.FromResult(new RommSave());
            }

            public Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken)
            {
                return Task.FromResult(new byte[] { 1 });
            }

            public Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken)
            {
                return Task.FromResult(new RommSave());
            }

            public Task<bool> ValidateSessionAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }
        }

        private sealed class MetadataStubInstaller : IPlatformInstaller, IPlatformInstallerMetadata
        {
            public MetadataStubInstaller(string platformKey, PlatformInstallerCapabilities capabilities)
            {
                PlatformKey = platformKey;
                DisplayName = platformKey;
                Capabilities = capabilities;
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }
            public PlatformInstallerCapabilities Capabilities { get; }

            public PlatformConfigDescriptor? GetConfigDescriptor() => null;

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<RomM.Platforms.Abstractions.Models.Install.InstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<RomM.Platforms.Abstractions.Models.Install.InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new RomM.Platforms.Abstractions.Models.Install.InstallResult { Success = true });
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

