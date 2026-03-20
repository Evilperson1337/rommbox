using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class RommPlatformCacheTests
    {
        [Fact]
        public async Task GetOrFetchAsync_ReusesCachedResult_ForSamePlatform()
        {
            var (cache, _, _) = CreateCache();
            var fetchCount = 0;

            var first = await cache.GetOrFetchAsync(
                "ps2",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(new[] { new RommRom { Id = "1", PlatformId = "ps2", Title = "Test" } });
                },
                CancellationToken.None);

            var second = await cache.GetOrFetchAsync(
                "ps2",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(Array.Empty<RommRom>());
                },
                CancellationToken.None);

            fetchCount.Should().Be(1);
            second.Should().BeSameAs(first);
        }

        [Fact]
        public async Task GetOrFetchAsync_DeduplicatesConcurrentRequests_ForSamePlatform()
        {
            var (cache, _, _) = CreateCache();
            var fetchCount = 0;
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<IReadOnlyList<RommRom>> Fetch(CancellationToken _)
            {
                Interlocked.Increment(ref fetchCount);
                return release.Task.ContinueWith<IReadOnlyList<RommRom>>(
                    _ => new[] { new RommRom { Id = "1", PlatformId = "snes", Title = "Mario" } },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            var tasks = Enumerable.Range(0, 5)
                .Select(_ => cache.GetOrFetchAsync("snes", Fetch, CancellationToken.None))
                .ToArray();

            release.SetResult(true);
            await Task.WhenAll(tasks);

            fetchCount.Should().Be(1);
            tasks.Select(t => t.Result).Distinct(ReferenceEqualityComparer<IReadOnlyList<RommRom>>.Instance).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetOrFetchAsync_DoesNotCacheFailures()
        {
            var (cache, _, _) = CreateCache();
            var fetchCount = 0;

            await FluentActions.Invoking(() => cache.GetOrFetchAsync(
                    "ps1",
                    _ =>
                    {
                        Interlocked.Increment(ref fetchCount);
                        throw new InvalidOperationException("boom");
                    },
                    CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();

            var result = await cache.GetOrFetchAsync(
                "ps1",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(new[] { new RommRom { Id = "2", PlatformId = "ps1", Title = "Retry" } });
                },
                CancellationToken.None);

            fetchCount.Should().Be(2);
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task Invalidate_RemovesCachedPlatform()
        {
            var (cache, _, logSink) = CreateCache();
            var fetchCount = 0;

            await cache.GetOrFetchAsync(
                "psp",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(Array.Empty<RommRom>());
                },
                CancellationToken.None);

            cache.Invalidate("psp");

            await cache.GetOrFetchAsync(
                "psp",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(Array.Empty<RommRom>());
                },
                CancellationToken.None);

            fetchCount.Should().Be(2);
            logSink.Drain().Any(m => (m.Message ?? string.Empty).Contains("[RomM Cache] INVALIDATE platform=psp", StringComparison.Ordinal)).Should().BeTrue();
        }

        [Fact]
        public async Task GetOrFetchAsync_CachesEmptyResults()
        {
            var (cache, _, _) = CreateCache();
            var fetchCount = 0;

            var first = await cache.GetOrFetchAsync(
                "vita",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(Array.Empty<RommRom>());
                },
                CancellationToken.None);

            var second = await cache.GetOrFetchAsync(
                "vita",
                _ =>
                {
                    Interlocked.Increment(ref fetchCount);
                    return Task.FromResult<IReadOnlyList<RommRom>>(new[] { new RommRom { Id = "1", PlatformId = "vita", Title = "ShouldNotFetch" } });
                },
                CancellationToken.None);

            fetchCount.Should().Be(1);
            second.Should().BeSameAs(first);
            second.Should().BeEmpty();
        }

        private static (RommPlatformCache Cache, LoggingService Logger, StubLogSink Sink) CreateCache()
        {
            var sink = new StubLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            return (new RommPlatformCache(logger), logger, sink);
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new();

            public bool Equals(T? x, T? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
