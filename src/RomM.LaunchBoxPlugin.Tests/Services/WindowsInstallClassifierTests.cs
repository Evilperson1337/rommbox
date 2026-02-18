using System.Text;
using FluentAssertions;
using Moq;
using RomMbox.Services;
using RomMbox.Services.Install;
using RomMbox.Services.Settings;
using RomMbox.Tests.Utilities;
using Xunit;

namespace RomMbox.Tests.Services
{
    public sealed class WindowsInstallClassifierTests
    {
        [Fact]
        public void IsInnoInstaller_ShouldReturnFalse_WhenPathMissing()
        {
            var archiveService = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));
            var classifier = new WindowsInstallClassifier(archiveService, TestLogger.Create());

            classifier.IsInnoInstaller(null).Should().BeFalse();
            classifier.IsInnoInstaller(string.Empty).Should().BeFalse();
        }

        [Fact]
        public void IsInnoInstallerExe_ShouldReturnFalse_WhenFileMissing()
        {
            var archiveService = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));
            var classifier = new WindowsInstallClassifier(archiveService, TestLogger.Create());

            classifier.IsInnoInstallerExe("Z:\\missing.exe").Should().BeFalse();
        }

        [Fact]
        public void IsInnoInstallerExe_ShouldDetectSignature()
        {
            using var temp = new TempDirectory();
            var exePath = temp.CreateFile("setup.exe", Encoding.ASCII.GetBytes("Hello Inno Setup World"));

            var archiveService = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));
            var classifier = new WindowsInstallClassifier(archiveService, TestLogger.Create());

            classifier.IsInnoInstallerExe(exePath).Should().BeTrue();
        }

        [Fact]
        public void IsInnoInstaller_ShouldDetectInnoSetupExeInFolder()
        {
            using var temp = new TempDirectory();
            temp.CreateFile("setup.exe", Encoding.ASCII.GetBytes("Inno Setup"));

            var archiveService = new ArchiveService(TestLogger.Create(), new SettingsManager(TestLogger.Create()));
            var classifier = new WindowsInstallClassifier(archiveService, TestLogger.Create());

            classifier.IsInnoInstaller(temp.Path).Should().BeTrue();
        }
    }
}
