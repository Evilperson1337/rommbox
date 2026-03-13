using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
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
                installer.PlatformKey.Should().Be("windows");
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
