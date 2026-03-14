using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using RomM.Platforms.Arcade;
using RomM.Platforms.Windows;
using RomMbox.Services.PlatformInstallers;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class PlatformInstallerLoaderTests
    {
        [Fact]
        public void Load_ReturnsEmpty_WhenPlatformFolderMissing()
        {
            var root = ResolveTestRoot();
            var systemRoot = Path.Combine(root, "system");
            if (Directory.Exists(systemRoot))
            {
                Directory.Delete(systemRoot, true);
            }

            var logger = TestLogger.Create();
            var loader = new PlatformInstallerLoader(logger);

            var registry = loader.Load();

            registry.GetAll().Should().BeEmpty();
        }

        [Fact]
        public void Load_RegistersWindowsInstaller_WhenAssemblyPresent()
        {
            var root = ResolveTestRoot();
            var platformsRoot = Path.Combine(root, "system", "platforms");
            Directory.CreateDirectory(platformsRoot);

            var sourcePath = typeof(WindowsPlatformInstaller).Assembly.Location;
            var targetPath = Path.Combine(platformsRoot, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, true);

            try
            {
                var logger = TestLogger.Create();
                var loader = new PlatformInstallerLoader(logger);

                var registry = loader.Load();

                registry.TryGetInstaller("windows", out var installer).Should().BeTrue();
                installer.Should().NotBeNull();
                installer!.PlatformKey.Should().Be("windows");
            }
            finally
            {
                var systemRoot = Path.Combine(root, "system");
                if (Directory.Exists(systemRoot))
                {
                    Directory.Delete(systemRoot, true);
                }
            }
        }

        [Fact]
        public void Load_Resolves_SharedPlatformDependencies_FromPlatformsFolder()
        {
            var root = ResolveTestRoot();
            var platformsRoot = Path.Combine(root, "system", "platforms");
            Directory.CreateDirectory(platformsRoot);

            var dependencyAssemblies = new[]
            {
                typeof(RomM.Platforms.Abstractions.IPlatformInstaller).Assembly,
                typeof(RomM.Platforms.RomBase.RomPlatformInstallerBase).Assembly,
                typeof(ArcadePlatformInstaller).Assembly
            };

            foreach (var assembly in dependencyAssemblies)
            {
                var sourcePath = assembly.Location;
                var targetPath = Path.Combine(platformsRoot, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, targetPath, true);
            }

            try
            {
                var logger = TestLogger.Create();
                var loader = new PlatformInstallerLoader(logger);

                var registry = loader.Load();

                registry.TryGetInstaller("arcade", out var installer).Should().BeTrue();
                installer.Should().NotBeNull();
                installer!.PlatformKey.Should().Be("arcade");
                installer.GetType().Assembly.GetName().Name.Should().Be("RomM.Platforms.Arcade");
            }
            finally
            {
                var systemRoot = Path.Combine(root, "system");
                if (Directory.Exists(systemRoot))
                {
                    Directory.Delete(systemRoot, true);
                }
            }
        }

        private static string ResolveTestRoot()
        {
            var location = typeof(PlatformInstallerLoader).Assembly.Location;
            return Path.GetDirectoryName(location) ?? AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
        }
    }
}
