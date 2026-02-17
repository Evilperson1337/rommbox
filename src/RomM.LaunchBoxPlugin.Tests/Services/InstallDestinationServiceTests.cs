using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public class InstallDestinationServiceTests
    {
        private sealed class CapturingLogSink : ILogSink
        {
            public List<LogMessage> Messages { get; } = new List<LogMessage>();

            public void Write(LogMessage message)
            {
                Messages.Add(message);
            }
        }

        private sealed class FakePlatformFolder : IPlatformFolder
        {
            public string MediaType { get; set; }
            public string FolderPath { get; set; }
            public string Platform { get; set; }
        }

        private sealed class FakePlatform : IPlatform
        {
            private readonly IPlatformFolder[] _folders;

            public FakePlatform(params IPlatformFolder[] folders)
            {
                _folders = folders ?? Array.Empty<IPlatformFolder>();
            }

            public IGame[] GetAllGames(bool includeHidden, bool includeBroken) => Array.Empty<IGame>();
            public IGame[] GetAllGames(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => Array.Empty<IGame>();
            public IPlatformDocument[] GetAllPlatformDocuments() => Array.Empty<IPlatformDocument>();
            public IPlatformFolder[] GetAllPlatformFolders() => _folders;
            public IList<IPlatform> GetChildren() => new List<IPlatform>();
            public int GetGameCount(bool includeHidden, bool includeBroken) => 0;
            public int GetGameCount(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => 0;
            public string GetNewPlatformLogoPath(string url) => string.Empty;
            public string GetNewPlatformVideoPath(string url) => string.Empty;
            public IPlatformFolder GetPlatformFolderByImageType(string imageType) => null;
            public string GetPlatformVideoPath(bool fallBackToGameVideos, bool allowThemePath) => string.Empty;
            public bool HasGames(bool includeHidden, bool includeBroken) => false;
            public bool HasGames(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => false;

            public string BackgroundImagePath { get; set; }
            public string BackImagesFolder { get; set; }
            public string BannerImagePath { get; set; }
            public string BannerImagesFolder { get; set; }
            public string BigBoxTheme { get; set; }
            public string BigBoxView { get; set; }
            public string Category { get; set; }
            public string ClearLogoImagePath { get; set; }
            public string ClearLogoImagesFolder { get; set; }
            public string Cpu { get; set; }
            public string Default3DBoxImagePath { get; set; }
            public string Default3DCartImagePath { get; set; }
            public string DefaultBoxImagePath { get; set; }
            public string DefaultCartImagePath { get; set; }
            public string Developer { get; set; }
            public string DeviceImagePath { get; set; }
            public string Display { get; set; }
            public string FanartImagesFolder { get; set; }
            public string Folder { get; set; }
            public string Graphics { get; set; }
            public bool HideInBigBox { get; set; }
            public string ImageType { get; set; }
            public bool IsEmulated { get; set; }
            public string LastGameId { get; set; }
            public string ManualsFolder { get; set; }
            public string Manufacturer { get; set; }
            public string MaxControllers { get; set; }
            public string Media { get; set; }
            public string Memory { get; set; }
            public string MusicFolder { get; set; }
            public string Name { get; set; }
            public string NestedName { get; set; }
            public string Notes { get; set; }
            public string PlatformCategoryClearLogoImagePath { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public string ScrapeAs { get; set; }
            public string SortTitle { get; set; }
            public string SortTitleOrTitle { get; set; }
            public string Sound { get; set; }
            public string SteamBannerImagesFolder { get; set; }
            public string VideoPath { get; set; }
            public string VideosFolder { get; set; }
        }

        [TestMethod]
        public void IsWindowsPlatform_ReturnsTrueForWindowsAndPC()
        {
            Assert.IsTrue(InstallDestinationService.IsWindowsPlatform("Windows"));
            Assert.IsTrue(InstallDestinationService.IsWindowsPlatform("Windows 95"));
            Assert.IsTrue(InstallDestinationService.IsWindowsPlatform("PC"));
            Assert.IsTrue(InstallDestinationService.IsWindowsPlatform("PC Windows"));
        }

        [TestMethod]
        public void IsWindowsPlatform_ReturnsFalseForConsolePlatforms()
        {
            Assert.IsFalse(InstallDestinationService.IsWindowsPlatform("Nintendo 64"));
            Assert.IsFalse(InstallDestinationService.IsWindowsPlatform("PlayStation"));
        }

        [TestMethod]
        public void PluginSettings_Defaults_EnableWindowsPrompting()
        {
            var settings = new PluginSettings();
            settings.ApplyDefaults();

            Assert.IsTrue(settings.GetPromptForWindowsInstallDirectory());
        }

        [TestMethod]
        public void InstallDestinationService_CanBeConstructedWithSettings()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);

            var service = new InstallDestinationService(logger, settingsManager);
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void TryResolvePlatformFolderFromFolders_UsesFolderPath()
        {
            var sink = new CapturingLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var settingsManager = new SettingsManager(logger);
            var service = new InstallDestinationService(logger, settingsManager);

            var folder = new FakePlatformFolder
            {
                FolderPath = @"D:\Roms\SNES",
                MediaType = "ROM",
                Platform = "Super Nintendo"
            };
            var platform = new FakePlatform(folder) { Name = "Super Nintendo" };

            var method = typeof(InstallDestinationService)
                .GetMethod("TryResolvePlatformFolderFromFolders", BindingFlags.NonPublic | BindingFlags.Instance);
            var resolved = method?.Invoke(service, new object[] { platform }) as string;

            Assert.AreEqual(@"D:\Roms\SNES", resolved);
            Assert.IsTrue(sink.Messages.Any(message => message.Message.Contains("FolderPath")));
        }

        [TestMethod]
        public void TryResolvePlatformFolderFromFolders_SkipsImageMediaFolders()
        {
            var sink = new CapturingLogSink();
            var logger = new LoggingService(LogLevel.Debug, sink);
            var settingsManager = new SettingsManager(logger);
            var service = new InstallDestinationService(logger, settingsManager);

            var imageFolder = new FakePlatformFolder
            {
                FolderPath = @"D:\LaunchBox\Images\Windows\Front",
                MediaType = "Image",
                Platform = "Windows"
            };
            var romFolder = new FakePlatformFolder
            {
                FolderPath = @"D:\LaunchBox\Games\Windows",
                MediaType = "ROM",
                Platform = "Windows"
            };
            var platform = new FakePlatform(imageFolder, romFolder) { Name = "Windows" };

            var method = typeof(InstallDestinationService)
                .GetMethod("TryResolvePlatformFolderFromFolders", BindingFlags.NonPublic | BindingFlags.Instance);
            var resolved = method?.Invoke(service, new object[] { platform }) as string;

            Assert.AreEqual(@"D:\LaunchBox\Games\Windows", resolved);
            Assert.IsTrue(sink.Messages.Any(message => message.Message.Contains("Skipping platform folder")));
        }

        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }
    }
}
