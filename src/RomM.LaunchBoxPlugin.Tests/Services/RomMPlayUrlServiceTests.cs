using FluentAssertions;
using RomMbox.Services;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class RomMPlayUrlServiceTests
    {
        [Theory]
        [InlineData("", "rom1")]
        [InlineData("https://romm.local", "")]
        public void BuildPlayUrl_ShouldReturnEmpty_WhenMissingInputs(string serverUrl, string romId)
        {
            var service = new RomMPlayUrlService(TestLogger.Create());

            var result = service.BuildPlayUrl(serverUrl, romId);

            result.Should().BeEmpty();
        }

        [Fact]
        public void BuildPlayUrl_ShouldReturnEmpty_WhenNullInputs()
        {
            var service = new RomMPlayUrlService(TestLogger.Create());

            service.BuildPlayUrl(null, "rom1").Should().BeEmpty();
            service.BuildPlayUrl("https://romm.local", null).Should().BeEmpty();
        }

        [Fact]
        public void BuildPlayUrl_ShouldReturnEmpty_WhenServerUrlInvalid()
        {
            var service = new RomMPlayUrlService(TestLogger.Create());

            var result = service.BuildPlayUrl("not a url", "rom1");

            result.Should().BeEmpty();
        }

        [Fact]
        public void BuildPlayUrl_ShouldNormalizeTrailingSlash()
        {
            var service = new RomMPlayUrlService(TestLogger.Create());

            var result = service.BuildPlayUrl("https://romm.local/", "rom1");

            result.Should().Be("https://romm.local/rom/rom1/ejs");
        }

        [Fact]
        public void BuildPlayUrl_ShouldComposeExpectedPath()
        {
            var service = new RomMPlayUrlService(TestLogger.Create());

            var result = service.BuildPlayUrl("https://romm.local", "rom123");

            result.Should().Be("https://romm.local/rom/rom123/ejs");
        }
    }
}
