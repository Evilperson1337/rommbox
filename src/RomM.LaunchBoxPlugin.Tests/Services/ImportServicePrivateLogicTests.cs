using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using RomMbox.Services;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class ImportServicePrivateLogicTests
    {
        [Theory]
        [InlineData("")]
        public void NormalizeAgeRating_ShouldReturnNotRated_WhenMissing(string rating)
        {
            var result = InvokePrivate<string>("NormalizeAgeRating", rating);

            result.Should().Be("Not Rated");
        }

        [Fact]
        public void NormalizeAgeRating_ShouldReturnNotRated_WhenNull()
        {
#pragma warning disable CS8625
#pragma warning disable CS8600
            var result = InvokePrivate<string>("NormalizeAgeRating", (object)null);
#pragma warning restore CS8600
#pragma warning restore CS8625

            result.Should().Be("Not Rated");
        }

        [Theory]
        [InlineData("E", "E - Everyone")]
        [InlineData("Everyone", "E - Everyone")]
        [InlineData("E10+", "E10+ - Everyone 10+")]
        [InlineData("Everyone 10+", "E10+ - Everyone 10+")]
        [InlineData("T", "T - Teen")]
        [InlineData("teen", "T - Teen")]
        [InlineData("M", "M - Mature")]
        [InlineData("mature", "M - Mature")]
        public void NormalizeAgeRating_ShouldMapKnownRatings(string rating, string expected)
        {
            var result = InvokePrivate<string>("NormalizeAgeRating", rating);

            result.Should().Be(expected);
        }

        [Fact]
        public void NormalizePlayMode_ShouldDetectCooperative()
        {
            var result = InvokePrivate<string>("NormalizePlayMode", (object)new[] { "Co-op" });

            result.Should().Be("Cooperative");
        }

        [Fact]
        public void NormalizePlayMode_ShouldDetectMultiplayer()
        {
            var result = InvokePrivate<string>("NormalizePlayMode", (object)new[] { "multi player" });

            result.Should().Be("Multiplayer");
        }

        [Fact]
        public void NormalizePlayMode_ShouldDefaultToSinglePlayer()
        {
            var result = InvokePrivate<string>("NormalizePlayMode", (object)Array.Empty<string>());

            result.Should().Be("Single Player");
        }

        [Fact]
        public void ParseTargetImportFiles_ShouldSplitByMultipleSeparators()
        {
            var result = InvokePrivate<System.Collections.Generic.IReadOnlyList<string>>("ParseTargetImportFiles", "game.exe, setup.exe; install.exe|main.exe");

            result.Should().BeEquivalentTo(new[] { "game.exe", "setup.exe", "install.exe", "main.exe" });
        }

        [Fact]
        public void ExtractYearFromReleaseDate_ShouldReturnYear()
        {
            var year = ImportService.ExtractYearFromReleaseDate(new DateTimeOffset(2022, 6, 1, 0, 0, 0, TimeSpan.Zero));

            year.Should().Be(2022);
        }

        [Fact]
        public void NormalizeTitleForMatch_ShouldRemoveBracketedSegmentsAndDiacritics()
        {
            var result = InvokePrivate<string>("NormalizeTitleForMatch", "Pokemon (USA) [rev 1]");

            result.Should().Be("pokemon");
        }

        [Fact]
        public void ExtractCommandLineValue_ShouldReturnFlagValue()
        {
            var result = InvokePrivate<string>("ExtractCommandLineValue", "-L \"cores\\core.dll\" -v", "-L");

            result.Should().Be("cores\\core.dll");
        }

        [Fact]
        public void ComputeTitleSimilarity_ShouldReturnFullMatch()
        {
            var result = InvokePrivate<double>("ComputeTitleSimilarity", "super mario", "super mario");

            result.Should().Be(1.0);
        }

        private static T InvokePrivate<T>(string name, params object[] args)
        {
            var method = typeof(ImportService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull($"Expected to find private static method '{name}'.");
            return (T)method!.Invoke(null, args)!;
        }
    }
}
