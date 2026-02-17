using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Models;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public sealed class InstallStateServiceTests
    {
        public TestContext TestContext { get; set; }

        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private sealed class FakeCustomField : ICustomField
        {
            public string GameId { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }

        private sealed class FakeGame : IGame
        {
            private readonly System.Collections.Generic.List<ICustomField> _fields = new System.Collections.Generic.List<ICustomField>();

            public ICustomField[] GetAllCustomFields() => _fields.ToArray();

            public ICustomField AddNewCustomField()
            {
                var field = new FakeCustomField();
                _fields.Add(field);
                return field;
            }

            public bool TryRemoveCustomField(ICustomField customField) => _fields.Remove(customField);

            public string Id { get; set; }
            public string Title { get; set; }
            public string Platform { get; set; }

            public bool AggressiveWindowHiding { get; set; }
            public string ApplicationPath { get; set; }
            public string BackgroundImagePath { get; set; }
            public string BackImagePath { get; set; }
            public string Box3DImagePath { get; set; }
            public bool Broken { get; set; }
            public string Cart3DImagePath { get; set; }
            public string CartBackImagePath { get; set; }
            public string CartFrontImagePath { get; set; }
            public string ClearLogoImagePath { get; set; }
            public string CloneOf { get; set; }
            public string CommandLine { get; set; }
            public float CommunityOrLocalStarRating { get; set; }
            public float CommunityStarRating { get; set; }
            public int CommunityStarRatingTotalVotes { get; set; }
            public bool Completed { get; set; }
            public string ConfigurationCommandLine { get; set; }
            public string ConfigurationPath { get; set; }
            public DateTime DateAdded { get; set; }
            public DateTime DateModified { get; set; }
            public string DetailsWithoutPlatform { get; set; }
            public string DetailsWithPlatform { get; set; }
            public string[] Developers { get; set; }
            public bool DisableShutdownScreen { get; set; }
            public string DosBoxConfigurationPath { get; set; }
            public string Developer { get; set; }
            public string EmulatorId { get; set; }
            public bool Favorite { get; set; }
            public string FrontImagePath { get; set; }
            public System.Collections.Concurrent.BlockingCollection<string> Genres { get; set; }
            public string GenresString { get; set; }
            public bool Hide { get; set; }
            public bool HideAllNonExclusiveFullscreenWindows { get; set; }
            public bool HideMouseCursorInGame { get; set; }
            public bool? Installed { get; set; }
            public DateTime? LastPlayedDate { get; set; }
            public int? LaunchBoxDbId { get; set; }
            public string ManualPath { get; set; }
            public string MarqueeImagePath { get; set; }
            public int? MaxPlayers { get; set; }
            public string MusicPath { get; set; }
            public string Notes { get; set; }
            public bool OverrideDefaultStartupScreenSettings { get; set; }
            public int PlayCount { get; set; }
            public string PlayMode { get; set; }
            public string[] PlayModes { get; set; }
            public int PlayTime { get; set; }
            public bool Portable { get; set; }
            public string Progress { get; set; }
            public string Publisher { get; set; }
            public string[] Publishers { get; set; }
            public object RatingImage { get; set; }
            public string Rating { get; set; }
            public string Region { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public int? ReleaseYear { get; set; }
            public string ReleaseType { get; set; }
            public string RootFolder { get; set; }
            public bool ScummVmAspectCorrection { get; set; }
            public bool ScummVmFullscreen { get; set; }
            public string ScummVmGameDataFolderPath { get; set; }
            public string ScummVmGameType { get; set; }
            public string Series { get; set; }
            public string[] SeriesValues { get; set; }
            public bool ShowBack { get; set; }
            public string ScreenshotImagePath { get; set; }
            public string SortTitle { get; set; }
            public string SortTitleOrTitle { get; set; }
            public string Source { get; set; }
            public int StarRating { get; set; }
            public float StarRatingFloat { get; set; }
            public int StartupLoadDelay { get; set; }
            public string Status { get; set; }
            public string ThemeVideoPath { get; set; }
            public bool UseDosBox { get; set; }
            public bool UseScummVm { get; set; }
            public bool UseStartupScreen { get; set; }
            public string Version { get; set; }
            public string VideoPath { get; set; }
            public string VideoUrl { get; set; }
            public int? WikipediaId { get; set; }
            public string WikipediaUrl { get; set; }

            public bool AddControllerSupport(string controllerName, string controllerCategory, int? supportLevel) => false;
            public IAdditionalApplication AddNewAdditionalApplication() => null;
            public IAlternateName AddNewAlternateName() => null;
            public IMount AddNewMount() => null;
            public string Configure() => string.Empty;
            public IAdditionalApplication[] GetAllAdditionalApplications() => Array.Empty<IAdditionalApplication>();
            public IAlternateName[] GetAllAlternateNames() => Array.Empty<IAlternateName>();
            public ImageDetails[] GetAllImagesWithDetails() => Array.Empty<ImageDetails>();
            public ImageDetails[] GetAllImagesWithDetails(string imageType) => Array.Empty<ImageDetails>();
            public IMount[] GetAllMounts() => Array.Empty<IMount>();
            public string GetBigBoxDetails(bool showPlatform) => string.Empty;
            public System.Collections.Generic.KeyValuePair<IGameController, int?>[] GetControllerSupport() => Array.Empty<System.Collections.Generic.KeyValuePair<IGameController, int?>>();
            public string GetEffectiveCommandLine() => string.Empty;
            public string GetManualPath() => string.Empty;
            public string GetMusicPath() => string.Empty;
            public string GetNewManualFilePath(string extension) => string.Empty;
            public string GetNewMusicFilePath(string extension) => string.Empty;
            public string GetNewThemeVideoFilePath(string extension) => string.Empty;
            public string GetNewVideoFilePath(string extension) => string.Empty;
            public string GetNextAvailableImageFilePath(string extension, string imageType, string region) => string.Empty;
            public string GetNextVideoFilePath(string videoType, string extension) => string.Empty;
            public string GetThemeVideoPath() => string.Empty;
            public string GetVideoPath(bool prioritizeThemeVideos) => string.Empty;
            public string GetVideoPath(string videoType) => string.Empty;
            public string OpenFolder() => string.Empty;
            public string OpenManual() => string.Empty;
            public string Play() => string.Empty;
            public bool TryRemoveAdditionalApplication(IAdditionalApplication additionalApplication) => false;
            public bool TryRemoveAlternateNames(IAlternateName alternateName) => false;
            public bool TryRemoveMount(IMount mount) => false;
            public void UpdateTitleAndMigrateMedia(string newTitle) { }
        }

        [TestMethod]
        public async Task InstallStateService_CreatesAndReadsState()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var service = new InstallStateService(logger, new SettingsManager(logger));

            var state = new InstallState
            {
                LaunchBoxGameId = Guid.NewGuid().ToString(),
                RommRomId = "romm-1",
                RommPlatformId = "snes",
                ServerUrl = "http://localhost",
                InstalledPath = Path.Combine(TestContext.DeploymentDirectory, "test.rom"),
                IsInstalled = false,
                InstalledUtc = DateTimeOffset.UtcNow,
                LastValidatedUtc = DateTimeOffset.UtcNow
            };

            await service.UpsertStateAsync(state, CancellationToken.None);
            var read = await service.GetStateAsync(state.LaunchBoxGameId, CancellationToken.None);

            Assert.IsNotNull(read);
            Assert.AreEqual(state.RommRomId, read.RommRomId);
        }

        [TestMethod]
        public async Task InstallStateService_IsRomMSourcedGame_DetectsFields()
        {
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var service = new InstallStateService(logger, new SettingsManager(logger));
            var game = new FakeGame { Id = Guid.NewGuid().ToString() };
            var source = game.AddNewCustomField();
            source.Name = "RomM.Source";
            source.Value = "RomM";

            var result = service.IsRomMSourcedGame(game);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task InstallStateService_ValidatesInstallPath()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var service = new InstallStateService(logger, new SettingsManager(logger));
            var path = Path.Combine(TestContext.DeploymentDirectory, "romm-installed.rom");
            File.WriteAllText(path, "stub");
            var state = new InstallState
            {
                LaunchBoxGameId = Guid.NewGuid().ToString(),
                InstalledPath = path,
                IsInstalled = false
            };

            await service.UpsertStateAsync(state, CancellationToken.None);
            var game = new FakeGame { Id = state.LaunchBoxGameId };
            game.Source = "RomM";

            var installed = await service.IsGameInstalledAsync(game, CancellationToken.None);

            Assert.IsTrue(installed);
        }
    }
}
