using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Models.PlatformMapping;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public class ArchiveServiceTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        [TestMethod]
        public void IsSupportedArchive_ReturnsTrueForKnownExtensions()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));

            Assert.IsTrue(service.IsSupportedArchive("test.zip"));
            Assert.IsTrue(service.IsSupportedArchive("test.7z"));
            Assert.IsTrue(service.IsSupportedArchive("test.rar"));
        }

        [TestMethod]
        public void IsSupportedArchive_ReturnsFalseForUnknownExtensions()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));

            Assert.IsFalse(service.IsSupportedArchive("test.txt"));
            Assert.IsFalse(service.IsSupportedArchive(string.Empty));
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void ExtractAsync_ThrowsForMissingFile()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));
            service.ExtractAsync("missing.zip", "output", ExtractionBehavior.Subfolder, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void ExtractAsync_ThrowsForUnsupportedArchive()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));
            var tempFile = Path.GetTempFileName();
            try
            {
                service.ExtractAsync(tempFile, "output", ExtractionBehavior.Subfolder, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void DetectInstallType_UsesFilenameMarkers()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));

            Assert.AreEqual(RomMbox.Models.Install.InstallType.Portable, service.DetectInstallType("C:\\games\\Title (Portable).zip", null));
            Assert.AreEqual(RomMbox.Models.Install.InstallType.Installer, service.DetectInstallType("C:\\games\\Title (Installer).zip", null));
        }

        [TestMethod]
        public void DetectInstallType_UsesSetupExePresence()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var setupPath = Path.Combine(tempDir, "setup.exe");
            File.WriteAllText(setupPath, string.Empty);

            try
            {
                Assert.AreEqual(RomMbox.Models.Install.InstallType.Installer, service.DetectInstallType("C:\\games\\Title.zip", tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void DetectInstallType_ReturnsPortableWhenNoSetupExe()
        {
            var service = new ArchiveService(new LoggingService(new NullSink()), new SettingsManager(new LoggingService(new NullSink())));
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var markerPath = Path.Combine(tempDir, "game.exe");
            File.WriteAllText(markerPath, string.Empty);

            try
            {
                Assert.AreEqual(RomMbox.Models.Install.InstallType.Portable, service.DetectInstallType("C:\\games\\Title.zip", tempDir));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
