using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;

namespace RomM.Platforms.Vita
{
    internal static class Vita3kEnvironmentResolver
    {
        private static readonly Regex PrefPathRegex = new(@"^[ \t]*(?:pref[-_]path|prefPath)[ \t]*:[ \t]*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static Vita3kEnvironmentResolution Resolve(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings, IPlatformLogger? logger)
        {
            var executablePath = ResolveExecutablePath(installSettings, romSettings, logger);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Vita3kEnvironmentResolution.Invalid("Vita3K executable could not be resolved from platform settings or LaunchBox emulator mapping.");
            }

            var configCandidates = GetConfigCandidates(executablePath).ToList();
            foreach (var candidate in configCandidates)
            {
                logger?.Write(PlatformLogLevel.Info, $"Vita3K config candidate: {candidate}");
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var prefPath = TryReadPrefPath(candidate, logger);
                if (string.IsNullOrWhiteSpace(prefPath))
                {
                    continue;
                }

                var normalizedPrefPath = NormalizePath(prefPath, Path.GetDirectoryName(candidate) ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalizedPrefPath))
                {
                    continue;
                }

                var ux0AppRoot = Path.Combine(normalizedPrefPath, "ux0", "app");
                logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K executable path: {executablePath}");
                logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K config path: {candidate}");
                logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K pref path: {normalizedPrefPath}");
                logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K ux0 app root: {ux0AppRoot}");
                return Vita3kEnvironmentResolution.Valid(executablePath, candidate, normalizedPrefPath, ux0AppRoot);
            }

            logger?.Write(PlatformLogLevel.Warning, $"Unable to resolve Vita3K pref path from config. Searched: {string.Join(", ", configCandidates)}");
            return Vita3kEnvironmentResolution.Invalid("Vita3K pref path could not be resolved from a Vita3K configuration file.");
        }

        private static string ResolveExecutablePath(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings, IPlatformLogger? logger)
        {
            var configured = installSettings?.Vita3kExecutablePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var normalizedConfigured = Path.GetFullPath(configured);
                if (File.Exists(normalizedConfigured))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K executable from platform override: {normalizedConfigured}");
                    return normalizedConfigured;
                }

                logger?.Write(PlatformLogLevel.Warning, $"Configured Vita3K executable path does not exist: {normalizedConfigured}");
            }

            var mapped = romSettings?.EmulatorExecutablePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                var normalizedMapped = Path.GetFullPath(mapped);
                if (File.Exists(normalizedMapped))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K executable from LaunchBox emulator mapping: {normalizedMapped}");
                    return normalizedMapped;
                }

                logger?.Write(PlatformLogLevel.Warning, $"LaunchBox emulator executable path for Vita3K does not exist: {normalizedMapped}");
            }

            return string.Empty;
        }

        private static IEnumerable<string> GetConfigCandidates(string executablePath)
        {
            var executableDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataVita3k = string.IsNullOrWhiteSpace(appData) ? string.Empty : Path.Combine(appData, "Vita3K");

            return new[]
            {
                Path.Combine(executableDirectory, "config.yml"),
                Path.Combine(executableDirectory, "config.yaml"),
                Path.Combine(appDataVita3k, "config.yml"),
                Path.Combine(appDataVita3k, "config.yaml")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string TryReadPrefPath(string configPath, IPlatformLogger? logger)
        {
            try
            {
                var text = File.ReadAllText(configPath);
                var match = PrefPathRegex.Match(text);
                if (!match.Success)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Vita3K config did not contain pref path entry: {configPath}");
                    return string.Empty;
                }

                return match.Groups[1].Value.Trim().Trim('"', '\'');
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to read Vita3K config '{configPath}': {ex.Message}");
                return string.Empty;
            }
        }

        private static string NormalizePath(string path, string configDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return string.Empty;
            }

            return Path.GetFullPath(Path.Combine(configDirectory, path));
        }
    }

    internal sealed class Vita3kEnvironmentResolution
    {
        private Vita3kEnvironmentResolution()
        {
        }

        public bool IsValid { get; private set; }

        public string ValidationError { get; private set; } = string.Empty;

        public string ExecutablePath { get; private set; } = string.Empty;

        public string ConfigPath { get; private set; } = string.Empty;

        public string PrefPath { get; private set; } = string.Empty;

        public string Ux0AppRoot { get; private set; } = string.Empty;

        public static Vita3kEnvironmentResolution Invalid(string message)
        {
            return new Vita3kEnvironmentResolution
            {
                ValidationError = message ?? string.Empty,
                IsValid = false
            };
        }

        public static Vita3kEnvironmentResolution Valid(string executablePath, string configPath, string prefPath, string ux0AppRoot)
        {
            return new Vita3kEnvironmentResolution
            {
                IsValid = true,
                ExecutablePath = executablePath ?? string.Empty,
                ConfigPath = configPath ?? string.Empty,
                PrefPath = prefPath ?? string.Empty,
                Ux0AppRoot = ux0AppRoot ?? string.Empty
            };
        }
    }
}
