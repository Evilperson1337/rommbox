using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class PlatformMappingServiceTests
    {
        [Fact]
        public async Task DiscoverPlatformsAsync_ShouldMapUsingPredefinedAliases()
        {
            ResetPlatformCache();
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings());
            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);

            var rommClient = new StubRommClient
            {
                Platforms = new[]
                {
                    new RommPlatform { Id = "nes", Name = "NES" }
                }
            };

            var service = new PlatformMappingService(logger, settingsManager, rommClient);

            var result = await service.DiscoverPlatformsAsync(CancellationToken.None);

            result.Mappings.Should().ContainSingle();
            result.Mappings[0].LaunchBoxPlatformName.Should().Be("Nintendo Entertainment System");
            result.Mappings[0].AutoMapped.Should().BeTrue();
        }

        [Fact]
        public async Task DiscoverPlatformsAsync_ShouldPreferSavedMapping()
        {
            ResetPlatformCache();
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping
            {
                RommPlatformId = "romm1",
                LaunchBoxPlatformName = "Custom Platform"
            }));
            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);

            var rommClient = new StubRommClient
            {
                Platforms = new[]
                {
                    new RommPlatform { Id = "romm1", Name = "Custom RomM" }
                }
            };

            var service = new PlatformMappingService(logger, settingsManager, rommClient);

            var result = await service.DiscoverPlatformsAsync(CancellationToken.None);

            result.Mappings.Should().ContainSingle();
            result.Mappings[0].LaunchBoxPlatformName.Should().Be("Custom Platform");
            result.Mappings[0].AutoMapped.Should().BeFalse();
        }

        [Fact]
        public async Task DiscoverPlatformsAsync_ShouldPopulateSavedFlags()
        {
            ResetPlatformCache();
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);
            settingsManager.SavePlatformMapping(new PlatformMapping
            {
                RommPlatformId = "romm1",
                LaunchBoxPlatformName = "Custom Platform",
                DisableAutoImport = true,
                ExtractionBehavior = ExtractionBehavior.Direct
            });

            var rommClient = new StubRommClient
            {
                Platforms = new[]
                {
                    new RommPlatform { Id = "romm1", Name = "Custom RomM" }
                }
            };

            var service = new PlatformMappingService(logger, settingsManager, rommClient);

            var result = await service.DiscoverPlatformsAsync(CancellationToken.None);

            result.Mappings[0].DisableAutoImport.Should().BeTrue();
            result.Mappings[0].ExtractionBehavior.Should().Be(ExtractionBehavior.Direct);
        }

        [Fact]
        public async Task DiscoverPlatformsAsync_ShouldReturnEmpty_WhenClientReturnsNone()
        {
            ResetPlatformCache();
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings());
            var logger = TestLogger.Create();
            var settingsManager = new SettingsManager(logger);

            var rommClient = new StubRommClient
            {
                Platforms = Array.Empty<RommPlatform>()
            };

            var service = new PlatformMappingService(logger, settingsManager, rommClient);

            var result = await service.DiscoverPlatformsAsync(CancellationToken.None);

            result.Mappings.Should().BeEmpty();
        }

        private static void ResetPlatformCache()
        {
            var field = typeof(PlatformMappingService).GetField("PlatformsCache", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = field?.GetValue(null) as System.Collections.IDictionary;
            cache?.Clear();
        }
    }
}
