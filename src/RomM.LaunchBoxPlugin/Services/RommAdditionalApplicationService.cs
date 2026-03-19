using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RomMbox.Models;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services
{
    internal sealed class RommAdditionalApplicationService
    {
        internal const string RommVersion = "RomM";
        internal const string ManagedStatus = "Managed by RomMbox";
        internal const string InstallCaption = "Install RomM Version...";
        internal const string PlayCaption = "Play RomM Version...";

        private readonly LoggingService _logger;
        private readonly InstallStateService _installStateService;

        public RommAdditionalApplicationService(LoggingService logger, InstallStateService installStateService)
        {
            _logger = logger;
            _installStateService = installStateService;
        }

        public async Task<bool> EnsureMergedAdditionalApplicationAsync(
            IGame baseGame,
            InstallState state,
            string operationId,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return false;
        }

        public async Task<bool> SyncAdditionalApplicationAsync(
            IGame baseGame,
            InstallState state,
            string operationId,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return false;
        }

        internal static RommAdditionalAppUpdateResult UpsertRommAdditionalApplicationXml(
            string xml,
            IGame baseGame,
            InstallState state,
            string additionalAppId,
            bool updateExisting,
            string operationId,
            LoggingService logger)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new RommAdditionalAppUpdateResult(xml ?? string.Empty, false, false, 0);
            }

            var baseGameId = baseGame?.Id ?? string.Empty;
            var baseTitle = baseGame?.Title ?? string.Empty;
            var installed = state?.IsInstalled == true;
            var launchPath = installed ? (state?.RommLaunchPath ?? state?.InstalledPath ?? string.Empty) : string.Empty;
            var launchArgs = installed ? (state?.RommLaunchArgs ?? string.Empty) : string.Empty;
            var displayName = GetDisplayName(installed);

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                var root = doc.Root;
                if (root == null)
                {
                    return new RommAdditionalAppUpdateResult(xml, false, false, 0);
                }

                var additionalApps = root.Elements("AdditionalApplication").ToList();
                var existing = additionalApps.FirstOrDefault(app =>
                    string.Equals(app.Element("Id")?.Value ?? string.Empty, additionalAppId, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = additionalApps.FirstOrDefault(app => IsRommAdditionalApplicationElement(app, baseGameId));
                }

                if (existing != null)
                {
                    var changed = false;
                    changed |= SetElementValue(existing, "Id", additionalAppId);

                    if (!updateExisting)
                    {
                        using (logger?.BeginOperation(operationId))
                        {
                            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["BaseGameId"] = baseGameId,
                                ["AdditionalAppId"] = additionalAppId,
                                ["MatchedByTag"] = !string.Equals(existing.Element("Id")?.Value ?? string.Empty, additionalAppId, StringComparison.OrdinalIgnoreCase)
                            };
                            logger?.Write(LogLevel.Info, "RomMImportMergeAdditionalAppExists", null, props);
                        }
                        if (!changed)
                        {
                            return new RommAdditionalAppUpdateResult(xml, false, true, ParsePriority(existing.Element("Priority")?.Value));
                        }

                        doc.Declaration = new XDeclaration("1.0", "utf-8", "yes");
                        using var existingIdStream = new MemoryStream();
                        using (var streamWriter = new StreamWriter(existingIdStream, new UnicodeEncoding(false, false), 1024, true))
                        {
                            doc.Save(streamWriter, SaveOptions.DisableFormatting);
                        }

                        return new RommAdditionalAppUpdateResult(Encoding.Unicode.GetString(existingIdStream.ToArray()), true, true, ParsePriority(existing.Element("Priority")?.Value));
                    }

                    changed |= SetElementValue(existing, "ApplicationPath", launchPath ?? string.Empty);
                    changed |= SetElementValue(existing, "CommandLine", launchArgs ?? string.Empty);
                    changed |= SetElementValue(existing, "Installed", installed ? "true" : "false");
                    changed |= SetElementValue(existing, "Name", displayName);
                    changed |= SetElementValue(existing, "Version", RommVersion);
                    changed |= SetElementValue(existing, "Status", ManagedStatus);
                    changed |= SetElementValue(existing, "GameID", baseGameId);
                    if (!changed)
                    {
                        return new RommAdditionalAppUpdateResult(xml, false, true, ParsePriority(existing.Element("Priority")?.Value));
                    }

                    doc.Declaration = new XDeclaration("1.0", "utf-8", "yes");
                    using var memoryStream = new MemoryStream();
                    using (var streamWriter = new StreamWriter(memoryStream, new UnicodeEncoding(false, false), 1024, true))
                    {
                        doc.Save(streamWriter, SaveOptions.DisableFormatting);
                    }

                    return new RommAdditionalAppUpdateResult(Encoding.Unicode.GetString(memoryStream.ToArray()), true, true, ParsePriority(existing.Element("Priority")?.Value));
                }

                var maxPriority = additionalApps
                    .Where(app => string.Equals(app.Element("GameID")?.Value ?? string.Empty, baseGameId, StringComparison.OrdinalIgnoreCase))
                    .Select(app => ParsePriority(app.Element("Priority")?.Value))
                    .DefaultIfEmpty(0)
                    .Max();

                var priority = maxPriority + 1;
                var appElement = new XElement("AdditionalApplication",
                    new XElement("GogAppId", string.Empty),
                    new XElement("OriginAppId", string.Empty),
                    new XElement("OriginInstallPath", string.Empty),
                    new XElement("Id", additionalAppId),
                    new XElement("PlayCount", "0"),
                    new XElement("PlayTime", "0"),
                    new XElement("GameID", baseGameId),
                    new XElement("ApplicationPath", launchPath ?? string.Empty),
                    new XElement("AutoRunAfter", "false"),
                    new XElement("AutoRunBefore", "false"),
                    new XElement("CommandLine", launchArgs ?? string.Empty),
                    new XElement("Name", displayName),
                    new XElement("UseDosBox", "false"),
                    new XElement("UseEmulator", "false"),
                    new XElement("WaitForExit", "false"),
                    new XElement("ReleaseDate", baseGame?.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
                    new XElement("Developer", baseGame?.Developer ?? string.Empty),
                    new XElement("Publisher", baseGame?.Publisher ?? string.Empty),
                    new XElement("Region", baseGame?.Region ?? string.Empty),
                    new XElement("Version", RommVersion),
                    new XElement("Status", ManagedStatus),
                    new XElement("EmulatorId", string.Empty),
                    new XElement("SideA", "false"),
                    new XElement("SideB", "false"),
                    new XElement("Priority", priority.ToString(CultureInfo.InvariantCulture)),
                    new XElement("Installed", installed ? "true" : "false"),
                    new XElement("HasCloudSynced", "false"));

                root.Add(appElement);

                using (logger?.BeginOperation(operationId))
                {
                    var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["BaseGameId"] = baseGameId,
                        ["AdditionalAppId"] = additionalAppId,
                        ["Priority"] = priority
                    };
                    logger?.Write(LogLevel.Info, "RomMImportMergeAdditionalAppCreated", null, props);
                }

                doc.Declaration = new XDeclaration("1.0", "utf-8", "yes");
                using var newStream = new MemoryStream();
                using (var streamWriter = new StreamWriter(newStream, new UnicodeEncoding(false, false), 1024, true))
                {
                    doc.Save(streamWriter, SaveOptions.DisableFormatting);
                }

                return new RommAdditionalAppUpdateResult(Encoding.Unicode.GetString(newStream.ToArray()), true, false, priority);
            }
            catch (Exception ex)
            {
                var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BaseGameId"] = baseGameId,
                    ["Title"] = baseTitle
                };
                logger?.Write(LogLevel.Error, "RomMImportMergeFailed", ex, props);
                throw;
            }
        }

        internal static string GetDisplayName(bool installed)
        {
            return installed ? PlayCaption : InstallCaption;
        }

        internal static bool IsRommAdditionalApplication(IAdditionalApplication additionalApplication, string baseGameId = null, string additionalAppId = null)
        {
            if (additionalApplication == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(additionalAppId)
                && string.Equals(additionalApplication.Id ?? string.Empty, additionalAppId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(baseGameId)
                && !string.Equals(additionalApplication.GameId ?? string.Empty, baseGameId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(additionalApplication.Version ?? string.Empty, RommVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(additionalApplication.Status ?? string.Empty, ManagedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(additionalApplication.Name ?? string.Empty, InstallCaption, StringComparison.OrdinalIgnoreCase)
                || string.Equals(additionalApplication.Name ?? string.Empty, PlayCaption, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRommAdditionalApplicationElement(XElement element, string baseGameId)
        {
            if (element == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(baseGameId)
                && !string.Equals(element.Element("GameID")?.Value ?? string.Empty, baseGameId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(element.Element("Version")?.Value ?? string.Empty, RommVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(element.Element("Status")?.Value ?? string.Empty, ManagedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var name = element.Element("Name")?.Value ?? string.Empty;
            return string.Equals(name, InstallCaption, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, PlayCaption, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EnsureElementValue(XElement parent, string name, string value)
        {
            var element = parent.Element(name);
            if (element == null)
            {
                parent.Add(new XElement(name, value ?? string.Empty));
                return true;
            }

            if (string.IsNullOrWhiteSpace(element.Value) && !string.IsNullOrWhiteSpace(value))
            {
                element.Value = value;
                return true;
            }

            return false;
        }

        private static bool SetElementValue(XElement parent, string name, string value)
        {
            var element = parent.Element(name);
            if (element == null)
            {
                parent.Add(new XElement(name, value ?? string.Empty));
                return true;
            }

            if (!string.Equals(element.Value ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
            {
                element.Value = value ?? string.Empty;
                return true;
            }

            return false;
        }

        private static int ParsePriority(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0;
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

            var fileName = SanitizePlatformFileName(launchBoxPlatformName);
            return Path.Combine(root, "Data", "Platforms", fileName + ".xml");
        }

        private static string SanitizePlatformFileName(string platformName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(platformName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return sanitized.Trim();
        }

        internal static string CreatePlatformXmlBackup(string platformPath)
        {
            var directory = Path.GetDirectoryName(platformPath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(platformPath);
            var extension = Path.GetExtension(platformPath);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var backupFile = Path.Combine(directory, $"{fileName}.{timestamp}.bak{extension}");
            File.Copy(platformPath, backupFile, overwrite: false);
            return backupFile;
        }

        internal static void WritePlatformXmlSafely(string platformPath, string xml, string backupPath)
        {
            if (string.IsNullOrWhiteSpace(platformPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(platformPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                File.WriteAllText(platformPath, xml, new UnicodeEncoding(false, false));
                return;
            }

            var fileName = Path.GetFileName(platformPath);
            var tempPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(tempPath, xml, new UnicodeEncoding(false, false));
            try
            {
                File.Replace(tempPath, platformPath, backupPath, true);
            }
            catch
            {
                File.WriteAllText(platformPath, xml, new UnicodeEncoding(false, false));
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        internal sealed record RommAdditionalAppUpdateResult(string Xml, bool Changed, bool Existed, int Priority);
    }
}
