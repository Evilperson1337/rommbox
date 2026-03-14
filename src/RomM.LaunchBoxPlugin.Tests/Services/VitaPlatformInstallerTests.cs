using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Vita;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class VitaPlatformInstallerTests
    {
        [Fact]
        public async Task InstallAsync_BaseVpk_CreatesTokenApplicationPath()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game [PCSE00120].vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorName = "Vita3K",
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith(".vita3k.json");
            File.Exists(result.ExecutablePath).Should().BeTrue();
            result.Arguments.Should().NotBeNullOrEmpty();
            result.Arguments![0].Should().Contain("--title-id");
            result.Arguments[0].Should().Contain("PCSE00120");
        }

        [Fact]
        public async Task InstallAsync_ArchiveWithExtractedVpk_UsesExtractedCandidate()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            Directory.CreateDirectory(extracted);
            await File.WriteAllTextAsync(archive, "zip");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game [PCSE00120].vpk"), "pkg");
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive,
                ExtractedPath = extracted,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().EndWith(".vita3k.json");
            result.PlatformContentId.Should().Be("PCSE00120");
        }

        [Fact]
        public async Task InstallAsync_MalformedArchiveWithoutExtractedContent_Fails()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var archive = Path.Combine(temp.Path, "download", "Game.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? temp.Path);
            await File.WriteAllTextAsync(archive, "zip");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = archive
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("No Vita base game candidate detected");
        }

        [Fact]
        public async Task InstallAsync_MissingTitleId_FailsSafely()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game.vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("title id");
        }

        [Fact]
        public async Task Detect_Verify_Uninstall_Lifecycle_Works()
        {
            using var temp = new TempDirectory();
            var platformDir = Path.Combine(temp.Path, "Games", "Sony Playstation Vita", "Game");
            Directory.CreateDirectory(platformDir);
            var tokenPath = Path.Combine(platformDir, "PCSE00120.vita3k.json");
            await File.WriteAllTextAsync(tokenPath, "{\"titleId\":\"PCSE00120\",\"cachedArtifacts\":[]}");

            var installer = new VitaPlatformInstaller();
            var detect = await installer.DetectAsync(new PlatformContext
            {
                PlatformKey = "psvita",
                IsInstalled = true,
                InstalledPath = tokenPath
            }, CancellationToken.None);
            detect.IsInstalled.Should().BeTrue();

            var verifyBefore = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = tokenPath
            }, CancellationToken.None);
            verifyBefore.IsValid.Should().BeTrue();

            var uninstall = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Game",
                InstalledPath = tokenPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            uninstall.Success.Should().BeTrue();
            File.Exists(tokenPath).Should().BeFalse();

            var verifyAfter = await installer.VerifyAsync(new RomM.Platforms.Abstractions.Models.Verify.VerifyContext
            {
                InstalledPath = tokenPath
            }, CancellationToken.None);
            verifyAfter.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task InstallAsync_WithUpdateAndDlcFlags_ProducesToken()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var extracted = Path.Combine(temp.Path, "staging");
            Directory.CreateDirectory(extracted);
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120");

            await File.WriteAllTextAsync(Path.Combine(extracted, "Game [PCSE00120].vpk"), "base");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game Update [PCSE00120] v1.02.vpk"), "upd");
            await File.WriteAllTextAsync(Path.Combine(extracted, "Game DLC [PCSE00120].vpk"), "dlc");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ExtractedPath = extracted,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath,
                    VitaInstallUpdatesAutomatically = true,
                    VitaInstallDlcAutomatically = true
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            File.Exists(result.ExecutablePath).Should().BeTrue();
            result.ExecutablePath.Should().NotBeNullOrWhiteSpace();
            var launchPath = result.ExecutablePath!;
            var tokenText = await File.ReadAllTextAsync(launchPath);
            tokenText.Should().Contain("\"updatesImported\": 1");
            tokenText.Should().Contain("\"dlcImported\": 1");
            tokenText.Should().Contain("\"launchMode\": \"title-id\"");
            tokenText.Should().Contain("\"launchTarget\": \"PCSE00120\"");
        }

        [Fact]
        public async Task InstallAsync_ParamSfoTitleIdLayout_UsesGameNameForCanonicalFolderAndTitleIdLaunch()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var extracted = Path.Combine(temp.Path, "staging", "PCSE00999", "sce_sys");
            Directory.CreateDirectory(extracted);

            var paramPath = Path.Combine(extracted, "param.sfo");
            var paramText = "TITLE_ID\0PCSE00999\0TITLE\0TITLE\0APP_VER\01.00\0";
            await File.WriteAllTextAsync(paramPath, paramText);
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00999");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Gravity Rush",
                InstallDirectory = root,
                ExtractedPath = Path.Combine(temp.Path, "staging"),
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.ExecutablePath.Should().Contain("Gravity Rush");
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Be("--title-id PCSE00999");
            result.PlatformContentId.Should().Be("PCSE00999");
        }

        [Fact]
        public async Task InstallAsync_CustomLaunchArguments_UseResolvedTitleIdPlaceholders()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game [PCSE00120].vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath,
                    LaunchArguments = "--title-id {titleId} --focus {launchTarget}"
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Arguments.Should().ContainSingle();
            result.Arguments[0].Should().Be("--title-id PCSE00120 --focus PCSE00120");
        }

        [Fact]
        public async Task InstallAsync_ConsolidatedMode_Creates_Vita3k_Link_To_Physical_Content()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game [PCSE00120].vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath,
                    VitaConsolidateGameInstalls = true
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            var physicalPath = Path.Combine(root, "Game [PCSE00120]", "PCSE00120");
            var linkPath = Path.Combine(env.PrefPath, "ux0", "app", "PCSE00120");
            Directory.Exists(physicalPath).Should().BeTrue();
            Directory.Exists(linkPath).Should().BeTrue();
            new DirectoryInfo(linkPath).Attributes.Should().HaveFlag(FileAttributes.ReparsePoint);
        }

        [Fact]
        public async Task InstallAsync_ConsolidatedMode_Repairs_Missing_Link_When_Physical_Content_Exists()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            var source = Path.Combine(temp.Path, "download", "Game [PCSE00120].vpk");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? temp.Path);
            await File.WriteAllTextAsync(source, "pkg");
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120", createVisibleAppDirectory: false);
            var physicalPath = Path.Combine(root, "Game [PCSE00120]", "PCSE00120");
            Directory.CreateDirectory(physicalPath);
            await File.WriteAllTextAsync(Path.Combine(physicalPath, "eboot.bin"), "existing");

            var installer = new VitaPlatformInstaller();
            var result = await installer.InstallAsync(new InstallContext
            {
                GameName = "Game",
                InstallDirectory = root,
                ArchivePath = source,
                Settings = new PlatformInstallSettings
                {
                    Vita3kExecutablePath = env.ExecutablePath,
                    VitaConsolidateGameInstalls = true
                },
                RomSettings = new RomInstallSettings
                {
                    EmulatorExecutablePath = env.ExecutablePath
                }
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            var linkPath = Path.Combine(env.PrefPath, "ux0", "app", "PCSE00120");
            Directory.Exists(linkPath).Should().BeTrue();
            new DirectoryInfo(linkPath).Attributes.Should().HaveFlag(FileAttributes.ReparsePoint);
        }

        [Fact]
        public async Task UninstallAsync_ConsolidatedMode_Removes_Title_Link_And_Physical_Content_Only()
        {
            using var temp = new TempDirectory();
            var root = Path.Combine(temp.Path, "Games", "Sony Playstation Vita");
            Directory.CreateDirectory(root);
            var gameRoot = Path.Combine(root, "Game [PCSE00120]");
            Directory.CreateDirectory(gameRoot);
            var env = await CreateVita3kEnvironmentAsync(temp.Path, "PCSE00120", createVisibleAppDirectory: false);
            var physicalPath = Path.Combine(gameRoot, "PCSE00120");
            Directory.CreateDirectory(physicalPath);
            await File.WriteAllTextAsync(Path.Combine(physicalPath, "eboot.bin"), "existing");
            var visibleOtherTitle = Path.Combine(env.PrefPath, "ux0", "app", "PCSE99999");
            Directory.CreateDirectory(visibleOtherTitle);
            await File.WriteAllTextAsync(Path.Combine(visibleOtherTitle, "eboot.bin"), "other");
            var linkPath = Path.Combine(env.PrefPath, "ux0", "app", "PCSE00120");
            await CreateDirectoryLinkAsync(linkPath, physicalPath);

            var tokenPath = Path.Combine(gameRoot, "PCSE00120.vita3k.json");
            await File.WriteAllTextAsync(tokenPath, "{\n  \"titleId\": \"PCSE00120\",\n  \"vitaAppPath\": \"" + EscapeJson(linkPath) + "\",\n  \"physicalInstallPath\": \"" + EscapeJson(physicalPath) + "\",\n  \"isConsolidatedInstall\": true,\n  \"cachedArtifacts\": []\n}");

            var installer = new VitaPlatformInstaller();
            var result = await installer.UninstallAsync(new UninstallContext
            {
                GameName = "Game",
                InstalledPath = tokenPath
            }, new Progress<InstallProgress>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            Directory.Exists(physicalPath).Should().BeFalse();
            Directory.Exists(linkPath).Should().BeFalse();
            Directory.Exists(Path.Combine(env.PrefPath, "ux0", "app")).Should().BeTrue();
            Directory.Exists(visibleOtherTitle).Should().BeTrue();
        }

        private static async Task<(string ExecutablePath, string PrefPath)> CreateVita3kEnvironmentAsync(string tempRoot, string titleId, bool createVisibleAppDirectory = true)
        {
            var exeDir = Path.Combine(tempRoot, "Vita3K");
            Directory.CreateDirectory(exeDir);
            var executablePath = Path.Combine(exeDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Vita3K.exe" : "Vita3K");
            File.Copy(Environment.SystemDirectory.Length > 0 ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : executablePath, executablePath, overwrite: true);

            var prefPath = Path.Combine(tempRoot, "pref");
            Directory.CreateDirectory(Path.Combine(prefPath, "ux0", "app"));
            await File.WriteAllTextAsync(Path.Combine(exeDir, "config.yml"), $"pref-path: '{prefPath.Replace("\\", "/")}'");

            if (createVisibleAppDirectory)
            {
                var appPath = Path.Combine(prefPath, "ux0", "app", titleId);
                Directory.CreateDirectory(appPath);
                await File.WriteAllTextAsync(Path.Combine(appPath, "eboot.bin"), "installed");
            }

            return (executablePath, prefPath);
        }

        private static async Task CreateDirectoryLinkAsync(string linkPath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath) ?? string.Empty);
            var process = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var running = System.Diagnostics.Process.Start(process);
            running.Should().NotBeNull();
            running!.WaitForExit(10000);
            running.ExitCode.Should().Be(0, await running.StandardError.ReadToEndAsync());
        }

        private static string EscapeJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

