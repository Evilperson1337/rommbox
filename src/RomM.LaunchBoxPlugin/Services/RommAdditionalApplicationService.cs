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
            if (baseGame == null || string.IsNullOrWhiteSpace(baseGame.Id))
            {
                return false;
            }

            var additionalAppId = await _installStateService
                .EnsureRommAdditionalAppIdAsync(baseGame.Id, cancellationToken)
                .ConfigureAwait(false);

            using (_logger.BeginOperation(operationId))
            {
                _logger?.Write(LogLevel.Info, "RomMImportMergeDbLoaded", null,
                    "BaseGameId", baseGame.Id ?? string.Empty,
                    "HasRomMId", !string.IsNullOrWhiteSpace(state?.RommRomId),
                    "HasAdditionalAppId", !string.IsNullOrWhiteSpace(additionalAppId));
            }

            if (string.IsNullOrWhiteSpace(additionalAppId))
            {
                throw new InvalidOperationException("Failed to resolve RomM AdditionalApplication id.");
            }

            var platformPath = ResolvePlatformXmlPath(baseGame.Platform);
            if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
            {
                throw new FileNotFoundException($"Platform XML not found for '{baseGame.Platform}'.", platformPath);
            }

            var xml = await File.ReadAllTextAsync(platformPath, cancellationToken).ConfigureAwait(false);
            var updateResult = UpsertRommAdditionalApplicationXml(xml, baseGame, state, additionalAppId, updateExisting: false, operationId: operationId, _logger);
            if (updateResult.Changed)
            {
                var backupPath = CreatePlatformXmlBackup(platformPath);
                using (_logger.BeginOperation(operationId))
                {
                    var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PlatformXmlPath"] = LoggingService.SanitizePath(platformPath),
                        ["BackupPath"] = LoggingService.SanitizePath(backupPath)
                    };
                    _logger?.Write(LogLevel.Info, "RomMImportMergeXmlBackupCreated", null, props);
                }

                var writeStart = DateTimeOffset.UtcNow;
                WritePlatformXmlSafely(platformPath, updateResult.Xml, backupPath);
                var duration = DateTimeOffset.UtcNow - writeStart;
                using (_logger.BeginOperation(operationId))
                {
                _logger?.Write(LogLevel.Info, "RomMImportMergeXmlWriteCompleted", null,
                    "DurationMs", (long)duration.TotalMilliseconds);
                    _logger?.Write(LogLevel.Info, "RomMInstallMenuEntryAdded", null,
                        "BaseGameId", baseGame.Id ?? string.Empty);
                }
            }
            else
            {
                using (_logger.BeginOperation(operationId))
                {
                    _logger?.Write(LogLevel.Info, "RomMInstallMenuEntrySkippedAlreadyExists", null,
                        "BaseGameId", baseGame.Id ?? string.Empty);
                }
            }

            await _installStateService.UpdateRommAdditionalAppStateAsync(
                    baseGame.Id,
                    baseGame.Id,
                    launchPath: state?.RommLaunchPath ?? state?.InstalledPath ?? string.Empty,
                    launchArgs: state?.RommLaunchArgs ?? string.Empty,
                    syncedUtc: DateTimeOffset.UtcNow,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }

        public async Task<bool> SyncAdditionalApplicationAsync(
            IGame baseGame,
            InstallState state,
            string operationId,
            CancellationToken cancellationToken)
        {
            if (baseGame == null || state == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(state.RommMergedBaseGameId))
            {
                return false;
            }

            using (_logger.BeginOperation(operationId))
            {
                var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BaseGameId"] = state.RommMergedBaseGameId ?? string.Empty,
                    ["AdditionalAppId"] = state.RommAdditionalAppId ?? string.Empty
                };
                _logger?.Write(LogLevel.Info, "RomMAdditionalAppSyncStarted", null, props);
            }

            var additionalAppId = state.RommAdditionalAppId;
            if (string.IsNullOrWhiteSpace(additionalAppId))
            {
                additionalAppId = await _installStateService
                    .EnsureRommAdditionalAppIdAsync(state.RommMergedBaseGameId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(additionalAppId))
            {
                throw new InvalidOperationException("Failed to resolve RomM AdditionalApplication id.");
            }

            var platformPath = ResolvePlatformXmlPath(baseGame.Platform);
            if (string.IsNullOrWhiteSpace(platformPath) || !File.Exists(platformPath))
            {
                throw new FileNotFoundException($"Platform XML not found for '{baseGame.Platform}'.", platformPath);
            }

            var xml = await File.ReadAllTextAsync(platformPath, cancellationToken).ConfigureAwait(false);
            var updateResult = UpsertRommAdditionalApplicationXml(xml, baseGame, state, additionalAppId, updateExisting: true, operationId: operationId, _logger);
            if (updateResult.Changed)
            {
                var backupPath = CreatePlatformXmlBackup(platformPath);
                WritePlatformXmlSafely(platformPath, updateResult.Xml, backupPath);
            }

            await _installStateService.UpdateRommAdditionalAppStateAsync(
                    state.LaunchBoxGameId,
                    state.RommMergedBaseGameId,
                    launchPath: state.RommLaunchPath ?? state.InstalledPath ?? string.Empty,
                    launchArgs: state.RommLaunchArgs ?? string.Empty,
                    syncedUtc: DateTimeOffset.UtcNow,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using (_logger.BeginOperation(operationId))
            {
                var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BaseGameId"] = state.RommMergedBaseGameId ?? string.Empty,
                    ["AdditionalAppId"] = additionalAppId ?? string.Empty
                };
                _logger?.Write(LogLevel.Info, "RomMAdditionalAppSyncCompleted", null, props);
            }

            return updateResult.Changed;
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
                if (existing != null)
                {
                    if (!updateExisting)
                    {
                        using (logger?.BeginOperation(operationId))
                        {
                            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["BaseGameId"] = baseGameId,
                                ["AdditionalAppId"] = additionalAppId
                            };
                            logger?.Write(LogLevel.Info, "RomMImportMergeAdditionalAppExists", null, props);
                        }
                        return new RommAdditionalAppUpdateResult(xml, false, true, ParsePriority(existing.Element("Priority")?.Value));
                    }

                    var changed = false;
                    changed |= SetElementValue(existing, "ApplicationPath", launchPath ?? string.Empty);
                    changed |= SetElementValue(existing, "CommandLine", launchArgs ?? string.Empty);
                    changed |= SetElementValue(existing, "Installed", installed ? "true" : "false");
                    changed |= EnsureElementValue(existing, "Name", "Play RomM Version...");
                    changed |= EnsureElementValue(existing, "Version", "RomM");
                    changed |= SetElementValue(existing, "Status", "Managed by RomMbox");
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
                    new XElement("Name", "Play RomM Version..."),
                    new XElement("UseDosBox", "false"),
                    new XElement("UseEmulator", "false"),
                    new XElement("WaitForExit", "false"),
                    new XElement("ReleaseDate", baseGame?.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
                    new XElement("Developer", baseGame?.Developer ?? string.Empty),
                    new XElement("Publisher", baseGame?.Publisher ?? string.Empty),
                    new XElement("Region", baseGame?.Region ?? string.Empty),
                    new XElement("Version", "RomM"),
                    new XElement("Status", "Managed by RomMbox"),
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
