using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RomMbox.Services.Logging;
using RomMbox.Services.Settings;

namespace RomMbox.Tests.Settings
{
    [TestClass]
    public class SettingsManagerTests
    {
        private sealed class NullSink : ILogSink
        {
            public void Write(LogMessage message)
            {
            }
        }

        private static string GetTempPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "RomMbox.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [TestMethod]
        public void Load_Creates_Defaults_When_File_Missing()
        {
            var temp = GetTempPath();
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = new SettingsManager(logger);

            try
            {
                OverrideSettingsPath(temp);
                var settings = manager.Load();
                Assert.IsNotNull(settings);
                Assert.AreEqual("Debug", settings.LogLevelName);
                Assert.IsTrue(File.Exists(RomMbox.Services.Paths.PluginPaths.GetSettingsPath()));
            }
            finally
            {
                RestoreSettingsPath();
            }
        }

        [TestMethod]
        public void Load_Uses_Defaults_When_File_Corrupted()
        {
            var temp = GetTempPath();
            var logger = new LoggingService(LogLevel.Debug, new NullSink());
            var manager = new SettingsManager(logger);

            try
            {
                OverrideSettingsPath(temp);
                File.WriteAllText(RomMbox.Services.Paths.PluginPaths.GetSettingsPath(), "not-json");
                var settings = manager.Load();
                Assert.AreEqual("Debug", settings.LogLevelName);
            }
            finally
            {
                RestoreSettingsPath();
            }
        }

        private static void OverrideSettingsPath(string root)
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", root);
        }

        private static void RestoreSettingsPath()
        {
            Environment.SetEnvironmentVariable("ROMMBOX_TEST_SETTINGS", null);
        }
    }
}
