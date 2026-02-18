using System.IO;
using System.Runtime.Serialization.Json;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Paths;
using RomMbox.Services.Settings;

namespace RomMbox.Tests.Utilities
{
    internal sealed class TestSettingsStore
    {
        private readonly string _settingsPath;

        public TestSettingsStore(string rootPath)
        {
            _settingsPath = Path.Combine(rootPath, "RomM", "LaunchBoxPlugin", "settings.json");
        }

        public void WriteSettings(PluginSettings settings)
        {
            settings.ApplyDefaults();
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(_settingsPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            serializer.WriteObject(stream, settings);
        }

        public static PluginSettings CreateSettings(params PlatformMapping[] mappings)
        {
            var settings = new PluginSettings
            {
                PlatformMappings = mappings ?? System.Array.Empty<PlatformMapping>()
            };
            settings.ApplyDefaults();
            return settings;
        }
    }
}
