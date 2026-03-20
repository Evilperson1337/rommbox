using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Romm;
using RomMbox.Services.Logging;

namespace RomMbox.Services
{
    /// <summary>
    /// Thread-safe in-memory cache for platform ROM lists.
    /// </summary>
    internal sealed class RommPlatformCache : IRommPlatformCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly LoggingService _logger;

        public RommPlatformCache(LoggingService logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<RommRom>> GetOrFetchAsync(
            string platformId,
            Func<CancellationToken, Task<IReadOnlyList<RommRom>>> fetchFunc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                throw new ArgumentException("PlatformId is required.", nameof(platformId));
            }

            if (fetchFunc == null)
            {
                throw new ArgumentNullException(nameof(fetchFunc));
            }

            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                if (_entries.TryGetValue(platformId, out var existingEntry))
                {
                    _logger?.Info($"[RomM Cache] HIT platform={platformId}");
                    return existingEntry.GetValueAsync(cancellationToken);
                }

                _logger?.Info($"[RomM Cache] MISS platform={platformId} -> fetching from API");
                var createdEntry = new CacheEntry(
                    platformId,
                    ct => FetchAndFinalizeAsync(platformId, fetchFunc, ct),
                    RemoveEntry);

                var actualEntry = _entries.GetOrAdd(platformId, createdEntry);
                if (ReferenceEquals(actualEntry, createdEntry))
                {
                    return actualEntry.GetValueAsync(cancellationToken);
                }

                _logger?.Info($"[RomM Cache] HIT platform={platformId}");
                return actualEntry.GetValueAsync(cancellationToken);
            }
        }

        public void Invalidate(string platformId)
        {
            if (string.IsNullOrWhiteSpace(platformId))
            {
                return;
            }

            if (_entries.TryRemove(platformId, out _))
            {
                _logger?.Info($"[RomM Cache] INVALIDATE platform={platformId}");
            }
        }

        public void InvalidateAll()
        {
            var count = _entries.Count;
            _entries.Clear();
            _logger?.Info($"[RomM Cache] INVALIDATE ALL count={count}");
        }

        private async Task<IReadOnlyList<RommRom>> FetchAndFinalizeAsync(
            string platformId,
            Func<CancellationToken, Task<IReadOnlyList<RommRom>>> fetchFunc,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger?.Info($"[RomM Cache] API FETCH platform={platformId}");
                var result = await fetchFunc(cancellationToken).ConfigureAwait(false);
                return result ?? Array.Empty<RommRom>();
            }
            catch (OperationCanceledException)
            {
                RemoveEntry(platformId);
                throw;
            }
            catch
            {
                RemoveEntry(platformId);
                throw;
            }
        }

        private void RemoveEntry(string platformId)
        {
            _entries.TryRemove(platformId, out _);
        }

        private sealed class CacheEntry
        {
            private readonly Func<CancellationToken, Task<IReadOnlyList<RommRom>>> _factory;
            private readonly Action<string> _removeEntry;
            private readonly object _syncRoot = new();
            private Task<IReadOnlyList<RommRom>> _dataTask;

            public CacheEntry(
                string platformId,
                Func<CancellationToken, Task<IReadOnlyList<RommRom>>> factory,
                Action<string> removeEntry)
            {
                PlatformId = platformId;
                _factory = factory;
                _removeEntry = removeEntry;
                CreatedAt = DateTime.UtcNow;
            }

            public string PlatformId { get; }

            public DateTime CreatedAt { get; }

            public Task<IReadOnlyList<RommRom>> GetValueAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = Volatile.Read(ref _dataTask);
                if (task == null)
                {
                    lock (_syncRoot)
                    {
                        task ??= _dataTask;
                        if (task == null)
                        {
                            task = _factory(CancellationToken.None);
                            _dataTask = task;
                            task.ContinueWith(
                                static (t, state) =>
                                {
                                    if (t.IsCanceled || t.IsFaulted)
                                    {
                                        var entry = (CacheEntry)state;
                                        entry._removeEntry(entry.PlatformId);
                                    }
                                },
                                this,
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                        }
                    }
                }

                return task;
            }
        }
    }
}
