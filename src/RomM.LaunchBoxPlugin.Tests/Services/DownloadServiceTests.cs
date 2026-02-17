using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Settings;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public class DownloadServiceTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private sealed class FakeRommClient : IRommClient
        {
            public Task<bool> ValidateSessionAsync(CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<System.Collections.Generic.IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken) => Task.FromResult<System.Collections.Generic.IReadOnlyList<RommPlatform>>(Array.Empty<RommPlatform>());
            public Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<RommRom>());
            public Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken) => Task.FromResult<RommRom>(null);
            public Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken) => Task.FromResult<RommPayload>(new RommPayload
            {
                DownloadUrl = "https://example.invalid/rom",
                FileName = "demo.rom",
                Extension = ".rom"
            });
            public Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1, 2, 3 });
            public Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1, 2, 3 });
            public Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<RomMbox.Models.Download.DownloadProgress> progress)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath));
                System.IO.File.WriteAllBytes(destinationPath, new byte[] { 1, 2, 3 });
                progress?.Report(new RomMbox.Models.Download.DownloadProgress(3, 3));
                return Task.FromResult<long?>(3);
            }
            public Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
            public Task<System.Collections.Generic.IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken) => Task.FromResult<System.Collections.Generic.IReadOnlyList<RommSave>>(Array.Empty<RommSave>());
            public Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken) => Task.FromResult<RommSave>(null);
            public Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
            public Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        [TestMethod]
        public void DownloadRomAsync_SkipsExtractionWhenDisabled()
        {
            var settings = new SettingsManager(new LoggingService(new NullSink()));
            var service = new DownloadService(new LoggingService(new NullSink()), new FakeRommClient(), new ArchiveService(new LoggingService(new NullSink()), settings), settings);
            var result = service.DownloadRomAsync(new RommRom { Id = "1", Name = "Demo" }, System.IO.Path.GetTempPath(), "https://example.invalid", ExtractionBehavior.Subfolder, false, CancellationToken.None, null, null)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.Success);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ArchivePath));
            Assert.IsTrue(string.IsNullOrWhiteSpace(result.ExtractedPath));
            Assert.AreEqual(RomMbox.Models.Install.InstallType.Unknown, result.InstallType);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void DownloadRomAsync_ThrowsWhenServerUrlMissing()
        {
            var settings = new SettingsManager(new LoggingService(new NullSink()));
            var service = new DownloadService(new LoggingService(new NullSink()), new FakeRommClient(), new ArchiveService(new LoggingService(new NullSink()), settings), settings);
            service.DownloadRomAsync(new RommRom { Id = "1" }, "C:\\temp", string.Empty, ExtractionBehavior.Subfolder, true, CancellationToken.None, null, null)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
    }
}
