using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RomM.Platforms.Abstractions.Models.Install;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    internal sealed class ManagedAdditionalApplicationService
    {
        private readonly LoggingService _logger;

        public ManagedAdditionalApplicationService(LoggingService logger)
        {
            _logger = logger;
        }

        public async Task SyncAsync(IGame baseGame, IReadOnlyList<AdditionalApplicationLaunchInfo> additionalApplications, CancellationToken cancellationToken)
        {
            if (baseGame == null)
            {
                return;
            }

            var platformPath = ResolvePlatformXmlPath(baseGame.Platform);
            if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
            {
                return;
            }

            var xml = await File.ReadAllTextAsync(platformPath, cancellationToken).ConfigureAwait(false);
            var updated = UpsertManagedApplicationsXml(xml, baseGame, additionalApplications ?? Array.Empty<AdditionalApplicationLaunchInfo>(), _logger);
            if (!updated.Changed)
            {
                return;
            }

            var backupPath = RommAdditionalApplicationService.CreatePlatformXmlBackup(platformPath);
            RommAdditionalApplicationService.WritePlatformXmlSafely(platformPath, updated.Xml, backupPath);
        }

        internal static (string Xml, bool Changed) UpsertManagedApplicationsXml(string xml, IGame baseGame, IReadOnlyList<AdditionalApplicationLaunchInfo> entries, LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(xml) || baseGame == null)
            {
                return (xml ?? string.Empty, false);
            }

            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root == null)
            {
                return (xml, false);
            }

            var gameId = baseGame.Id ?? string.Empty;
            var idPrefix = BuildManagedIdPrefix(gameId);
            var additionalApps = root.Elements("AdditionalApplication").ToList();
            var managedApps = additionalApps
                .Where(app => (app.Element("Id")?.Value ?? string.Empty).StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var changed = false;
            foreach (var app in managedApps)
            {
                app.Remove();
                changed = true;
            }

            var desiredEntries = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.ApplicationPath))
                .ToList();
            if (desiredEntries.Count == 0)
            {
                if (!changed)
                {
                    return (xml, false);
                }

                document.Declaration = new XDeclaration("1.0", "utf-8", "yes");
                using var emptyStream = new MemoryStream();
                using (var writer = new StreamWriter(emptyStream, new UnicodeEncoding(false, false), 1024, true))
                {
                    document.Save(writer, SaveOptions.DisableFormatting);
                }

                return (Encoding.Unicode.GetString(emptyStream.ToArray()), true);
            }

            var maxPriority = additionalApps
                .Where(app => string.Equals(app.Element("GameID")?.Value ?? string.Empty, gameId, StringComparison.OrdinalIgnoreCase))
                .Select(app => ParsePriority(app.Element("Priority")?.Value))
                .DefaultIfEmpty(0)
                .Max();

            foreach (var entry in desiredEntries)
            {
                maxPriority++;
                var commandLine = entry.Arguments == null || entry.Arguments.Count == 0
                    ? string.Empty
                    : string.Join(" ", entry.Arguments);
                root.Add(new XElement("AdditionalApplication",
                    new XElement("GogAppId", string.Empty),
                    new XElement("OriginAppId", string.Empty),
                    new XElement("OriginInstallPath", string.Empty),
                    new XElement("Id", BuildManagedId(gameId, entry.Id)),
                    new XElement("PlayCount", "0"),
                    new XElement("PlayTime", "0"),
                    new XElement("GameID", gameId),
                    new XElement("ApplicationPath", entry.ApplicationPath ?? string.Empty),
                    new XElement("AutoRunAfter", "false"),
                    new XElement("AutoRunBefore", "false"),
                    new XElement("CommandLine", commandLine),
                    new XElement("Name", entry.Name ?? string.Empty),
                    new XElement("UseDosBox", "false"),
                    new XElement("UseEmulator", "false"),
                    new XElement("WaitForExit", "false"),
                    new XElement("ReleaseDate", baseGame.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
                    new XElement("Developer", baseGame.Developer ?? string.Empty),
                    new XElement("Publisher", baseGame.Publisher ?? string.Empty),
                    new XElement("Region", baseGame.Region ?? string.Empty),
                    new XElement("Version", "RomMbox Managed"),
                    new XElement("Status", "Managed by RomMbox (Disc)"),
                    new XElement("EmulatorId", string.Empty),
                    new XElement("SideA", "false"),
                    new XElement("SideB", "false"),
                    new XElement("Priority", maxPriority.ToString(CultureInfo.InvariantCulture)),
                    new XElement("Installed", "true"),
                    new XElement("HasCloudSynced", "false")));
                logger?.Info($"Managed additional application synced for '{baseGame.Title}': '{entry.Name}' -> '{entry.ApplicationPath}'.");
                changed = true;
            }

            if (!changed)
            {
                return (xml, false);
            }

            document.Declaration = new XDeclaration("1.0", "utf-8", "yes");
            using var memoryStream = new MemoryStream();
            using (var streamWriter = new StreamWriter(memoryStream, new UnicodeEncoding(false, false), 1024, true))
            {
                document.Save(streamWriter, SaveOptions.DisableFormatting);
            }

            return (Encoding.Unicode.GetString(memoryStream.ToArray()), true);
        }

        private static string BuildManagedIdPrefix(string gameId) => $"rommbox-managed:{gameId}:";

        private static string BuildManagedId(string gameId, string entryId)
        {
            var suffix = string.IsNullOrWhiteSpace(entryId) ? Guid.NewGuid().ToString("N") : entryId.Trim();
            return BuildManagedIdPrefix(gameId) + suffix;
        }

        private static int ParsePriority(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static string ResolvePlatformXmlPath(string launchBoxPlatformName)
        {
            if (string.IsNullOrWhiteSpace(launchBoxPlatformName))
            {
                return string.Empty;
            }

            var root = PluginPaths.GetLaunchBoxRootDirectory();
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var fileName = new string(launchBoxPlatformName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return Path.Combine(root, "Data", "Platforms", fileName + ".xml");
        }
    }
}
