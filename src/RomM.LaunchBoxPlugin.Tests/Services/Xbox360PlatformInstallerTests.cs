using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Xbox360;
using RomMbox.Models;
using RomMbox.Plugin;
using RomMbox.Services;
using RomMbox.Services.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class Xbox360PlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_DirectIso_InstallsToGameDirectory()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var source = Path.Combine(temp.Path, "download", "Halo3.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "iso");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var expectedRoot = Path.Combine(root, "Halo 3");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(expectedRoot, "Halo3.iso"));
            result.InstallRootPath.Should().Be(expectedRoot);
            File.Exists(result.ExecutablePath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_ExtractedLayoutWithDefaultXex_InstallsFolderAndLaunchesXex()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Halo 3");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "default.xex"), "xex");
            await File.WriteAllTextAsync(Path.Combine(extracted, "readme.txt"), "meta");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var installedRoot = Path.Combine(root, "Halo 3");
            var installedXex = Path.Combine(installedRoot, "default.xex");
            result.Success.Should().BeTrue();
            result.InstallRootPath.Should().Be(installedRoot);
            result.ExecutablePath.Should().Be(installedXex);
            File.Exists(installedXex).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_GodLayout_Unsupported_FailsTransparently()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Halo3");
            var godPath = Path.Combine(extracted, "Content", "0000000000000000", "4D5307E6");
            Directory.CreateDirectory(godPath);
            await File.WriteAllTextAsync(Path.Combine(godPath, "00007000"), "god");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ExtractedPath = extracted
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("GOD/content layout");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works_AndPreservesPlatformDirectory()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Halo 3");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "default.xex");
            await File.WriteAllTextAsync(installedPath, "xex");

            var installer = new Xbox360PlatformInstaller();

            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "xbox360",
                IsInstalled = true,
                InstalledPath = installedPath
            }, CancellationToken.None);
            detect.IsInstalled.Should().BeTrue();

            var verifyBefore = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyBefore.IsValid.Should().BeTrue();

            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Halo 3",
                InstalledPath = installedPath,
                InstallRootPath = gameDir
            }, new Progress<InstallProgress>(), CancellationToken.None);
            uninstall.Success.Should().BeTrue();
            File.Exists(installedPath).Should().BeFalse();
            Directory.Exists(gameDir).Should().BeFalse();
            Directory.Exists(platformDir).Should().BeTrue();

            var verifyAfter = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = installedPath
            }, CancellationToken.None);
            verifyAfter.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task InstallAsync_MultiDiscIso_InstallsAllDiscs_AndReturnsAdditionalApplications()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Lost Odyssey");
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(Path.Combine(extracted, "Lost Odyssey (Disc 1).iso"), "disc1");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Lost Odyssey (Disc 2).iso"), "disc2");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Lost Odyssey",
                InstallDirectory = root,
                ExtractedPath = extracted,
                RomSettings = new RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings()
            }, new Progress<InstallProgress>(), CancellationToken.None);

            var installedRoot = Path.Combine(root, "Lost Odyssey");
            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.Combine(installedRoot, "Lost Odyssey (Disc 1).iso"));
            result.AdditionalApplications.Should().ContainSingle();
            result.AdditionalApplications.Single().ApplicationPath.Should().Be(Path.Combine(installedRoot, "Lost Odyssey (Disc 2).iso"));
            File.Exists(result.ExecutablePath).Should().BeTrue();
            File.Exists(result.AdditionalApplications.Single().ApplicationPath).Should().BeTrue();
        }

        [Fact]
        public async Task InstallAsync_InstallsDetectedUpdateAndDlcPackages_WithoutBlockingBaseGame()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            var extracted = Path.Combine(temp.Path, "staging", "Halo 3");
            Directory.CreateDirectory(extracted);
            var iso = Path.Combine(extracted, "Halo_3_4D5307E6.iso");
            await File.WriteAllTextAsync(iso, "iso");
            CreateZip(Path.Combine(extracted, "update.zip"), "Content/4D5307E6/000B0000/tu.bin", "update");
            CreateZip(Path.Combine(extracted, "dlc.zip"), "Content/4D5307E6/00000002/dlc.bin", "dlc");
            var xeniaExe = Path.Combine(temp.Path, "Emulators", "Xenia", "xenia.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var installer = new Xbox360PlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Halo 3",
                InstallDirectory = root,
                ExtractedPath = extracted,
                RomSettings = new RomM.Platforms.Abstractions.Models.Rom.RomInstallSettings
                {
                    EmulatorExecutablePath = xeniaExe
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(result.ExecutablePath).Should().BeTrue();
            File.Exists(Path.Combine(temp.Path, "Emulators", "Xenia", "content", "0000000000000000", "4D5307E6", "000B0000", "tu.bin")).Should().BeTrue();
            File.Exists(Path.Combine(temp.Path, "Emulators", "Xenia", "content", "0000000000000000", "4D5307E6", "00000002", "dlc.bin")).Should().BeTrue();
        }

        [Fact]
        public async Task UninstallAsync_Removes_XeniaTitleContentFolder_WhenTitleIdAndEmulatorPathAreAvailable()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Naruto Rise of a Ninja");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "Naruto - Rise of a Ninja (USA).iso");
            await File.WriteAllTextAsync(installedPath, "iso");

            var xeniaExe = Path.Combine(temp.Path, "Emulators", "Xenia Canary", "xenia_canary.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var titleContentRoot = Path.Combine(temp.Path, "Emulators", "Xenia Canary", "localstate", "Content", "555307E5");
            var dlcDirectory = Path.Combine(titleContentRoot, "00000002");
            var updateDirectory = Path.Combine(titleContentRoot, "000B0000");
            Directory.CreateDirectory(dlcDirectory);
            Directory.CreateDirectory(updateDirectory);
            await File.WriteAllTextAsync(Path.Combine(dlcDirectory, "marker.bin"), "dlc");
            await File.WriteAllTextAsync(Path.Combine(updateDirectory, "tu.bin"), "update");

            var installer = new Xbox360PlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Naruto: Rise of a Ninja",
                InstalledPath = installedPath,
                InstallRootPath = gameDir,
                PlatformContentId = "555307E5",
                EmulatorExecutablePath = xeniaExe
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            Directory.Exists(titleContentRoot).Should().BeFalse();
        }

        [Fact]
        public async Task UninstallAsync_Recovers_TitleId_FromInstalledPath_WhenInstallStateDidNotRecordIt()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Halo 3");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "Halo_3_4D5307E6.iso");
            await File.WriteAllTextAsync(installedPath, "iso");

            var xeniaExe = Path.Combine(temp.Path, "Emulators", "Xenia Canary", "xenia_canary.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var titleContentRoot = Path.Combine(temp.Path, "Emulators", "Xenia Canary", "localstate", "Content", "4D5307E6");
            var dlcDirectory = Path.Combine(titleContentRoot, "00000002");
            Directory.CreateDirectory(dlcDirectory);
            await File.WriteAllTextAsync(Path.Combine(dlcDirectory, "dlc.bin"), "dlc");

            var otherTitleRoot = Path.Combine(temp.Path, "Emulators", "Xenia Canary", "localstate", "Content", "555307E5");
            var otherTitleDlcDirectory = Path.Combine(otherTitleRoot, "00000002");
            Directory.CreateDirectory(otherTitleDlcDirectory);
            await File.WriteAllTextAsync(Path.Combine(otherTitleDlcDirectory, "other.bin"), "keep");

            var installer = new Xbox360PlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Halo 3",
                InstalledPath = installedPath,
                InstallRootPath = gameDir,
                EmulatorExecutablePath = xeniaExe
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            Directory.Exists(titleContentRoot).Should().BeFalse();
            Directory.Exists(otherTitleRoot).Should().BeTrue();
        }

        [Fact]
        public async Task UninstallAsync_Removes_Legacy_XeniaContentLayout_WithoutTouchingOtherTitles()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Halo 3");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "Halo_3_4D5307E6.iso");
            await File.WriteAllTextAsync(installedPath, "iso");

            var xeniaExe = Path.Combine(temp.Path, "Emulators", "Xenia", "xenia.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var legacyTitleRoot = Path.Combine(temp.Path, "Emulators", "Xenia", "content", "0000000000000000", "4D5307E6");
            var legacyDlcDirectory = Path.Combine(legacyTitleRoot, "00000002");
            var legacyUpdateDirectory = Path.Combine(legacyTitleRoot, "000B0000");
            Directory.CreateDirectory(legacyDlcDirectory);
            Directory.CreateDirectory(legacyUpdateDirectory);
            await File.WriteAllTextAsync(Path.Combine(legacyDlcDirectory, "dlc.bin"), "dlc");
            await File.WriteAllTextAsync(Path.Combine(legacyUpdateDirectory, "tu.bin"), "update");

            var otherTitleRoot = Path.Combine(temp.Path, "Emulators", "Xenia", "content", "0000000000000000", "555307E5");
            var otherTitleDlcDirectory = Path.Combine(otherTitleRoot, "00000002");
            Directory.CreateDirectory(otherTitleDlcDirectory);
            await File.WriteAllTextAsync(Path.Combine(otherTitleDlcDirectory, "keep.bin"), "keep");

            var installer = new Xbox360PlatformInstaller();
            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Halo 3",
                InstalledPath = installedPath,
                InstallRootPath = gameDir,
                PlatformContentId = "4D5307E6",
                EmulatorExecutablePath = xeniaExe
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            Directory.Exists(legacyTitleRoot).Should().BeFalse();
            Directory.Exists(otherTitleRoot).Should().BeTrue();
        }

        [Fact]
        public async Task RomMUninstallService_UninstallAsync_Removes_XeniaContent_And_Clears_LaunchBox_State()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            using var launchBoxScope = new LaunchBoxRootScope(CreateLaunchBoxRoot(temp));
            using var registryScope = new PlatformInstallerRegistryScope(new PlatformInstallerRegistry(new System.Collections.Generic.Dictionary<string, RomM.Platforms.Abstractions.IPlatformInstaller>
            {
                ["xbox360"] = new Xbox360PlatformInstaller()
            }));

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var gameId = Guid.NewGuid().ToString("N");
            var platformRoot = Path.Combine(temp.Path, "LaunchBox", "Games", "Microsoft Xbox 360");
            var gameDir = Path.Combine(platformRoot, "Halo 3");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "Halo_3_4D5307E6.iso");
            await File.WriteAllTextAsync(installedPath, "iso");

            var xeniaExe = Path.Combine(temp.Path, "LaunchBox", "Emulators", "Xenia Canary", "xenia_canary.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var titleContentRoot = Path.Combine(temp.Path, "LaunchBox", "Emulators", "Xenia Canary", "localstate", "Content", "4D5307E6");
            var dlcDirectory = Path.Combine(titleContentRoot, "00000002");
            var updateDirectory = Path.Combine(titleContentRoot, "000B0000");
            Directory.CreateDirectory(dlcDirectory);
            Directory.CreateDirectory(updateDirectory);
            await File.WriteAllTextAsync(Path.Combine(dlcDirectory, "dlc.bin"), "dlc");
            await File.WriteAllTextAsync(Path.Combine(updateDirectory, "tu.bin"), "update");

            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-halo3",
                RommPlatformId = "30",
                WindowsInstallType = "Portable",
                InstallRootPath = gameDir,
                InstalledPath = installedPath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns("Microsoft Xbox 360");
            game.SetupGet(g => g.Title).Returns("Halo 3");
            game.SetupGet(g => g.EmulatorId).Returns("xenia-canary");
            game.SetupProperty(g => g.ApplicationPath, Path.Combine("Games", "Microsoft Xbox 360", "Halo 3", "Halo_3_4D5307E6.iso"));
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Microsoft Xbox 360");
            platform.SetupGet(p => p.Folder).Returns(Path.Combine(temp.Path, "LaunchBox", "Games", "Microsoft Xbox 360"));

            var emulator = new Mock<IEmulator>();
            emulator.SetupGet(e => e.Id).Returns("xenia-canary");
            emulator.SetupGet(e => e.ApplicationPath).Returns(xeniaExe);

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Microsoft Xbox 360")).Returns(platform.Object);
            dataManager.Setup(dm => dm.GetEmulatorById("xenia-canary")).Returns(emulator.Object);
            dataManager.Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>())).Callback<Action>(callback => callback?.Invoke());

            var service = new RomMUninstallService(logger, installStateService);
            var result = await service.UninstallAsync(game.Object, dataManager.Object, CancellationToken.None, new Progress<RomMbox.Models.Install.UninstallProgress>());

            result.Success.Should().BeTrue();
            Directory.Exists(gameDir).Should().BeFalse();
            Directory.Exists(titleContentRoot).Should().BeFalse();
            game.Object.ApplicationPath.Should().BeEmpty();
            game.Object.Installed.Should().BeFalse();
            game.Object.Status.Should().Be("Available Remotely");
        }

        [Fact]
        public async Task RomMUninstallService_Uses_Db_PlatformContentId_For_XeniaCleanup_WhenFilenameLacksTitleId()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);
            using var launchBoxScope = new LaunchBoxRootScope(CreateLaunchBoxRoot(temp));
            using var registryScope = new PlatformInstallerRegistryScope(new PlatformInstallerRegistry(new System.Collections.Generic.Dictionary<string, RomM.Platforms.Abstractions.IPlatformInstaller>
            {
                ["xbox360"] = new Xbox360PlatformInstaller()
            }));

            var logger = new LoggingService(LogLevel.Debug, new StubLogSink());
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);
            await installStateService.InitializeAsync(CancellationToken.None);

            var gameId = Guid.NewGuid().ToString("N");
            var platformDir = Path.Combine(temp.Path, "LaunchBox", "Games", "Microsoft Xbox 360");
            Directory.CreateDirectory(platformDir);
            var gameDir = Path.Combine(platformDir, "Naruto Rise of a Ninja");
            Directory.CreateDirectory(gameDir);
            var installedPath = Path.Combine(gameDir, "Naruto - Rise of a Ninja (USA).iso");
            await File.WriteAllTextAsync(installedPath, "iso");

            var xeniaExe = Path.Combine(temp.Path, "LaunchBox", "Emulators", "Xenia Canary", "xenia_canary.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(xeniaExe) ?? temp.Path);
            await File.WriteAllTextAsync(xeniaExe, "exe");

            var titleContentRoot = Path.Combine(temp.Path, "LaunchBox", "Emulators", "Xenia Canary", "localstate", "Content", "555307E5");
            var dlcDirectory = Path.Combine(titleContentRoot, "00000002");
            Directory.CreateDirectory(dlcDirectory);
            await File.WriteAllTextAsync(Path.Combine(dlcDirectory, "dlc.bin"), "dlc");

            await installStateService.UpsertStateAsync(new InstallState
            {
                LaunchBoxGameId = gameId,
                RommRomId = "rom-naruto",
                RommPlatformId = "30",
                PlatformContentId = "555307E5",
                WindowsInstallType = "Portable",
                InstallRootPath = gameDir,
                InstalledPath = installedPath,
                IsInstalled = true
            }, CancellationToken.None);

            var game = new Mock<IGame>();
            game.SetupGet(g => g.Id).Returns(gameId);
            game.SetupGet(g => g.Source).Returns("RomM");
            game.SetupGet(g => g.Platform).Returns("Microsoft Xbox 360");
            game.SetupGet(g => g.Title).Returns("Naruto: Rise of a Ninja");
            game.SetupGet(g => g.EmulatorId).Returns("xenia-canary");
            game.SetupProperty(g => g.ApplicationPath, Path.Combine("Games", "Microsoft Xbox 360", "Naruto Rise of a Ninja", "Naruto - Rise of a Ninja (USA).iso"));
            game.SetupProperty(g => g.Installed, true);
            game.SetupProperty(g => g.Status, "Installed");

            var platform = new Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Microsoft Xbox 360");
            platform.SetupGet(p => p.Folder).Returns(platformDir);

            var emulator = new Mock<IEmulator>();
            emulator.SetupGet(e => e.Id).Returns("xenia-canary");
            emulator.SetupGet(e => e.ApplicationPath).Returns(xeniaExe);

            var dataManager = new Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Microsoft Xbox 360")).Returns(platform.Object);
            dataManager.Setup(dm => dm.GetEmulatorById("xenia-canary")).Returns(emulator.Object);
            dataManager.Setup(dm => dm.BackgroundReloadSave(It.IsAny<Action>())).Callback<Action>(callback => callback?.Invoke());

            var service = new RomMUninstallService(logger, installStateService);
            var result = await service.UninstallAsync(game.Object, dataManager.Object, CancellationToken.None, new Progress<RomMbox.Models.Install.UninstallProgress>());

            result.Success.Should().BeTrue();
            Directory.Exists(titleContentRoot).Should().BeFalse();
        }

        private static void CreateZip(string zipPath, string entryPath, string contents)
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
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

        private sealed class PlatformInstallerRegistryScope : IDisposable
        {
            private readonly object? _prior;

            public PlatformInstallerRegistryScope(PlatformInstallerRegistry registry)
            {
                var field = typeof(PluginEntry).GetField("_platformInstallerRegistry", BindingFlags.Static | BindingFlags.NonPublic);
                _prior = field?.GetValue(null);
                field?.SetValue(null, registry);
            }

            public void Dispose()
            {
                var field = typeof(PluginEntry).GetField("_platformInstallerRegistry", BindingFlags.Static | BindingFlags.NonPublic);
                field?.SetValue(null, _prior);
            }
        }
    }
}

