using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Models.Import;
using RomMbox.Services;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public sealed class ImportServiceTests
    {
        public TestContext TestContext { get; set; }

        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private sealed class FakeRommClient : IRommClient
        {
            private readonly Queue<PagedResult<RommRom>> _pages;

            public FakeRommClient(params PagedResult<RommRom>[] pages)
            {
                _pages = new Queue<PagedResult<RommRom>>(pages ?? Array.Empty<PagedResult<RommRom>>());
            }

            public Task<bool> ValidateSessionAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }

            public Task<IReadOnlyList<RommPlatform>> ListPlatformsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RommPlatform>>(new List<RommPlatform>());
            }

            public Task<PagedResult<RommRom>> ListRomsByPlatformAsync(string platformId, int page, int pageSize, RommFilters filters, CancellationToken cancellationToken)
            {
                if (_pages.Count == 0)
                {
                    return Task.FromResult(new PagedResult<RommRom> { Items = new List<RommRom>() });
                }

                return Task.FromResult(_pages.Dequeue());
            }

            public Task<RommRom> GetRomDetailsAsync(string romId, CancellationToken cancellationToken)
            {
                return Task.FromResult<RommRom>(null);
            }

            public Task<RommPayload> GetDownloadInfoAsync(string romId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadRomPayloadAsync(RommPayload payload, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadRomContentAsync(string romId, string fileName, string fileIds, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<long?> DownloadRomContentToFileAsync(string romId, string fileName, string fileIds, string destinationPath, CancellationToken cancellationToken, IProgress<RomMbox.Models.Download.DownloadProgress> progress)
            {
                throw new NotSupportedException();
            }

            public Task<byte[]> DownloadMediaAsync(string mediaUrl, CancellationToken cancellationToken)
            {
                return Task.FromResult(Array.Empty<byte>());
            }

            public Task<IReadOnlyList<RommSave>> ListSavesAsync(string romId, string platformId, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RommSave>>(new List<RommSave>());
            }

            public Task<RommSave> GetSaveAsync(int saveId, CancellationToken cancellationToken)
            {
                return Task.FromResult<RommSave>(null);
            }

            public Task<byte[]> DownloadSaveAsync(string downloadPath, CancellationToken cancellationToken)
            {
                return Task.FromResult(Array.Empty<byte>());
            }

            public Task<RommSave> UploadSaveAsync(string romId, string emulator, string filePath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakePlatformMappingService : PlatformMappingService
        {
            private readonly PlatformMappingResult _result;

            public FakePlatformMappingService(LoggingService logger, SettingsManager settingsManager, IRommClient client, PlatformMappingResult result)
                : base(logger, settingsManager, client)
            {
                _result = result;
            }

            public override Task<PlatformMappingResult> DiscoverPlatformsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_result);
            }
        }

        private sealed class FakeDataManager : IDataManager
        {
            private readonly List<IGame> _games = new List<IGame>();
            private readonly List<IPlatform> _platforms = new List<IPlatform>();

            public void AddPlatform(IPlatform platform)
            {
                _platforms.Add(platform);
            }

            public IGame[] GetAllGames()
            {
                return _games.ToArray();
            }

            public IPlatform GetPlatformByName(string name)
            {
                foreach (var platform in _platforms)
                {
                    if (string.Equals(platform?.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return platform;
                    }
                }

                return null;
            }

            public IGame AddNewGame(string title)
            {
                var game = new FakeGame { Title = title, Id = Guid.NewGuid().ToString("N") };
                _games.Add(game);
                return game;
            }

            public void Save(bool wait)
            {
            }

            public IEmulator[] GetAllEmulators() => Array.Empty<IEmulator>();
            public IPlatform[] GetAllPlatforms() => _platforms.ToArray();
            public IPlatformCategory[] GetAllPlatformCategories() => Array.Empty<IPlatformCategory>();
            public IPlaylist[] GetAllPlaylists() => Array.Empty<IPlaylist>();
            public IParent[] GetAllParents() => Array.Empty<IParent>();
            public IGame GetGameById(string id)
            {
                return _games.FirstOrDefault(game => string.Equals(game?.Id, id, StringComparison.OrdinalIgnoreCase));
            }
            public IEmulator GetEmulatorById(string id) => null;
            public IPlaylist GetPlaylistById(string id) => null;
            public IPlatformCategory GetPlatformCategoryByName(string name) => null;
            public IList<IPlatform> GetRootPlatformsCategoriesPlaylists() => new List<IPlatform>();
            public IList<IGameController> GetGameControllers() => new List<IGameController>();
            public string AddGameController(string controllerName, string controllerCategory, string[] associatedPlatforms) => string.Empty;
            public IEmulator AddNewEmulator() => null;
            public IPlatform AddNewPlatform(string name) => null;
            public IPlatformCategory AddNewPlatformCategory(string name) => null;
            public IPlaylist AddNewPlaylist(string name) => null;
            public bool TryRemoveGame(IGame game) => false;
            public bool TryRemoveEmulator(IEmulator emulator) => false;
            public bool TryRemovePlatform(IPlatform platform) => false;
            public bool TryRemovePlatformCategory(IPlatformCategory platformCategory) => false;
            public bool TryRemovePlaylist(IPlaylist playlist) => false;
            public void ReloadIfNeeded() { }
            public void ForceReload() { }
            public void BackgroundReloadSave(Action changes) { }
        }

        private sealed class FakePlatform : IPlatform
        {
            public string Name { get; set; }
            public string FrontImagesFolder { get; set; }
            public string ScreenshotImagesFolder { get; set; }
            public bool HideInBigBox { get; set; }
            public bool IsEmulated { get; set; }
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
            public string ImageType { get; set; }
            public string LastGameId { get; set; }
            public string ManualsFolder { get; set; }
            public string Manufacturer { get; set; }
            public string MaxControllers { get; set; }
            public string Media { get; set; }
            public string Memory { get; set; }
            public string MusicFolder { get; set; }
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

            public IGame[] GetAllGames(bool includeHidden, bool includeBroken) => Array.Empty<IGame>();
            public IGame[] GetAllGames(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => Array.Empty<IGame>();
            public IPlatformDocument[] GetAllPlatformDocuments() => Array.Empty<IPlatformDocument>();
            public IPlatformFolder[] GetAllPlatformFolders() => Array.Empty<IPlatformFolder>();
            public IList<IPlatform> GetChildren() => new List<IPlatform>();
            public int GetGameCount(bool includeHidden, bool includeBroken) => 0;
            public int GetGameCount(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => 0;
            public string GetNewPlatformLogoPath(string url) => string.Empty;
            public string GetNewPlatformVideoPath(string url) => string.Empty;
            public IPlatformFolder GetPlatformFolderByImageType(string imageType) => null;
            public string GetPlatformVideoPath(bool fallBackToGameVideos, bool allowThemePath) => string.Empty;
            public bool HasGames(bool includeHidden, bool includeBroken) => false;
            public bool HasGames(bool includeHidden, bool includeBroken, bool excludeGamesMissingVideos, bool excludeGamesMissingBoxFrontImage, bool excludeGamesMissingScreenshotImage, bool excludeGamesMissingClearLogoImage, bool excludeGamesMissingBackgroundImage) => false;
        }

        private sealed class FakeGame : IGame
        {
            private readonly List<ICustomField> _customFields = new List<ICustomField>();

            public string Id { get; set; }
            public string Title { get; set; }
            public string Developer { get; set; }
            public string Publisher { get; set; }
            public string Notes { get; set; }
            public string Rating { get; set; }
            public string Region { get; set; }
            public string GenresString { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public int? ReleaseYear { get; set; }
            public string ScreenshotImagePath { get; set; }
            public string FrontImagePath { get; set; }

            public ICustomField AddNewCustomField()
            {
                var field = new FakeCustomField();
                _customFields.Add(field);
                return field;
            }

            public ICustomField[] GetAllCustomFields()
            {
                return _customFields.ToArray();
            }

            public bool TryRemoveCustomField(ICustomField customField)
            {
                return _customFields.Remove(customField);
            }

            public string GetNextAvailableImageFilePath(string extension, string imageType, string region)
            {
                return System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + extension);
            }

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
            public KeyValuePair<IGameController, int?>[] GetControllerSupport() => Array.Empty<KeyValuePair<IGameController, int?>>();
            public string GetEffectiveCommandLine() => string.Empty;
            public string GetManualPath() => string.Empty;
            public string GetMusicPath() => string.Empty;
            public string GetNewManualFilePath(string extension) => string.Empty;
            public string GetNewMusicFilePath(string extension) => string.Empty;
            public string GetNewThemeVideoFilePath(string extension) => string.Empty;
            public string GetNewVideoFilePath(string extension) => string.Empty;
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
            public string EmulatorId { get; set; }
            public bool Favorite { get; set; }
            public System.Collections.Concurrent.BlockingCollection<string> Genres { get; set; }
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
            public bool OverrideDefaultStartupScreenSettings { get; set; }
            public int PlayCount { get; set; }
            public string PlayMode { get; set; }
            public string[] PlayModes { get; set; }
            public int PlayTime { get; set; }
            public bool Portable { get; set; }
            public string Progress { get; set; }
            public string[] Publishers { get; set; }
            public object RatingImage { get; set; }
            public string Platform { get; set; }
            public string ReleaseType { get; set; }
            public string RootFolder { get; set; }
            public bool ScummVmAspectCorrection { get; set; }
            public bool ScummVmFullscreen { get; set; }
            public string ScummVmGameDataFolderPath { get; set; }
            public string ScummVmGameType { get; set; }
            public string Series { get; set; }
            public string[] SeriesValues { get; set; }
            public bool ShowBack { get; set; }
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
        }

        private sealed class FakeCustomField : ICustomField
        {
            public string GameId { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_ValidPlatform_ReturnsSuccessResult()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom { Id = "1", Title = "Test Game", PlatformId = "snes" }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            var result = await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);

            Assert.AreEqual(1, result.TotalRoms);
            Assert.AreEqual(1, result.SuccessfulImports);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task ImportPlatformCatalogAsync_NoPlatformMapping_ThrowsException()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var client = new FakeRommClient();
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, new PlatformMappingResult());
            var dataManager = new FakeDataManager();
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task ImportPlatformCatalogAsync_NoLaunchBoxPlatform_ThrowsException()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var client = new FakeRommClient();
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Missing" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_DuplicateGame_SkipsByDefault()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom { Id = "1", Title = "Test Game", PlatformId = "snes" }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);
            var result = await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);

            Assert.AreEqual(1, result.SkippedDuplicates);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_SoftSync_UpdatesExistingGame()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom { Id = "1", Title = "Test Game", PlatformId = "snes", Description = "Updated description" }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var existing = dataManager.AddNewGame("Test Game") as FakeGame;
            existing.Platform = "Super Nintendo Entertainment System";
            existing.Notes = "Old";
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            var result = await service.ImportPlatformCatalogAsync(
                "snes",
                downloadDuringImport: false,
                allowDuplicates: true,
                matchByRomId: false,
                matchByMd5: false,
                matchByTitle: true,
                cancellationToken: CancellationToken.None,
                progress: null);

            Assert.AreEqual(1, result.SuccessfulImports);
            Assert.AreEqual("Old", existing.Notes);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_FileNameMatch_SoftSyncsMissingMetadata()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom
                    {
                        Id = "2",
                        Title = "Different Title",
                        PlatformId = "snes",
                        Developer = "RomM Dev",
                        Payload = new RommPayload { FileName = "match.smc" }
                    }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var existing = dataManager.AddNewGame("Existing Title") as FakeGame;
            existing.Platform = "Super Nintendo Entertainment System";
            existing.ApplicationPath = "C:\\Games\\match.smc";
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            var result = await service.ImportPlatformCatalogAsync(
                "snes",
                downloadDuringImport: false,
                allowDuplicates: true,
                matchByRomId: false,
                matchByMd5: false,
                matchByTitle: true,
                cancellationToken: CancellationToken.None,
                progress: null);

            Assert.AreEqual(1, result.SuccessfulImports);
            Assert.AreEqual("RomM Dev", existing.Developer);
            Assert.AreEqual(1, result.MatchCandidates.Count);
            Assert.AreEqual(existing.Id, result.MatchCandidates[0].LaunchBoxGameId);
        }


        [TestMethod]
        public async Task ImportPlatformCatalogAsync_IgnoresPreviouslyRejectedMatch()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom { Id = "3", Title = "Match Me", PlatformId = "snes" }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var existing = dataManager.AddNewGame("Match Me") as FakeGame;
            existing.Platform = "Super Nintendo Entertainment System";
            var ignoreStore = new MatchIgnoreStore(logger);
            ignoreStore.AddIgnore("snes", "3", existing.Id);

            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);
            var result = await service.ImportPlatformCatalogAsync(
                "snes",
                downloadDuringImport: false,
                allowDuplicates: true,
                matchByRomId: false,
                matchByMd5: false,
                matchByTitle: true,
                cancellationToken: CancellationToken.None,
                progress: null);

            Assert.AreEqual(0, result.MatchCandidates.Count);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_AllowDuplicates_ImportsAgain()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom>
                {
                    new RommRom { Id = "1", Title = "Test Game", PlatformId = "snes" }
                },
                Total = 1
            };
            var client = new FakeRommClient(romsPage, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);
            var result = await service.ImportPlatformCatalogAsync(
                "snes",
                downloadDuringImport: false,
                allowDuplicates: true,
                matchByRomId: true,
                matchByMd5: true,
                matchByTitle: true,
                cancellationToken: CancellationToken.None,
                progress: null);

            Assert.AreEqual(1, result.SuccessfulImports);
            Assert.AreEqual(0, result.SkippedDuplicates);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_MultiplePages_FetchesAllRoms()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var page1 = new PagedResult<RommRom>
            {
                Items = new List<RommRom> { new RommRom { Id = "1", Title = "Game1", PlatformId = "snes" } },
                Total = 2
            };
            var page2 = new PagedResult<RommRom>
            {
                Items = new List<RommRom> { new RommRom { Id = "2", Title = "Game2", PlatformId = "snes" } },
                Total = 2
            };
            var client = new FakeRommClient(page1, page2, new PagedResult<RommRom> { Items = new List<RommRom>() });
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);

            var result = await service.ImportPlatformCatalogAsync("snes", CancellationToken.None);

            Assert.AreEqual(2, result.SuccessfulImports);
        }

        [TestMethod]
        public void ExtractYearFromReleaseDate_ValidDate_ReturnsYear()
        {
            var year = ImportService.ExtractYearFromReleaseDate(new DateTimeOffset(new DateTime(1995, 1, 1)));

            Assert.AreEqual(1995, year);
        }

        [TestMethod]
        public void ExtractYearFromReleaseDate_InvalidDate_ReturnsNull()
        {
            var year = ImportService.ExtractYearFromReleaseDate(null);

            Assert.IsNull(year);
        }

        [TestMethod]
        public async Task ImportPlatformCatalogAsync_CancellationToken_ThrowsOperationCanceledException()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", TestContext?.DeploymentDirectory);
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var settingsManager = new SettingsManager(logger);
            var romsPage = new PagedResult<RommRom>
            {
                Items = new List<RommRom> { new RommRom { Id = "1", Title = "Game1", PlatformId = "snes" } },
                Total = 1
            };
            var client = new FakeRommClient(romsPage);
            var mappingResult = new PlatformMappingResult
            {
                Mappings = new List<PlatformMapping>
                {
                    new PlatformMapping { RommPlatformId = "snes", LaunchBoxPlatformName = "Super Nintendo Entertainment System" }
                }
            };
            var mappingService = new FakePlatformMappingService(logger, settingsManager, client, mappingResult);
            var dataManager = new FakeDataManager();
            dataManager.AddPlatform(new FakePlatform { Name = "Super Nintendo Entertainment System" });
            var service = new ImportService(logger, settingsManager, mappingService, client, dataManager);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                service.ImportPlatformCatalogAsync("snes", cts.Token));
        }
    }
}
