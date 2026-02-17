using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Download;
using RomMbox.Models.Romm;

namespace RomMbox.Services
{
    /// <summary>
    /// Abstraction for RomM API communication. Used by services to enable testing
    /// and alternate implementations.
    /// </summary>
    internal interface IRommClient
    {
        /// <summary>
        /// Validates the current session against the RomM server.
        /// </summary>
        Task<bool> ValidateSessionAsync(CancellationToken cancellationToken);
        /// <summary>
        /// Lists RomM platforms available on the server.
        /// </summary>
        Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken);
        /// <summary>
        /// Lists ROMs for a platform with paging and optional filters.
        /// </summary>
        Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken);
        /// <summary>
        /// Retrieves full ROM details by id.
        /// </summary>
        Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken);
        /// <summary>
        /// Retrieves download payload details for a ROM.
        /// </summary>
        Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken);
        /// <summary>
        /// Downloads payload data referenced by a RomM payload.
        /// </summary>
        Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken);
        /// <summary>
        /// Downloads ROM content as a byte array.
        /// </summary>
        Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken);
        /// <summary>
        /// Streams ROM content to disk with progress reporting.
        /// </summary>
        Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<DownloadProgress> progress);
        /// <summary>
        /// Downloads cover art or screenshot media.
        /// </summary>
        Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken);
        /// <summary>
        /// Lists save files for a ROM.
        /// </summary>
        Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken);
        /// <summary>
        /// Retrieves a save entry by id.
        /// </summary>
        Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken);
        /// <summary>
        /// Downloads save content.
        /// </summary>
        Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken);
        /// <summary>
        /// Uploads a save file to RomM.
        /// </summary>
        Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken);
    }
}
