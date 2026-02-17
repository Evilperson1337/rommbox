using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Settings;
using RomMbox.Services.Logging;

namespace RomMbox.Tests.Services
{
    [TestClass]
    public sealed class InstallScenarioTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        [TestMethod]
        public void PluginSettings_ApplyDefaults_NormalizesScenarioFields()
        {
            var settings = new PluginSettings
            {
                PlatformMappings = new[]
                {
                    new PlatformMapping
                    {
                        InstallScenario = (InstallScenario)99,
                        TargetImportFile = null,
                        InstallerSilentArgs = null,
                        InstallerMode = (InstallerMode)99,
                        MusicRootPath = null,
                        BonusRootPath = null,
                        PreReqsRootPath = null
                    }
                }
            };

            settings.ApplyDefaults();

            Assert.AreEqual(InstallScenario.Basic, settings.PlatformMappings[0].InstallScenario);
            Assert.IsNotNull(settings.PlatformMappings[0].TargetImportFile);
            Assert.IsNotNull(settings.PlatformMappings[0].InstallerSilentArgs);
            Assert.AreEqual(InstallerMode.Manual, settings.PlatformMappings[0].InstallerMode);
            Assert.IsNotNull(settings.PlatformMappings[0].MusicRootPath);
            Assert.IsNotNull(settings.PlatformMappings[0].BonusRootPath);
            Assert.IsNotNull(settings.PlatformMappings[0].PreReqsRootPath);
            Assert.IsTrue(settings.GetPromptForWindowsInstallDirectory());
        }
    }
}
