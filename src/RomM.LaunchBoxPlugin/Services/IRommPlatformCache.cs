using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Romm;

namespace RomMbox.Services
{
    /// <summary>
    /// In-memory cache for RomM platform ROM lists.
    /// </summary>
    internal interface IRommPlatformCache
    {
        Task<IReadOnlyList<RommRom>> GetOrFetchAsync(
            string platformId,
            Func<CancellationToken, Task<IReadOnlyList<RommRom>>> fetchFunc,
            CancellationToken cancellationToken);

        void Invalidate(string platformId);

        void InvalidateAll();
    }
}
