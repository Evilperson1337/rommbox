using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Metadata;
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.Vita.Inspection;

namespace RomM.Platforms.Vita
{
    public sealed class VitaPlatformInstaller : IPlatformInstaller, IPlatformInstallerMetadata, IPlatformInstallerIdentityMetadata
    {
        public string PlatformKey => "psvita";
        public string DisplayName => "PlayStation Vita";
        public IReadOnlyCollection<string>? SupportedPlatformIds => new[] { "psvita", "vita", "sony-playstation-vita" };
        public IReadOnlyCollection<string>? SupportedPlatformAliases => new[]
        {
            "psvita",
            "ps vita",
            "vita",
            "playstation vita",
            "sony playstation vita"
        };

        public PlatformInstallerCapabilities Capabilities => new PlatformInstallerCapabilities
        {
            SupportsArchives = true,
            SupportsDirectFiles = true,
            RequiresStagingInspection = true,
            SupportsAutoFormatDetection = true,
            SupportsInstaller = false,
            SupportsSilentInstaller = false,
            SupportsUninstall = true,
            SupportsInstallStateDetection = true,
            SupportsApplicationPathDiscovery = true,
            SupportsDlc = true,
            SupportsUpdates = true,
            SupportsRaps = false,
            RequiresEmulatorPath = false
        };

        public PlatformConfigDescriptor? GetConfigDescriptor()
        {
            return new PlatformConfigDescriptor
            {
                Fields = new List<PlatformConfigFieldDescriptor>
                {
                    new()
                    {
                        Key = "Vita3kExecutablePath",
                        Label = "Vita3K Executable",
                        Description = "Optional Vita3K executable override. If omitted, LaunchBox emulator mapping is used.",
                        Type = PlatformConfigFieldType.Path,
                        Required = false,
                        Advanced = false
                    },
                    new()
                    {
                        Key = "VitaInstallUpdatesAutomatically",
                        Label = "Install Updates Automatically",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "false"
                    },
                    new()
                    {
                        Key = "VitaInstallDlcAutomatically",
                        Label = "Install DLC Automatically",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "false"
                    },
                    new()
                    {
                        Key = "VitaFailIfEmulatorNotReady",
                        Label = "Fail If Emulator Not Ready",
                        Type = PlatformConfigFieldType.Boolean,
                        Required = false,
                        Advanced = true,
                        DefaultValue = "false"
                    }
                }
            };
        }

        public Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError>
                {
                    new() { Code = "context_missing", Message = "Platform context missing." }
                };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installedPath = ctx.InstalledPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(installedPath))
            {
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "missing_installed_path", Message = "Installed path missing." }
                };
                return Task.FromResult(result);
            }

            if (!File.Exists(installedPath))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "installed_missing", Message = "Installed Vita launch token not found on disk." }
                };
                return Task.FromResult(result);
            }

            if (!IsTokenPath(installedPath))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "unsupported_extension", Message = "Installed file is not a Vita launch token (*.vita3k.json)." }
                };
                return Task.FromResult(result);
            }

            var metadata = ReadToken(installedPath);
            if (string.IsNullOrWhiteSpace(metadata.TitleId))
            {
                result.IsInstalled = false;
                result.Warnings = new List<DetectionWarning>
                {
                    new() { Code = "missing_title_id", Message = "Installed Vita launch token is missing title id metadata." }
                };
                return Task.FromResult(result);
            }

            result.RecommendedExecutablePath = installedPath;
            result.CandidateExecutablePaths = new List<string> { installedPath };
            return Task.FromResult(result);
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var installedPath = ctx?.InstalledPath ?? string.Empty;
            var valid = !string.IsNullOrWhiteSpace(installedPath)
                && File.Exists(installedPath)
                && IsTokenPath(installedPath)
                && !string.IsNullOrWhiteSpace(ReadToken(installedPath).TitleId);

            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "PlayStation Vita install verified." : "PlayStation Vita install missing on disk."
            });
        }

        public Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Install context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing PlayStation Vita install...", 0));

            var installRoot = ResolveInstallRoot(ctx.InstallDirectory, ctx.RomSettings?.RomRootPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                ctx.Logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return Task.FromResult(new InstallResult { Success = false, Message = "PlayStation Vita install directory missing." });
            }

            if (!EnsureDirectoryWritable(installRoot, ctx.Logger, out var writableError))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = writableError });
            }

            var inspector = new VitaGameInspector();
            var inspection = inspector.Inspect(ctx.ArchivePath, ctx.ExtractedPath, ctx.GameName, ctx.Logger);
            foreach (var warning in inspection.Warnings)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, warning);
            }

            if (!inspection.IsValid)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = inspection.ErrorMessage ?? "PlayStation Vita inspection failed." });
            }

            if (string.IsNullOrWhiteSpace(inspection.TitleId))
            {
                return Task.FromResult(new InstallResult { Success = false, Message = "Unable to determine PlayStation Vita title id safely." });
            }

            var readiness = ValidateVita3kReadiness(ctx.Settings, ctx.Logger);
            if (!readiness.IsReady && readiness.FailInstall)
            {
                return Task.FromResult(new InstallResult { Success = false, Message = readiness.Message });
            }

            var gameRoot = Path.Combine(installRoot, NormalizePathSegment(inspection.TitleName, inspection.TitleId));
            Directory.CreateDirectory(gameRoot);
            var cacheRoot = Path.Combine(gameRoot, "cache");
            Directory.CreateDirectory(cacheRoot);

            ctx.Logger?.Write(PlatformLogLevel.Info, "Resolved Vita install strategy: EmulatorManagedImport");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Resolved Vita install directory: {gameRoot}");

            var importedPackages = new List<string>();
            var importedUpdates = 0;
            var importedDlc = 0;

            foreach (var candidate in inspection.OrderedInstallCandidates)
            {
                ct.ThrowIfCancellationRequested();

                if (candidate.Role == VitaContentRole.Update && !(ctx.Settings?.VitaInstallUpdatesAutomatically ?? false))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, "Detected Vita update package but automated update import is not enabled. Install will continue without update import.");
                    continue;
                }

                if (candidate.Role == VitaContentRole.Dlc && !(ctx.Settings?.VitaInstallDlcAutomatically ?? false))
                {
                    ctx.Logger?.Write(PlatformLogLevel.Warning, "Detected Vita DLC package but automated DLC import is not enabled. Install will continue without DLC import.");
                    continue;
                }

                var stagedArtifact = StageCandidate(candidate, cacheRoot, ctx.Logger);
                if (string.IsNullOrWhiteSpace(stagedArtifact))
                {
                    continue;
                }

                importedPackages.Add(stagedArtifact);
                if (candidate.Role == VitaContentRole.Update)
                {
                    importedUpdates++;
                }
                else if (candidate.Role == VitaContentRole.Dlc)
                {
                    importedDlc++;
                }

                progress?.Report(new InstallProgress("Installing", $"Importing Vita content ({candidate.Role})...", 50));
                ImportIntoVita3k(stagedArtifact, ctx.Settings, ctx.Logger);
            }

            var tokenPath = Path.Combine(gameRoot, $"{inspection.TitleId}.vita3k.json");
            WriteToken(tokenPath, inspection.TitleId, inspection.TitleName, inspection.Version, importedPackages, importedUpdates, importedDlc, ctx.Logger);

            var launchArgs = BuildLaunchArguments(ctx.RomSettings, inspection.TitleId);
            ctx.Logger?.Write(PlatformLogLevel.Info, "PlayStation Vita install completed.");
            ctx.Logger?.Write(PlatformLogLevel.Info, $"Application path set to: {tokenPath}");

            return Task.FromResult(new InstallResult
            {
                Success = true,
                Message = "PlayStation Vita install completed.",
                ExecutablePath = tokenPath,
                Arguments = string.IsNullOrWhiteSpace(launchArgs) ? Array.Empty<string>() : new[] { launchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = gameRoot
            });
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "PlayStation Vita uninstall started.", 0, true));
            ctx.Logger?.Write(PlatformLogLevel.Info, "PlayStation Vita uninstall started");

            var removed = 0;
            var notes = new List<string>();
            var installedPath = ctx.InstalledPath ?? string.Empty;
            var token = ReadToken(installedPath);

            foreach (var ownedPath in token.CachedArtifacts)
            {
                try
                {
                    if (File.Exists(ownedPath))
                    {
                        File.Delete(ownedPath);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete owned artifact '{ownedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete owned Vita artifact '{ownedPath}': {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(installedPath))
            {
                try
                {
                    if (File.Exists(installedPath))
                    {
                        ctx.Logger?.Write(PlatformLogLevel.Info, $"Deleting installed launch token: {installedPath}");
                        File.Delete(installedPath);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete Vita launch token '{installedPath}': {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"Failed to delete Vita launch token '{installedPath}': {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(token.TitleId))
            {
                notes.Add($"Vita3K emulator-managed uninstall for TitleId '{token.TitleId}' is not automated yet; remove from Vita3K UI if desired.");
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"Vita3K uninstall automation not available for TitleId '{token.TitleId}'.");
            }

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));
            ctx.Logger?.Write(PlatformLogLevel.Info, "Uninstall completed");
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "PlayStation Vita uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        private static string ResolveInstallRoot(string? installDirectory, string? romRootPath)
        {
            if (!string.IsNullOrWhiteSpace(romRootPath))
            {
                return romRootPath;
            }

            return installDirectory ?? string.Empty;
        }

        private static bool EnsureDirectoryWritable(string directory, IPlatformLogger? logger, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "PlayStation Vita install directory missing.";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
                using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Install directory not writable: {ex.Message}";
                logger?.Write(PlatformLogLevel.Error, "ERROR: Install directory not writable");
                logger?.Write(PlatformLogLevel.Warning, $"PlayStation Vita install failed write-check for '{directory}': {ex.Message}");
                return false;
            }
        }

        private static string StageCandidate(VitaContentCandidate candidate, string cacheRoot, IPlatformLogger logger)
        {
            try
            {
                if (File.Exists(candidate.Path))
                {
                    var target = Path.Combine(cacheRoot, Path.GetFileName(candidate.Path));
                    MoveOrReplace(candidate.Path, target, logger);
                    return target;
                }

                if (Directory.Exists(candidate.Path))
                {
                    var folderName = Path.GetFileName(candidate.Path) ?? "vita-layout";
                    var targetRoot = Path.Combine(cacheRoot, NormalizePathSegment(folderName, "vita-layout"));
                    DirectoryCopy(candidate.Path, targetRoot);
                    return targetRoot;
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to stage Vita candidate '{candidate.Path}': {ex.Message}");
            }

            return string.Empty;
        }

        private static void ImportIntoVita3k(string stagedPath, PlatformInstallSettings? settings, IPlatformLogger? logger)
        {
            var executable = settings?.Vita3kExecutablePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                logger?.Write(PlatformLogLevel.Warning, "Vita3K executable not configured or not found; skipping automated import command. Content staged for manual Vita3K import.");
                return;
            }

            try
            {
                logger?.Write(PlatformLogLevel.Info, $"Importing Vita content into Vita3K: {stagedPath}");
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"--pkg {QuoteArgument(stagedPath)}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    logger?.Write(PlatformLogLevel.Warning, "Failed to start Vita3K import process.");
                    return;
                }

                process.WaitForExit(15000);
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }
                }

                logger?.Write(PlatformLogLevel.Info, $"Vita3K import exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Vita3K import command failed for '{stagedPath}': {ex.Message}");
            }
        }

        private static string BuildLaunchArguments(RomInstallSettings? settings, string titleId)
        {
            var template = settings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "--title-id {titleId}";
            }

            return template
                .Replace("{titleId}", titleId)
                .Replace("{rom}", QuoteArgument(titleId));
        }

        private static void WriteToken(
            string tokenPath,
            string titleId,
            string titleName,
            string version,
            IReadOnlyCollection<string> cachedArtifacts,
            int updates,
            int dlc,
            IPlatformLogger logger)
        {
            var lines = new List<string>
            {
                "{",
                $"  \"titleId\": \"{EscapeJson(titleId)}\",",
                $"  \"titleName\": \"{EscapeJson(titleName)}\",",
                $"  \"version\": \"{EscapeJson(version)}\",",
                $"  \"updatesImported\": {updates},",
                $"  \"dlcImported\": {dlc},",
                "  \"cachedArtifacts\": ["
            };

            var indexed = cachedArtifacts.ToList();
            for (var i = 0; i < indexed.Count; i++)
            {
                var suffix = i == indexed.Count - 1 ? string.Empty : ",";
                lines.Add($"    \"{EscapeJson(indexed[i])}\"{suffix}");
            }

            lines.Add("  ]");
            lines.Add("}");

            File.WriteAllLines(tokenPath, lines, Encoding.UTF8);
            logger?.Write(PlatformLogLevel.Info, $"Vita launch token created: {tokenPath}");
        }

        private static VitaInstallToken ReadToken(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return VitaInstallToken.Empty;
            }

            try
            {
                var text = File.ReadAllText(path);
                var titleId = ExtractJsonString(text, "titleId");
                var artifacts = ExtractJsonArray(text, "cachedArtifacts");
                return new VitaInstallToken(titleId, artifacts);
            }
            catch
            {
                return VitaInstallToken.Empty;
            }
        }

        private static EmulatorReadinessResult ValidateVita3kReadiness(PlatformInstallSettings settings, IPlatformLogger logger)
        {
            var failInstall = settings?.VitaFailIfEmulatorNotReady ?? false;
            var exePath = settings?.Vita3kExecutablePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                logger?.Write(PlatformLogLevel.Info, "Vita3K executable override not set; LaunchBox emulator mapping will be used.");
                return EmulatorReadinessResult.Ready(failInstall, string.Empty);
            }

            if (!File.Exists(exePath))
            {
                var message = $"Vita3K executable not found at '{exePath}'.";
                logger?.Write(PlatformLogLevel.Warning, message);
                return EmulatorReadinessResult.NotReady(failInstall, message);
            }

            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita3K executable: {exePath}");
            return EmulatorReadinessResult.Ready(failInstall, string.Empty);
        }

        private static string EscapeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var marker = $"\"{key}\"";
            var markerIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return string.Empty;
            }

            var colonIndex = json.IndexOf(':', markerIndex);
            if (colonIndex < 0)
            {
                return string.Empty;
            }

            var firstQuote = json.IndexOf('"', colonIndex + 1);
            if (firstQuote < 0)
            {
                return string.Empty;
            }

            var secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0)
            {
                return string.Empty;
            }

            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }

        private static List<string> ExtractJsonArray(string json, string key)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return values;
            }

            var marker = $"\"{key}\"";
            var markerIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return values;
            }

            var openIndex = json.IndexOf('[', markerIndex);
            var closeIndex = json.IndexOf(']', openIndex + 1);
            if (openIndex < 0 || closeIndex < 0 || closeIndex <= openIndex)
            {
                return values;
            }

            var body = json.Substring(openIndex + 1, closeIndex - openIndex - 1);
            foreach (var rawPart in body.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = rawPart.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    values.Add(cleaned);
                }
            }

            return values;
        }

        private static string NormalizePathSegment(string value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalid, '_');
            }

            normalized = normalized.Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static void MoveOrReplace(string sourcePath, string targetPath, IPlatformLogger logger)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Source and target paths are required.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PlayStation Vita source file not found.", sourcePath);
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("Target directory could not be resolved.");
            }

            Directory.CreateDirectory(targetDirectory);
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                logger?.Write(PlatformLogLevel.Info, $"Source and destination are identical; no move needed: '{targetPath}'.");
                return;
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            var sourceRoot = Path.GetPathRoot(sourcePath.Trim()) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetPath.Trim()) ?? string.Empty;
            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
            }
            else
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                File.Delete(sourcePath);
            }
        }

        private static void DirectoryCopy(string sourceDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
            }

            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var destination = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetDir);
                File.Copy(file, destination, overwrite: true);
            }
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }

        private static bool IsTokenPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.EndsWith(".vita3k.json", StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct EmulatorReadinessResult
        {
            public bool IsReady { get; }
            public bool FailInstall { get; }
            public string Message { get; }

            private EmulatorReadinessResult(bool isReady, bool failInstall, string message)
            {
                IsReady = isReady;
                FailInstall = failInstall;
                Message = message;
            }

            public static EmulatorReadinessResult Ready(bool failInstall, string message) => new(true, failInstall, message);
            public static EmulatorReadinessResult NotReady(bool failInstall, string message) => new(false, failInstall, message);
        }

        private readonly struct VitaInstallToken
        {
            public static VitaInstallToken Empty => new(string.Empty, new List<string>());

            public VitaInstallToken(string titleId, List<string> cachedArtifacts)
            {
                TitleId = titleId;
                CachedArtifacts = cachedArtifacts ?? new List<string>();
            }

            public string TitleId { get; }
            public List<string> CachedArtifacts { get; }
        }
    }
}

