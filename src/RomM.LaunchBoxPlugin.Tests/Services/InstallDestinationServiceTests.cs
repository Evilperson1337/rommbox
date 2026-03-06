using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformFolders;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class InstallDestinationServiceTests
    {
        [Fact]
        public async Task ResolveInstallLocationAsync_UsesPlatformFolderProperty()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Game Boy Advance", folder: "Games\\Nintendo Game Boy Advance");
            var game = BuildGame("Advance Wars", "Nintendo Game Boy Advance");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Nintendo Game Boy Advance"));
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_UsesPlatformFoldersWhenFolderPropertyMissing()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Super Nintendo", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Game", "Games\\Super Nintendo")
            });
            var game = BuildGame("Zelda", "Super Nintendo");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Super Nintendo"));
        }

        [Fact]
        public void GetPlatformGamesFolder_ResolvesConfiguredGameFolder()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Game Boy Advance", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Game", "Games\\Nintendo Game Boy Advance")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformGamesFolder("Nintendo Game Boy Advance");

            result.Should().Be(Path.Combine(launchBoxRoot, "Games", "Nintendo Game Boy Advance"));
        }

        [Fact]
        public void GetPlatformManualsFolder_ResolvesConfiguredManualsFolder()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Super Nintendo", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Manual", "Manuals\\Super Nintendo")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformManualsFolder("Super Nintendo");

            result.Should().Be(Path.Combine(launchBoxRoot, "Manuals", "Super Nintendo"));
        }

        [Fact]
        public void GetPlatformMusicFolder_ResolvesConfiguredMusicFolder()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Arcade", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Music", "Music\\Arcade")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformMusicFolder("Arcade");

            result.Should().Be(Path.Combine(launchBoxRoot, "Music", "Arcade"));
        }

        [Fact]
        public void GetPlatformImagesFolder_ResolvesConfiguredImagesFolder()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Windows", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Images", "Images\\Windows")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformImagesFolder("Windows");

            result.Should().Be(Path.Combine(launchBoxRoot, "Images", "Windows"));
        }

        [Fact]
        public void GetPlatformVideosFolder_ResolvesConfiguredVideosFolder()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Entertainment System", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Video", "Videos\\Nintendo Entertainment System")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformVideosFolder("Nintendo Entertainment System");

            result.Should().Be(Path.Combine(launchBoxRoot, "Videos", "Nintendo Entertainment System"));
        }

        [Fact]
        public void GetPlatformFolder_ReturnsEmptyWhenPlatformMissing()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(manager => manager.GetPlatformByName("Missing"))
                .Returns((IPlatform)null);
            using var dataManagerScope = new PluginDataManagerScope(dataManager.Object);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformFolder("Missing", PlatformFolderType.Games);

            result.Should().BeEmpty();
        }

        [Fact]
        public void GetPlatformGamesFolder_DoesNotMatchGameplayMediaTypes()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Game Boy Advance", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Screenshot - Gameplay", "Images\\Nintendo Game Boy Advance\\Screenshot - Gameplay"),
                BuildPlatformFolder("Video", "Videos\\Nintendo Game Boy Advance")
            });
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = BuildService().GetPlatformGamesFolder("Nintendo Game Boy Advance");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_IgnoresImagesPlatformFolders()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Game Boy Advance", folder: string.Empty, platformFolders: new[]
            {
                BuildPlatformFolder("Screenshot - Gameplay", "Images\\Nintendo Game Boy Advance\\Screenshot - Gameplay")
            });
            var game = BuildGame("Advance Wars 2", "Nintendo Game Boy Advance");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Nintendo Game Boy Advance"));
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_UsesPlatformXmlFolderWhenConfigured()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var xmlPath = Path.Combine(launchBoxRoot, "Data", "Platforms", "Arcade.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(xmlPath) ?? string.Empty);
            File.WriteAllText(xmlPath, WrapPlatformXml("Games\\Arcade"));

            var platform = BuildPlatform("Arcade", folder: string.Empty);
            var game = BuildGame("Metal Slug", "Arcade");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Arcade"));
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_SkipsGamesRootWhenUsingExistingGameHints()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var existingGame = BuildGame("Existing", "Nintendo Game Boy Advance", "Games\\Existing\\Existing.gba");
            var platform = BuildPlatform("Nintendo Game Boy Advance", folder: string.Empty, games: new[] { existingGame });
            var game = BuildGame("Advance Wars", "Nintendo Game Boy Advance");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Nintendo Game Boy Advance"));
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_FallsBackToPlatformFolderWhenMetadataMissing()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Nintendo Entertainment System", folder: string.Empty);
            var game = BuildGame("Metroid", "Nintendo Entertainment System");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Nintendo Entertainment System"));
        }

        [Fact]
        public async Task ResolveInstallLocationAsync_UsesWindowsDefaultForWindowsPlatform()
        {
            using var temp = new TempDirectory();
            var launchBoxRoot = CreateLaunchBoxRoot(temp);
            var platform = BuildPlatform("Windows", folder: string.Empty);
            var game = BuildGame("Doom", "Windows");
            var dataManager = BuildDataManager(platform);
            using var dataManagerScope = new PluginDataManagerScope(dataManager);
            using var launchBoxScope = new LaunchBoxRootScope(launchBoxRoot);

            var result = await BuildService()
                .ResolveInstallLocationAsync(game, InstallerMode.Manual, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.InstallDirectory.Should().Be(Path.Combine(launchBoxRoot, "Games", "Windows"));
        }

        private static InstallDestinationService BuildService()
        {
            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            return new InstallDestinationService(logger, settingsManager);
        }

        private static IDataManager BuildDataManager(IPlatform platform)
        {
            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(manager => manager.GetPlatformByName(platform.Name)).Returns(platform);
            return dataManager.Object;
        }

        private static IGame BuildGame(string title, string platform, string applicationPath = "")
        {
            var game = new Mock<IGame>();
            game.SetupGet(g => g.Title).Returns(title);
            game.SetupGet(g => g.Platform).Returns(platform);
            game.SetupGet(g => g.ApplicationPath).Returns(applicationPath);
            return game.Object;
        }

        private static IPlatform BuildPlatform(string name, string folder, IGame[]? games = null, IPlatformFolder[]? platformFolders = null)
        {
            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns(name);
            platform.SetupGet(p => p.Folder).Returns(folder ?? string.Empty);
            platform.Setup(p => p.GetAllGames(true, true)).Returns(games ?? Array.Empty<IGame>());
            platform.Setup(p => p.GetAllPlatformFolders()).Returns(platformFolders ?? Array.Empty<IPlatformFolder>());
            return platform.Object;
        }

        private static string CreateLaunchBoxRoot(TempDirectory temp)
        {
            var root = Path.Combine(temp.Path, "LaunchBox");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Games"));
            Directory.CreateDirectory(Path.Combine(root, "Plugins"));
            Directory.CreateDirectory(Path.Combine(root, "Core"));
            Directory.CreateDirectory(Path.Combine(root, "Data", "Platforms"));
            File.WriteAllText(Path.Combine(root, "LaunchBox.exe"), string.Empty);
            return root;
        }

        private static string WrapPlatformXml(string folderValue)
        {
            return $"<LaunchBoxPlatform><Folder>{folderValue}</Folder></LaunchBoxPlatform>";
        }

        private sealed class PluginDataManagerScope : IDisposable
        {
            private readonly IDataManager? _prior;

            public PluginDataManagerScope(IDataManager manager)
            {
                _prior = PluginHelper.DataManager;
                SetStaticProperty(typeof(PluginHelper), "DataManager", manager);
            }

            public void Dispose()
            {
                SetStaticProperty(typeof(PluginHelper), "DataManager", _prior);
            }

            private static void SetStaticProperty(Type type, string propertyName, object? value)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                property?.SetValue(null, value);
            }
        }

        private sealed class LaunchBoxRootScope : IDisposable
        {
            private readonly string _prior;

            public LaunchBoxRootScope(string launchBoxRoot)
            {
                _prior = Environment.GetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT") ?? string.Empty;
                Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", launchBoxRoot);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", _prior);
            }
        }

        private static IPlatformFolder BuildPlatformFolder(string mediaType, string folderPath)
        {
            var folder = new Mock<IPlatformFolder>();
            folder.SetupGet(f => f.MediaType).Returns(mediaType);
            folder.SetupGet(f => f.FolderPath).Returns(folderPath);
            return folder.Object;
        }
    }
}
