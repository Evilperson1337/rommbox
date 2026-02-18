using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Download;
using RomMbox.Models.Romm;
using RomMbox.Services;

namespace RomMbox.Tests.Utilities
{
    internal sealed class StubRommClient : IRommClient
    {
        public IReadOnlyList<RommPlatform> Platforms { get; set; } = new List<RommPlatform>();

        public Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Platforms ?? new List<RommPlatform>());
        }

        public Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<RommRom> { Items = new List<RommRom>() });
        }

        public Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken)
        {
#pragma warning disable CS8625
            return Task.FromResult<RommRom>(null);
#pragma warning restore CS8625
        }

        public Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken)
        {
#pragma warning disable CS8625
            return Task.FromResult<RommPayload>(null);
#pragma warning restore CS8625
        }

        public Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken)
        {
            return Task.FromResult(new byte[0]);
        }

        public Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
        {
            return Task.FromResult(new byte[0]);
        }

        public Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<DownloadProgress> progress)
        {
            return Task.FromResult<long?>(0);
        }

        public Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult(new byte[0]);
        }

        public Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RommSave>>(new List<RommSave>());
        }

        public Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken)
        {
#pragma warning disable CS8625
            return Task.FromResult<RommSave>(null);
#pragma warning restore CS8625
        }

        public Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(new byte[0]);
        }

        public Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken)
        {
#pragma warning disable CS8625
            return Task.FromResult<RommSave>(null);
#pragma warning restore CS8625
        }

        public Task<bool> ValidateSessionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
