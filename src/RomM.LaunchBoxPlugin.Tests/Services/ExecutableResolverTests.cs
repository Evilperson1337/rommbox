using System.Text.Json;
using FluentAssertions;
using RomMbox.Models.Install;
using RomMbox.Services.Install;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class ExecutableResolverTests
    {
        [Fact]
        public void Resolve_ShouldFail_WhenRootMissing()
        {
            var resolver = new ExecutableResolver(TestLogger.Create());

            var result = resolver.Resolve("Z:\\missing-path");

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("not found");
        }

        [Fact]
        public void Resolve_ShouldUseManifestExecutable_WhenPresent()
        {
            using var temp = new TempDirectory();
            temp.CreateTextFile("manifest.json", JsonSerializer.Serialize(new ManifestExecutable
            {
                Executable = "game.exe",
                Arguments = new[] { "-fullscreen" }
            }));
            temp.CreateFile("game.exe", new byte[] { 0x1 });

            var resolver = new ExecutableResolver(TestLogger.Create());

            var result = resolver.Resolve(temp.Path);

            result.Success.Should().BeTrue();
            result.RequiresConfirmation.Should().BeFalse();
            result.ExecutablePath.Should().Be(System.IO.Path.Combine(temp.Path, "game.exe"));
            result.Arguments.Should().ContainSingle().Which.Should().Be("-fullscreen");
        }

        [Fact]
        public void Resolve_ShouldSkipUninstallerAndSelectSingleExecutable()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("unins000.exe", new byte[] { 0x1 });
            temp.CreateFile("game.exe", new byte[] { 0x1 });

            var resolver = new ExecutableResolver(TestLogger.Create());

            var result = resolver.Resolve(temp.Path);

            result.Success.Should().BeTrue();
            result.RequiresConfirmation.Should().BeFalse();
            result.ExecutablePath.Should().Be(System.IO.Path.Combine(temp.Path, "game.exe"));
        }

        [Fact]
        public void Resolve_ShouldRequireConfirmation_WhenMultipleCandidates()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("game.exe", new byte[] { 0x1 });
            temp.CreateFile("setup.exe", new byte[] { 0x1 });

            var resolver = new ExecutableResolver(TestLogger.Create());

            var result = resolver.Resolve(temp.Path);

            result.Success.Should().BeTrue();
            result.RequiresConfirmation.Should().BeTrue();
            result.Candidates.Should().HaveCount(2);
            result.ExecutablePath.Should().Be(System.IO.Path.Combine(temp.Path, "game.exe"));
        }

        [Fact]
        public void Resolve_ShouldIgnoreExcludedRoots()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("dlc\\dlc.exe", new byte[] { 0x1 });
            temp.CreateFile("game.exe", new byte[] { 0x1 });

            var resolver = new ExecutableResolver(TestLogger.Create());

            var result = resolver.Resolve(temp.Path, new[] { "dlc" });

            result.Success.Should().BeTrue();
            result.RequiresConfirmation.Should().BeFalse();
            result.ExecutablePath.Should().Be(System.IO.Path.Combine(temp.Path, "game.exe"));
        }
    }
}
