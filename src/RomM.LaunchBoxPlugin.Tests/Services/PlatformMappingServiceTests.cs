using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public class PlatformMappingServiceTests
    {
        public TestContext TestContext { get; set; }

        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private sealed class FakeRommClient : IRommClient
        {
            private readonly IReadOnlyList<RommPlatform> _platforms;

            public FakeRommClient(IReadOnlyList<RommPlatform> platforms)
            {
                _platforms = platforms;
            }

            public Task<bool> ValidateSessionAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }

            public Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_platforms);
            }

            public Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<RomMbox.Models.Download.DownloadProgress> progress)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        [TestMethod]
        public async Task DiscoverPlatformsAsync_WithValidPlatforms_ShouldReturnMappings()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var platforms = new List<RommPlatform>
            {
                new RommPlatform { Id = "snes", Name = "SNES" }
            };
            var client = new FakeRommClient(platforms);
            var service = new PlatformMappingService(logger, settingsManager, client);

            var result = await service.DiscoverPlatformsAsync(CancellationToken.None);

            Assert.AreEqual(1, result.Mappings.Count);
            Assert.AreEqual("snes", result.Mappings[0].RommPlatformId);
            Assert.AreEqual("SNES", result.Mappings[0].RommPlatformName);
        }

        [TestMethod]
        public void GetLaunchBoxPlatformName_WithNoMapping_ShouldReturnRomMName()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var client = new FakeRommClient(new List<RommPlatform>());
            var service = new PlatformMappingService(logger, settingsManager, client);

            var result = service.GetLaunchBoxPlatformName("unknown", "Neo Geo");

            Assert.AreEqual("Neo Geo", result);
        }

        [TestMethod]
        public void SaveMapping_ShouldPersistMapping()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var client = new FakeRommClient(new List<RommPlatform>());
            var service = new PlatformMappingService(logger, settingsManager, client);

            service.SaveMapping(new PlatformMapping
            {
                RommPlatformId = "snes",
                RommPlatformName = "SNES",
                LaunchBoxPlatformName = "Super Nintendo Entertainment System",
                AutoMapped = false
            });

            var saved = settingsManager.GetPlatformMapping("snes");
            Assert.IsNotNull(saved);
            Assert.AreEqual("Super Nintendo Entertainment System", saved.LaunchBoxPlatformName);
        }
    }
}
