using System;
using System.IO;
using System.Reflection;

namespace RomMbox.Services.Paths
{
    /// <summary>
    /// Resolves plugin-related filesystem paths.
    /// </summary>
    internal static class PluginPaths
    {
        /// <summary>
        /// Gets the plugin data directory, honoring the test override environment variable.
        /// </summary>
        /// <returns>The data directory path.</returns>
        public static string GetPluginDataDirectory()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("ROMMBOX_TEST_SETTINGS");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return Path.Combine(overrideRoot, "RomM", "LaunchBoxPlugin");
            }

            var pluginRoot = GetPluginRootDirectory();
            return Path.Combine(pluginRoot, "system");
        }

        /// <summary>
        /// Gets the plugin root directory based on the executing assembly.
        /// </summary>
        /// <returns>The plugin root path.</returns>
        public static string GetPluginRootDirectory()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    var folder = Path.GetDirectoryName(location);
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        return folder;
                    }
                }
            }
            catch
            {
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var fullBase = Path.GetFullPath(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (Directory.Exists(Path.Combine(fullBase, "system")))
            {
                return fullBase;
            }

            const string pluginFolderName = "RomMbox";
            var pluginCandidates = new[]
            {
                Path.Combine(fullBase, pluginFolderName),
                Path.Combine(fullBase, "Plugins", pluginFolderName)
            };

            foreach (var candidate in pluginCandidates)
            {
                if (Directory.Exists(Path.Combine(candidate, "system")))
                {
                    return candidate;
                }
            }

            return fullBase;
        }

        /// <summary>
        /// Resolves the LaunchBox root directory from the current base directory.
        /// </summary>
        /// <returns>The LaunchBox root path, or empty if unavailable.</returns>
        public static string GetLaunchBoxRootDirectory()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return string.Empty;
            }

            var fullBase = Path.GetFullPath(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var candidate = fullBase;
            if (string.Equals(Path.GetFileName(candidate), "Core", StringComparison.OrdinalIgnoreCase))
            {
                candidate = Directory.GetParent(candidate)?.FullName ?? candidate;
            }

            for (var i = 0; i < 6; i++)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    break;
                }

                if (File.Exists(Path.Combine(candidate, "LaunchBox.exe")))
                {
                    return candidate;
                }

                if (Directory.Exists(Path.Combine(candidate, "Plugins"))
                    && Directory.Exists(Path.Combine(candidate, "Games")))
                {
                    return candidate;
                }

                if (string.Equals(Path.GetFileName(candidate), "Core", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(candidate)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parent)
                        && (File.Exists(Path.Combine(parent, "LaunchBox.exe"))
                            || (Directory.Exists(Path.Combine(parent, "Plugins"))
                                && Directory.Exists(Path.Combine(parent, "Games")))))
                    {
                        return parent;
                    }
                }

                candidate = Directory.GetParent(candidate)?.FullName;
            }

            return fullBase;
        }

        /// <summary>
        /// Gets the path to the settings file for the plugin.
        /// </summary>
        /// <returns>The settings file path.</returns>
        public static string GetSettingsPath()
        {
            return Path.Combine(GetPluginDataDirectory(), "settings.json");
        }

        /// <summary>
        /// Gets the path to the plugin log file.
        /// </summary>
        /// <returns>The log file path.</returns>
        public static string GetLogPath()
        {
            return Path.Combine(GetPluginDataDirectory(), "RomM.Plugin.log");
        }
    }
}
