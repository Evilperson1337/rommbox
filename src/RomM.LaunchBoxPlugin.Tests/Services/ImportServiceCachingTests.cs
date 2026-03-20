using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomMbox.Models.Import;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class ImportServiceCachingTests
    {
        [Fact]
        public async Task ListPlatformRomsAsync_UsesCache_WhenNoProgressRequested()
        {
            var client = new CountingRommClient
            {
                Responses =
                {
                    ["ps2"] = new List<RommRom>
                    {
                        new RommRom { Id = "1", PlatformId = "ps2", Title = "Game A" },
                        new RommRom { Id = "2", PlatformId = "ps2", Title = "Game B" }
                    }
                }
            };

            var service = CreateImportService(client, out _);

            var first = await service.ListPlatformRomsAsync("ps2", CancellationToken.None);
            var second = await service.ListPlatformRomsAsync("ps2", CancellationToken.None);

            client.ListCalls.Should().Be(1);
            second.Should().BeSameAs(first);
            second.Should().HaveCount(2);
        }

        [Fact]
        public async Task ListPlatformRomsAsync_RefreshAfterInvalidate_RefetchesPlatform()
        {
            var client = new CountingRommClient
            {
                Responses =
                {
                    ["snes"] = new List<RommRom>
                    {
                        new RommRom { Id = "1", PlatformId = "snes", Title = "Chrono Trigger" }
                    }
                }
            };

            var service = CreateImportService(client, out _);

            await service.ListPlatformRomsAsync("snes", CancellationToken.None);
            service.InvalidatePlatformRoms("snes");
            await service.ListPlatformRomsAsync("snes", CancellationToken.None);

            client.ListCalls.Should().Be(2);
        }

        [Fact]
        public async Task ListPlatformRomsAsync_WithProgress_StillUsesCache()
        {
            var client = new CountingRommClient
            {
                Responses =
                {
                    ["ps3"] = new List<RommRom>
                    {
                        new RommRom { Id = "1", PlatformId = "ps3", Title = "Demon's Souls" }
                    }
                }
            };

            var service = CreateImportService(client, out _);
            var progress = new Progress<ImportProgress>(_ => { });

            await service.ListPlatformRomsAsync("ps3", CancellationToken.None, progress);
            await service.ListPlatformRomsAsync("ps3", CancellationToken.None, progress);

            client.ListCalls.Should().Be(1);
        }

        private static ImportService CreateImportService(CountingRommClient client, out StubLogSink sink)
        {
            sink = new StubLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var settingsManager = new SettingsManager(logger);
            var mappingService = new PlatformMappingService(logger, settingsManager, client);
            var cache = new RommPlatformCache(logger);
            return new ImportService(logger, settingsManager, mappingService, client, platformCache: cache);
        }

        private sealed class CountingRommClient : StubRommClient
        {
            public Dictionary<string, List<RommRom>> Responses { get; } = new(System.StringComparer.OrdinalIgnoreCase);

            public int ListCalls { get; private set; }

            public override Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
            {
                ListCalls++;
                Responses.TryGetValue(platformId, out var items);
                var pageItems = page == 1 ? items ?? new List<RommRom>() : new List<RommRom>();
                return Task.FromResult(new PagedResult<RommRom>
                {
                    Items = pageItems,
                    Total = items?.Count ?? 0,
                    Offset = page == 1 ? 0 : pageSize,
                    PageSize = pageSize
                });
            }
        }
    }
}
