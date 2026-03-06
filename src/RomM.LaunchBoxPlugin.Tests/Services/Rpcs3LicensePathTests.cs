using System;
using System.IO;
using FluentAssertions;
using RomM.Platforms.PS3;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class Rpcs3LicensePathTests
    {
        [Fact]
        public void Resolves_License_Directory_From_Executable()
        {
            using var temp = new TempDirectory();
            var root = temp.Path;
            var exePath = Path.Combine(root, "Emulators", "RPCS3", "rpcs3.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath) ?? root);
            File.WriteAllText(exePath, "");

            using var launchBoxScope = new LaunchBoxRootScope(root);
            var licensePath = InvokeResolveDefaultLicenseDirectory(exePath);

            var exeDir = Path.GetDirectoryName(exePath) ?? root;
            licensePath.Should().Be(Path.Combine(exeDir, "dev_hdd0", "home", "00000001", "exdata"));
        }

        private static string InvokeResolveDefaultLicenseDirectory(string exePath)
        {
            var method = typeof(Ps3PlatformInstaller).GetMethod("ResolveDefaultLicenseDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("ResolveDefaultLicenseDirectory not found.");
            }

            var result = method.Invoke(null, new object[] { exePath });
            return result as string ?? string.Empty;
        }

        private sealed class LaunchBoxRootScope : IDisposable
        {
            private readonly string? _prior;

            public LaunchBoxRootScope(string launchBoxRoot)
            {
                _prior = Environment.GetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT");
                Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", launchBoxRoot);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("ROMMBOX_TEST_LAUNCHBOX_ROOT", _prior);
            }
        }
    }
}
