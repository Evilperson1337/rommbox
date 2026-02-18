using FluentAssertions;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class RommPlayabilityTests
    {
        [Fact]
        public void IsPlayablePlatform_ShouldReturnFalse_WhenNameMissing()
        {
            RommPlayability.IsPlayablePlatform(null).Should().BeFalse();
            RommPlayability.IsPlayablePlatform(string.Empty).Should().BeFalse();
            RommPlayability.IsPlayablePlatform(" ").Should().BeFalse();
        }

        [Theory]
        [InlineData("Nintendo 64")]
        [InlineData("Sega Saturn")]
        public void IsPlayablePlatform_ShouldReturnTrue_ForKnownPlatforms(string platform)
        {
            RommPlayability.IsPlayablePlatform(platform).Should().BeTrue();
        }

        [Theory]
        [InlineData("Sega Genesis/Megadrive", "Sega Mega Drive/Genesis")]
        [InlineData("Sony Playstation", "PlayStation")]
        public void IsPlayablePlatform_ShouldResolveAliases(string input, string canonical)
        {
            RommPlayability.IsPlayablePlatform(input).Should().BeTrue();
            RommPlayability.IsPlayablePlatform(canonical).Should().BeTrue();
        }

        [Fact]
        public void IsPlayablePlatform_WithSettingsManager_ShouldFallbackToRommName()
        {
            using var temp = new TempDirectory();
            using var env = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            var settingsStore = new TestSettingsStore(temp.Path);
            var settings = TestSettingsStore.CreateSettings();
            settings.UseSavedCredentials = false;
            settingsStore.WriteSettings(settings);
            var logger = TestLogger.Create(LogLevel.Debug);
            var settingsManager = new SettingsManager(logger);

            var result = RommPlayability.IsPlayablePlatform("romm1", "Nintendo DS", settingsManager);

            result.Should().BeTrue();
        }
    }
}
