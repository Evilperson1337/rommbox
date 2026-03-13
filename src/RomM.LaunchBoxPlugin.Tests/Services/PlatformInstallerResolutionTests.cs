using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomMbox.Services.Install.Pipeline.Steps;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerResolutionTests
    {
        [Fact]
        public void Resolves_PlatformKey_By_LaunchBox_Name_When_Id_Missing()
        {
            var registry = BuildRegistry(new StubInstaller("ps3", "PlayStation 3"));
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "",
                launchBoxPlatformName: "Sony Playstation 3",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps3");
        }

        [Fact]
        public void Resolves_PlayStation_To_Ps1_And_Does_Not_FuzzyMatch_To_Ps3()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = new IdentityStubInstaller(
                    platformKey: "ps1",
                    displayName: "PlayStation 1",
                    supportedIds: new[] { "22" },
                    supportedAliases: new[] { "PlayStation", "Sony Playstation", "PSX", "PS1" }),
                ["ps3"] = new StubInstaller("ps3", "PlayStation 3")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "22",
                platformDisplayName: "PlayStation",
                launchBoxPlatformName: "Sony Playstation",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps1");
        }

        [Fact]
        public void Resolves_Wii_To_Wii_And_Does_Not_Absorb_Into_WiiU()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wii"] = new IdentityStubInstaller(
                    platformKey: "wii",
                    displayName: "Nintendo Wii",
                    supportedIds: new[] { "26", "wii" },
                    supportedAliases: new[] { "wii", "nintendo wii" }),
                ["wiiu"] = new IdentityStubInstaller(
                    platformKey: "wiiu",
                    displayName: "Nintendo Wii U",
                    supportedIds: new[] { "27", "wiiu" },
                    supportedAliases: new[] { "wii u", "nintendo wii u", "wiiu" })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "26",
                platformDisplayName: "Wii",
                launchBoxPlatformName: "Nintendo Wii",
                registry: registry,
                logger: logger,
                fileExtension: ".rvz");

            resolved.Should().Be("wii");
        }

        [Fact]
        public void Resolves_PlayStation_To_Ps1_And_Does_Not_Absorb_Into_Psp()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = new IdentityStubInstaller(
                    platformKey: "ps1",
                    displayName: "PlayStation",
                    supportedIds: new[] { "22", "ps1" },
                    supportedAliases: new[] { "playstation", "sony playstation", "psx", "ps1" }),
                ["psp"] = new IdentityStubInstaller(
                    platformKey: "psp",
                    displayName: "PlayStation Portable",
                    supportedIds: new[] { "34", "psp" },
                    supportedAliases: new[] { "psp", "playstation portable", "sony playstation portable" })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "22",
                platformDisplayName: "PlayStation",
                launchBoxPlatformName: "Sony Playstation",
                registry: registry,
                logger: logger,
                fileExtension: ".chd");

            resolved.Should().Be("ps1");
        }

        [Fact]
        public void Resolves_By_RommPlatformId_When_NameEvidence_Is_Absent()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = new IdentityStubInstaller(
                    platformKey: "ps1",
                    displayName: "PlayStation",
                    supportedIds: new[] { "22" },
                    supportedAliases: Array.Empty<string>()),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "22",
                platformDisplayName: string.Empty,
                launchBoxPlatformName: string.Empty,
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps1");
        }

        [Fact]
        public void Resolves_By_Alias_When_Id_NotProvided()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = new IdentityStubInstaller(
                    platformKey: "ps1",
                    displayName: "PlayStation",
                    supportedIds: Array.Empty<string>(),
                    supportedAliases: new[] { "PSX", "Sony Playstation" })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "PSX",
                launchBoxPlatformName: string.Empty,
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps1");
        }

        [Fact]
        public void Resolves_Arcade_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["arcade"] = new IdentityStubInstaller(
                    platformKey: "arcade",
                    displayName: "Arcade",
                    supportedIds: Array.Empty<string>(),
                    supportedAliases: new[] { "arcade", "mame", "fbneo", "final burn neo" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "MAME",
                launchBoxPlatformName: "Arcade",
                registry: registry,
                logger: logger);

            resolved.Should().Be("arcade");
        }

        [Fact]
        public void Resolves_Switch_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["switch"] = new IdentityStubInstaller(
                    platformKey: "switch",
                    displayName: "Nintendo Switch",
                    supportedIds: Array.Empty<string>(),
                    supportedAliases: new[] { "switch", "nintendo switch", "nintendoswitch" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Switch",
                launchBoxPlatformName: "Nintendo Switch",
                registry: registry,
                logger: logger);

            resolved.Should().Be("switch");
        }

        [Fact]
        public void Resolves_Nintendo3DS_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["3ds"] = new IdentityStubInstaller(
                    platformKey: "3ds",
                    displayName: "Nintendo 3DS",
                    supportedIds: new[] { "3ds" },
                    supportedAliases: new[] { "3ds", "nintendo 3ds", "nintendo3ds" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "3DS",
                launchBoxPlatformName: "Nintendo 3DS",
                registry: registry,
                logger: logger);

            resolved.Should().Be("3ds");
        }

        [Fact]
        public void Resolves_Wii_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wii"] = new IdentityStubInstaller(
                    platformKey: "wii",
                    displayName: "Nintendo Wii",
                    supportedIds: new[] { "wii" },
                    supportedAliases: new[] { "wii", "nintendo wii", "nintendowii" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Wii",
                launchBoxPlatformName: "Nintendo Wii",
                registry: registry,
                logger: logger);

            resolved.Should().Be("wii");
        }

        [Fact]
        public void Resolves_WiiU_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wiiu"] = new IdentityStubInstaller(
                    platformKey: "wiiu",
                    displayName: "Nintendo Wii U",
                    supportedIds: new[] { "wiiu", "wii-u" },
                    supportedAliases: new[] { "wiiu", "wii u", "nintendo wii u", "nintendowiiu" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Wii U",
                launchBoxPlatformName: "Nintendo Wii U",
                registry: registry,
                logger: logger);

            resolved.Should().Be("wiiu");
        }

        [Fact]
        public void Uses_Extension_Filtering_To_Prevent_Wii_Being_Classified_As_WiiU()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wii"] = new IdentityStubInstaller(
                    platformKey: "wii",
                    displayName: "Nintendo Wii",
                    supportedIds: new[] { "wii" },
                    supportedAliases: new[] { "wii", "nintendo wii" }),
                ["wiiu"] = new IdentityStubInstaller(
                    platformKey: "wiiu",
                    displayName: "Nintendo Wii U",
                    supportedIds: new[] { "wiiu" },
                    supportedAliases: new[] { "wiiu", "wii u", "nintendo wii u" })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Nintendo Wii",
                launchBoxPlatformName: string.Empty,
                registry: registry,
                logger: logger,
                fileExtension: ".rvz");

            resolved.Should().Be("wii");
        }

        [Fact]
        public void Resolves_GameCube_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["gamecube"] = new IdentityStubInstaller(
                    platformKey: "gamecube",
                    displayName: "Nintendo GameCube",
                    supportedIds: new[] { "ngc", "gamecube" },
                    supportedAliases: new[] { "gamecube", "nintendo gamecube", "nintendogamecube" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "GameCube",
                launchBoxPlatformName: "Nintendo GameCube",
                registry: registry,
                logger: logger);

            resolved.Should().Be("gamecube");
        }

        [Fact]
        public void DoesNotResolve_GameCubeIdentifiers_To_WiiInstaller()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["wii"] = new IdentityStubInstaller(
                    platformKey: "wii",
                    displayName: "Nintendo Wii",
                    supportedIds: new[] { "wii" },
                    supportedAliases: new[] { "wii", "nintendo wii", "nintendowii" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Nintendo GameCube",
                launchBoxPlatformName: "GameCube",
                registry: registry,
                logger: logger);

            resolved.Should().NotBe("wii");
        }

        [Fact]
        public void DoesNotResolve_WiiIdentifiers_To_GameCubeInstaller()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["gamecube"] = new IdentityStubInstaller(
                    platformKey: "gamecube",
                    displayName: "Nintendo GameCube",
                    supportedIds: new[] { "ngc", "gamecube" },
                    supportedAliases: new[] { "gamecube", "nintendo gamecube", "nintendogamecube" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Nintendo Wii",
                launchBoxPlatformName: "Wii",
                registry: registry,
                logger: logger);

            resolved.Should().NotBe("gamecube");
        }

        [Fact]
        public void Resolves_Xbox_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["xbox"] = new IdentityStubInstaller(
                    platformKey: "xbox",
                    displayName: "Microsoft Xbox",
                    supportedIds: new[] { "xbox" },
                    supportedAliases: new[] { "xbox", "microsoft xbox", "original xbox" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Original Xbox",
                launchBoxPlatformName: "Microsoft Xbox",
                registry: registry,
                logger: logger);

            resolved.Should().Be("xbox");
        }

        [Fact]
        public void Resolves_Xbox360_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["xbox360"] = new IdentityStubInstaller(
                    platformKey: "xbox360",
                    displayName: "Microsoft Xbox 360",
                    supportedIds: new[] { "xbox360", "x360" },
                    supportedAliases: new[] { "xbox 360", "xbox360", "microsoft xbox 360", "x360" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "Xbox 360",
                launchBoxPlatformName: "Microsoft Xbox 360",
                registry: registry,
                logger: logger);

            resolved.Should().Be("xbox360");
        }

        [Fact]
        public void Resolves_Ps4_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps4"] = new IdentityStubInstaller(
                    platformKey: "ps4",
                    displayName: "PlayStation 4",
                    supportedIds: new[] { "ps4" },
                    supportedAliases: new[] { "ps4", "playstation 4", "sony playstation 4" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "PlayStation 4",
                launchBoxPlatformName: "Sony Playstation 4",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps4");
        }

        [Fact]
        public void Resolves_Ps2_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps2"] = new IdentityStubInstaller(
                    platformKey: "ps2",
                    displayName: "PlayStation 2",
                    supportedIds: new[] { "ps2" },
                    supportedAliases: new[] { "ps2", "playstation 2", "sony playstation 2" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "PlayStation 2",
                launchBoxPlatformName: "Sony Playstation 2",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps2");
        }

        [Fact]
        public void Resolves_Ps2_By_Name_Before_Incorrect_Foreign_RommId_Mapping()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["3ds"] = new IdentityStubInstaller(
                    platformKey: "3ds",
                    displayName: "Nintendo 3DS",
                    supportedIds: new[] { "17" },
                    supportedAliases: new[] { "3ds", "nintendo 3ds" }),
                ["ps2"] = new IdentityStubInstaller(
                    platformKey: "ps2",
                    displayName: "PlayStation 2",
                    supportedIds: new[] { "ps2" },
                    supportedAliases: new[] { "ps2", "playstation 2", "sony playstation 2" })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "17",
                platformDisplayName: "PlayStation 2",
                launchBoxPlatformName: "Sony Playstation 2",
                registry: registry,
                logger: logger);

            resolved.Should().Be("ps2");
        }

        [Theory]
        [InlineData("PlayStation 3", "Sony Playstation 3", "ps3")]
        [InlineData("PlayStation 4", "Sony Playstation 4", "ps4")]
        [InlineData("PlayStation Vita", "Sony Playstation Vita", "psvita")]
        [InlineData("PlayStation Portable", "Sony Playstation Portable", "psp")]
        public void Resolves_Exact_PlayStation_Family_Name_Without_Fuzzy_Matching_To_Ps1(
            string platformDisplayName,
            string launchBoxPlatformName,
            string expectedKey)
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["ps1"] = new IdentityStubInstaller(
                    platformKey: "ps1",
                    displayName: "PlayStation",
                    supportedIds: new[] { "22" },
                    supportedAliases: new[] { "playstation", "sony playstation", "psx", "ps1" }),
                [expectedKey] = new IdentityStubInstaller(
                    platformKey: expectedKey,
                    displayName: platformDisplayName,
                    supportedIds: Array.Empty<string>(),
                    supportedAliases: new[] { platformDisplayName, launchBoxPlatformName })
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: platformDisplayName,
                launchBoxPlatformName: launchBoxPlatformName,
                registry: registry,
                logger: logger);

            resolved.Should().Be(expectedKey);
        }

        [Fact]
        public void Resolves_Psp_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["psp"] = new IdentityStubInstaller(
                    platformKey: "psp",
                    displayName: "PlayStation Portable",
                    supportedIds: new[] { "psp" },
                    supportedAliases: new[] { "psp", "playstation portable", "sony playstation portable" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "PSP",
                launchBoxPlatformName: "Sony Playstation Portable",
                registry: registry,
                logger: logger);

            resolved.Should().Be("psp");
        }

        [Fact]
        public void Returns_Empty_When_Only_A_Foreign_Numeric_Id_Matches_A_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["n64"] = new IdentityStubInstaller(
                    platformKey: "n64",
                    displayName: "Nintendo 64",
                    supportedIds: new[] { "8", "n64" },
                    supportedAliases: new[] { "n64", "nintendo 64" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: "8",
                platformDisplayName: "Game Boy Advance",
                launchBoxPlatformName: "Nintendo Game Boy Advance",
                registry: registry,
                logger: logger,
                fileExtension: ".zip");

            resolved.Should().BeEmpty();
        }

        [Fact]
        public void Resolves_Vita_By_Alias_To_Dedicated_Installer()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["psvita"] = new IdentityStubInstaller(
                    platformKey: "psvita",
                    displayName: "PlayStation Vita",
                    supportedIds: new[] { "psvita", "vita" },
                    supportedAliases: new[] { "ps vita", "vita", "playstation vita", "sony playstation vita" }),
                ["general"] = new StubInstaller("general", "General Platform")
            });
            var logger = TestLogger.Create();

            var resolved = InstallContentStep.ResolveInstallerKey(
                platformKey: string.Empty,
                platformDisplayName: "PS Vita",
                launchBoxPlatformName: "Sony Playstation Vita",
                registry: registry,
                logger: logger);

            resolved.Should().Be("psvita");
        }

        [Fact]
        public void ShouldWarnOnResolvedKeyDifference_ReturnsFalse_For_UnmappedForeignIdentifier()
        {
            var registry = BuildRegistry(new StubInstaller("arcade", "Arcade"));

            var shouldWarn = InstallContentStep.ShouldWarnOnResolvedKeyDifference(
                providedKey: "3",
                resolvedKey: "arcade",
                registry: registry);

            shouldWarn.Should().BeFalse();
        }

        [Fact]
        public void ShouldWarnOnResolvedKeyDifference_ReturnsTrue_When_ProvidedKey_Is_AlreadyARegisteredInstaller()
        {
            var registry = new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                ["arcade"] = new StubInstaller("arcade", "Arcade"),
                ["general"] = new StubInstaller("general", "General Platform")
            });

            var shouldWarn = InstallContentStep.ShouldWarnOnResolvedKeyDifference(
                providedKey: "general",
                resolvedKey: "arcade",
                registry: registry);

            shouldWarn.Should().BeTrue();
        }

        private static PlatformInstallerRegistry BuildRegistry(IPlatformInstaller installer)
        {
            return new PlatformInstallerRegistry(new Dictionary<string, IPlatformInstaller>
            {
                [installer.PlatformKey] = installer
            });
        }

        private sealed class StubInstaller : IPlatformInstaller
        {
            public StubInstaller(string platformKey, string displayName)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<InstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new InstallResult { Success = true });
            }

            public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new UninstallResult { Success = true });
            }

            public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new VerifyResult { IsValid = true });
            }
        }

        private sealed class IdentityStubInstaller : IPlatformInstaller, IPlatformInstallerIdentityMetadata
        {
            public IdentityStubInstaller(string platformKey, string displayName, IReadOnlyCollection<string> supportedIds, IReadOnlyCollection<string> supportedAliases)
            {
                PlatformKey = platformKey;
                DisplayName = displayName;
                SupportedPlatformIds = supportedIds;
                SupportedPlatformAliases = supportedAliases;
            }

            public string PlatformKey { get; }
            public string DisplayName { get; }
            public IReadOnlyCollection<string>? SupportedPlatformIds { get; }
            public IReadOnlyCollection<string>? SupportedPlatformAliases { get; }

            public Task<DetectionResult> DetectAsync(RomM.Platforms.Abstractions.Models.PlatformContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new DetectionResult());
            }

            public Task<InstallResult> InstallAsync(RomM.Platforms.Abstractions.Models.Install.InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new InstallResult { Success = true });
            }

            public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
            {
                return Task.FromResult(new UninstallResult { Success = true });
            }

            public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
            {
                return Task.FromResult(new VerifyResult { IsValid = true });
            }
        }
    }
}
