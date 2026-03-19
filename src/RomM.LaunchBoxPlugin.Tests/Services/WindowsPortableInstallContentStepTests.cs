using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Models.Romm;
using RomMbox.Services;
using RomMbox.Services.Install;
using RomMbox.Services.Install.Pipeline;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.Logging;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Unbroken.LaunchBox.Plugins.Data;

using PipelineInstallContext = RomMbox.Services.Install.Pipeline.InstallContext;
using PlatformInstallResult = RomM.Platforms.Abstractions.Models.Install.InstallResult;

namespace RomMbox.Tests.Services
{
    [Collection("SettingsTests")]
    public sealed class WindowsPortableInstallContentStepTests
    {
        [Fact]
        public async Task ExecuteAsync_WindowsPortable_CommitsOnlyPortableGameRoot_AndCleansOperationDirectories()
        {
            using var temp = new TempDirectory();
            using var settingsScope = new TestEnvironmentScope("ROMMBOX_TEST_SETTINGS", temp.Path);

            var logger = TestLogger.Create();
            var settingsStore = new TestSettingsStore(temp.Path);
            settingsStore.WriteSettings(TestSettingsStore.CreateSettings(new PlatformMapping()));
            var settingsManager = new SettingsManager(logger);
            var installStateService = new InstallStateService(logger, settingsManager);

            var platform = new Moq.Mock<IPlatform>();
            platform.SetupGet(p => p.Name).Returns("Windows");
            var dataManager = new Moq.Mock<IDataManager>();
            dataManager.Setup(dm => dm.GetPlatformByName("Windows")).Returns(platform.Object);

            var game = new Moq.Mock<IGame>();
            game.SetupGet(g => g.Platform).Returns("Windows");
            game.SetupGet(g => g.Title).Returns("Beard Blade");

            var request = new InstallRequest(game.Object, dataManager.Object);
            var context = new PipelineInstallContext(request, logger, settingsManager, installStateService)
            {
                RommDetails = new RommRom
                {
                    Id = "9955",
                    PlatformId = "28",
                    PlatformDisplayName = "Windows"
                },
                PlatformMapping = new PlatformMapping
                {
                    InstallScenario = InstallScenario.Enhanced
                },
                InstallStateSnapshot = new InstallStateSnapshot(),
                InstallDirectory = Path.Combine(temp.Path, "Games", "Windows"),
                TempRoot = Path.Combine(temp.Path, "Games", "Windows", ".staging", "portable-op"),
                ArchivePath = Path.Combine(temp.Path, "Games", "Windows", ".staging", "portable-op", "download", "downloads", "Beard Blade (Portable).rar"),
                ExtractedPath = Path.Combine(temp.Path, "Games", "Windows", ".staging", "portable-op", "download", "extracted", "Beard Blade (Portable)")
            };

            var stagedPortableRoot = Path.Combine(context.TempRoot, "download", "Beard Blade");
            var stagedExecutable = Path.Combine(stagedPortableRoot, "Beard Blade.exe");
            Directory.CreateDirectory(Path.Combine(context.TempRoot, "download", "downloads"));
            Directory.CreateDirectory(Path.Combine(context.TempRoot, "download", "extracted"));
            Directory.CreateDirectory(stagedPortableRoot);
            await File.WriteAllTextAsync(stagedExecutable, "exe");

            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["windows"] = new StubWindowsPortableInstaller(stagedExecutable, stagedPortableRoot)
            });

            var step = new InstallContentStep(registry, new PlatformLoggerAdapter(logger), new ArchiveService(logger, settingsManager));
            var result = await step.ExecuteAsync(context, new Progress<InstallProgressEvent>(), CancellationToken.None);

            var finalRoot = Path.Combine(context.InstallDirectory, "Beard Blade");
            var finalExecutable = Path.Combine(finalRoot, "Beard Blade.exe");

            result.Success.Should().BeTrue();
            context.InstalledExecutablePath.Should().Be(finalExecutable);
            context.InstallStateSnapshot.InstallRootPath.Should().Be(finalRoot);
            Directory.Exists(finalRoot).Should().BeTrue();
            File.Exists(finalExecutable).Should().BeTrue();
            Directory.Exists(Path.Combine(finalRoot, "Beard Blade")).Should().BeFalse("portable commits should not create a duplicate nested game folder");
            Directory.Exists(Path.Combine(finalRoot, "downloads")).Should().BeFalse("download staging directories should not be deployed into the final game layout");
            Directory.Exists(Path.Combine(finalRoot, "extracted")).Should().BeFalse("extraction staging directories should not be deployed into the final game layout");
            Directory.Exists(context.TempRoot).Should().BeFalse("the operation staging root should be removed after a successful portable commit");
        }

        private sealed class StubWindowsPortableInstaller : IPlatformInstaller, IPlatformInstallerIdentityMetadata
        {
            private readonly string _executablePath;
            private readonly string _installRootPath;

            public StubWindowsPortableInstaller(string executablePath, string installRootPath)
            {
                _executablePath = executablePath;
                _installRootPath = installRootPath;
            }

            public string PlatformKey => "windows";
            public string DisplayName => "Windows";
            public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "28", "windows" };
            public IReadOnlyCollection<string>? SupportedPlatformAliases => new[] { "windows" };

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
                => Task.FromResult(new DetectionResult());

            public Task<PlatformInstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new PlatformInstallResult
                {
                    Success = true,
                    ExecutablePath = _executablePath,
                    InstallRootPath = _installRootPath,
                    InstallType = RomM.Platforms.Abstractions.Models.Install.InstallType.Portable
                });
            }

            public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
                => Task.FromResult(new UninstallResult { Success = true });

            public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
                => Task.FromResult(new VerifyResult { IsValid = true });
        }
    }
}
