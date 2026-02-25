using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Models.Install;
using RomMbox.Models.PlatformMapping;
using RomMbox.Services.Logging;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Orchestrates Windows install workflows, including optional content.
    /// </summary>
    internal sealed class WindowsInstallSubsystem
    {
        private static readonly string[] RootFolders = { "dlc", "update", "ost", "bonus", "pre-reqs" };
        private readonly LoggingService _logger;
        private readonly ArchiveService _archiveService;
        private readonly WindowsInstallClassifier _classifier;
        private readonly ExecutableResolver _executableResolver;
        private readonly Dictionary<string, bool> _innoSignatureCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Creates the Windows install subsystem with required services.
        /// </summary>
        /// <param name="logger">Logging service.</param>
        /// <param name="archiveService">Archive service for extraction.</param>
        public WindowsInstallSubsystem(LoggingService logger, ArchiveService archiveService)
        {
            _logger = logger;
            _archiveService = archiveService;
            _classifier = new WindowsInstallClassifier(archiveService, logger);
            _executableResolver = new ExecutableResolver(logger);
        }

        /// <summary>
        /// Installs a Windows title from an archive or extracted content.
        /// </summary>
        /// <param name="archivePath">The archive path.</param>
        /// <param name="extractedPath">Optional extracted path.</param>
        /// <param name="installDir">Target install directory.</param>
        /// <param name="mapping">Platform mapping settings.</param>
        /// <param name="gameName">Game display name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The install result.</returns>
        public async Task<WindowsInstallResult> InstallAsync(
            string archivePath,
            string extractedPath,
            string installDir,
            PlatformMapping mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<Models.Download.DownloadProgress> extractionProgress = null,
            IProgress<double> installProgress = null,
            string finalInstallDir = null,
            bool preferFinalInstallDirForInstaller = false)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                return WindowsInstallResult.Failed("Archive path not found.");
            }

            if (string.IsNullOrWhiteSpace(installDir))
            {
                return WindowsInstallResult.Failed("Install directory is required.");
            }

            _logger?.Info($"Windows install start. Archive='{archivePath}', InstallDir='{installDir}', ExtractedPath='{extractedPath ?? string.Empty}'.");
            Directory.CreateDirectory(installDir);

            var tempRoot = ResolveTempRoot(archivePath, extractedPath)
                ?? Path.Combine(Paths.PluginPaths.GetPluginRootDirectory(), "temp", "install", Guid.NewGuid().ToString("N"));
            var tempExtractDir = Path.Combine(tempRoot, "extracted");
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(tempExtractDir);

            var cleanupTempRoot = false;
            try
            {
                var extractRoot = !string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath)
                    ? EnsureExtractedInTemp(extractedPath, tempExtractDir)
                    : await _archiveService
                        .ExtractAsync(archivePath, tempExtractDir, ExtractionBehavior.Subfolder, cancellationToken, extractionProgress)
                        .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(extractRoot))
                {
                    return WindowsInstallResult.Failed("Extraction failed.");
                }

                if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
                {
                    _logger?.Info($"Extraction completed. ExtractedPath='{extractRoot}'.");
                }

                installProgress?.Report(0);

                var archiveInstallType = _classifier.DetectInstallType(archivePath, extractRoot);
                _logger?.Info($"Windows archive install classification: {archiveInstallType}.");

                var baseRoot = archiveInstallType == InstallType.Installer
                    ? extractRoot
                    : NormalizePortableRoot(extractRoot, installDir, gameName, archivePath);
                _logger?.Info($"Windows base root selected: '{baseRoot}'. ArchiveInstallType={archiveInstallType}.");
                var contentRoots = DiscoverContentRoots(baseRoot);
                _logger?.Info($"Windows content roots: Base='{baseRoot}', PreReqs='{contentRoots.PreReqs ?? "<none>"}', Bonus='{contentRoots.Bonus ?? "<none>"}', Ost='{contentRoots.Ost ?? "<none>"}', Update='{contentRoots.Update ?? "<none>"}', Dlc='{contentRoots.Dlc ?? "<none>"}'.");
                _logger?.Info($"Windows optional content config: PreReqsEnabled={mapping?.InstallPreReqs == true}, PreReqsRoot='{mapping?.PreReqsRootPath ?? "<empty>"}'.");

                _logger?.Info("Install check bypassed because content was just extracted for this install run.");

                var autoSilent = mapping?.InstallerMode == InstallerMode.AutoInnoSilent;
                var resolvedInstallDir = preferFinalInstallDirForInstaller && archiveInstallType == InstallType.Installer
                    ? finalInstallDir ?? installDir
                    : installDir;
                var baseResult = autoSilent && archiveInstallType == InstallType.Installer
                    ? await TryRunCombinedInstallerBatchAsync(baseRoot, contentRoots.Update, contentRoots.Dlc, resolvedInstallDir, mapping, gameName, cancellationToken, installProgress)
                        .ConfigureAwait(false)
                    : await InstallBaseAsync(baseRoot, resolvedInstallDir, mapping, gameName, skipAlreadyInstalled: true, cancellationToken, archiveInstallType, installProgress)
                        .ConfigureAwait(false);
                if (!baseResult.Success)
                {
                    return baseResult;
                }

                if (!baseResult.InstallType.HasValue || baseResult.InstallType.Value != InstallType.Installer || !autoSilent)
                {
                    await InstallUpdateAndDlcAsync(contentRoots.Update, resolvedInstallDir, mapping, gameName, "UPDATE", cancellationToken, installProgress).ConfigureAwait(false);
                    await InstallUpdateAndDlcAsync(contentRoots.Dlc, resolvedInstallDir, mapping, gameName, "DLC", cancellationToken, installProgress).ConfigureAwait(false);
                }
            await InstallOptionalContentAsync(contentRoots.Ost, resolvedInstallDir, mapping, mapping?.MusicRootPath, mapping?.InstallOst == true, gameName, "OST", cancellationToken).ConfigureAwait(false);
            await InstallOptionalContentAsync(contentRoots.Bonus, resolvedInstallDir, mapping, mapping?.BonusRootPath, mapping?.InstallBonus == true, gameName, "Bonus", cancellationToken).ConfigureAwait(false);
            await InstallOptionalContentAsync(contentRoots.PreReqs, resolvedInstallDir, mapping, mapping?.PreReqsRootPath, mapping?.InstallPreReqs == true, gameName, "Pre-Reqs", cancellationToken, deleteSource: true, perGame: false).ConfigureAwait(false);

                cleanupTempRoot = true;

                return baseResult;
            }
            catch (Exception ex)
            {
                _logger?.Error("Windows install failed; leaving temp install root for troubleshooting.", ex);
                throw;
            }
            finally
            {
                try
                {
                    if (cleanupTempRoot && Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                        _logger?.Info($"Temp install root cleaned up: '{tempRoot}'.");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Failed to clean up temp install root '{tempRoot}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Normalizes portable installs into a game-named folder under the install directory.
        /// </summary>
        private string NormalizePortableRoot(string baseRoot, string installDir, string gameName, string archivePath)
        {
            if (string.IsNullOrWhiteSpace(baseRoot) || string.IsNullOrWhiteSpace(installDir))
            {
                return baseRoot;
            }

            var safeGameName = NormalizeGameFolderName(gameName, Path.GetFileName(baseRoot));
            var targetRoot = Path.Combine(installDir, safeGameName);
            if (string.Equals(baseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return baseRoot;
            }

            var baseParent = Path.GetDirectoryName(baseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(baseParent)
                && string.Equals(baseParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(targetRoot))
                {
                    Directory.Delete(targetRoot, recursive: true);
                }

                if (IsSameVolume(baseRoot, targetRoot))
                {
                    Directory.Move(baseRoot, targetRoot);
                }
                else
                {
                    CopyDirectory(baseRoot, targetRoot);
                    Directory.Delete(baseRoot, recursive: true);
                }

                _logger?.Info($"Relocated extracted folder '{baseRoot}' -> '{targetRoot}'.");
                return targetRoot;
            }

            if (!Directory.Exists(targetRoot))
            {
                Directory.CreateDirectory(targetRoot);
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(baseRoot))
            {
                if (!string.IsNullOrWhiteSpace(archivePath)
                    && string.Equals(entry, archivePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileName(entry);
                var destination = Path.Combine(targetRoot, name);
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(destination))
                    {
                        Directory.Delete(destination, recursive: true);
                    }

                    if (IsSameVolume(entry, destination))
                    {
                        Directory.Move(entry, destination);
                    }
                    else
                    {
                        CopyDirectory(entry, destination);
                        Directory.Delete(entry, recursive: true);
                    }
                }
                else
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    if (IsSameVolume(entry, destination))
                    {
                        File.Move(entry, destination);
                    }
                    else
                    {
                        File.Copy(entry, destination, overwrite: true);
                        File.Delete(entry);
                    }
                }
            }

            try
            {
                Directory.Delete(baseRoot, recursive: false);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Portable root cleanup skipped: {ex.Message}");
            }

            return targetRoot;
        }

        /// <summary>
        /// Ensures extracted content is placed under the temp extraction directory.
        /// </summary>
        private string EnsureExtractedInTemp(string extractedPath, string tempExtractDir)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return extractedPath;
            }

            var normalizedExtracted = Path.GetFullPath(extractedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var normalizedTemp = Path.GetFullPath(tempExtractDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (normalizedExtracted.StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
            {
                return extractedPath;
            }

            var target = Path.Combine(tempExtractDir, Path.GetFileName(normalizedExtracted));
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                if (IsSameVolume(normalizedExtracted, target))
                {
                    Directory.Move(normalizedExtracted, target);
                }
                else
                {
                    CopyDirectory(normalizedExtracted, target);
                }

                _logger?.Info($"Normalized extracted content into temp extract folder: '{target}'.");
                return target;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to normalize extracted content into temp folder. Using original path. {ex.Message}");
                return extractedPath;
            }
        }

        /// <summary>
        /// Determines whether two paths are on the same volume.
        /// </summary>
        private static bool IsSameVolume(string source, string destination)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            {
                return false;
            }

            var sourceRoot = Path.GetPathRoot(source.Trim());
            var destinationRoot = Path.GetPathRoot(destination.Trim());
            return string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Installs the base game content, handling installers and portable builds.
        /// </summary>
        private async Task<WindowsInstallResult> InstallBaseAsync(
            string baseRoot,
            string installDir,
            PlatformMapping mapping,
            string gameName,
            bool skipAlreadyInstalled,
            CancellationToken cancellationToken,
            InstallType? archiveInstallType = null,
            IProgress<double> installProgress = null)
        {
            if (!skipAlreadyInstalled && IsAlreadyInstalled(installDir, gameName, out var alreadyInstalledReason))
            {
                if (!string.IsNullOrWhiteSpace(alreadyInstalledReason))
                {
                    _logger?.Info($"Install skipped: {alreadyInstalledReason}");
                }
                return WindowsInstallResult.Failed("This game is already installed.");
            }

            var installType = _classifier.DetectInstallType(baseRoot, baseRoot);
            if (archiveInstallType == InstallType.Installer && installType != InstallType.Installer)
            {
                _logger?.Info("Archive detection marked installer; overriding extracted classification to Installer.");
                installType = InstallType.Installer;
            }
            _logger?.Info($"Base install classification: {installType}.");

            if (installType == InstallType.Installer)
            {
                return await RunInstallerAsync(baseRoot, installDir, mapping, gameName, cancellationToken, installProgress).ConfigureAwait(false);
            }

            baseRoot = FlattenPortableRootIfNested(baseRoot, installDir, gameName);
            var resolution = _executableResolver.Resolve(baseRoot, gameName, RootFolders);
            if (!resolution.Success)
            {
                var relocated = TryRelocatePortableGameFolder(baseRoot, installDir, mapping, gameName);
                if (!relocated.Success)
                {
                    return WindowsInstallResult.Failed(relocated.Message ?? resolution.Message ?? "Executable resolution failed.");
                }

                resolution = _executableResolver.Resolve(relocated.GameFilesPath, gameName, RootFolders);
                if (!resolution.Success)
                {
                    return WindowsInstallResult.Failed(resolution.Message ?? "Executable resolution failed after relocating game folder.");
                }

                baseRoot = relocated.BaseRootPath;
                _logger?.Info($"Portable base root updated after relocation: '{baseRoot}'.");
            }

            if (resolution.RequiresConfirmation)
            {
                var selection = SelectExecutableCandidate(gameName, installDir, resolution, mapping);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath, resolution.Arguments, installType);
        }

        /// <summary>
        /// Runs an installer and resolves the resulting executable.
        /// </summary>
        private async Task<WindowsInstallResult> RunInstallerAsync(
            string extractedPath,
            string installDir,
            PlatformMapping mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<double> installProgress = null)
        {
            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                var innoCandidate = FindInnoInstallerInRoot(extractedPath);
                if (string.IsNullOrWhiteSpace(innoCandidate))
                {
                    return WindowsInstallResult.Failed("Installer setup.exe not found.");
                }

                setupPath = innoCandidate;
                _logger?.Info($"Using Inno installer in extracted root: {setupPath}");
            }

            var installerMode = mapping?.InstallerMode ?? InstallerMode.Manual;
            var targetInstallDir = Path.Combine(installDir, NormalizeGameFolderName(gameName, "Game"));
            ValidateTargetInstallDir(installDir, targetInstallDir, gameName);
            var wasEmpty = IsDirectoryEmpty(targetInstallDir);
            Directory.CreateDirectory(targetInstallDir);
            var inno = IsInnoInstallerCached(setupPath) || _classifier.IsInnoInstaller(extractedPath);
            var innoLogPath = inno ? BuildInnoLogPath(installDir, gameName) : string.Empty;

            if (installerMode == InstallerMode.AutoInnoSilent && inno)
            {
                var args = string.IsNullOrWhiteSpace(mapping?.InstallerSilentArgs)
                    ? "/SILENT /SUPPRESSMSGBOXES /NORESTART"
                    : mapping.InstallerSilentArgs;
                var logArg = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" /LOG=\"{innoLogPath}\"";
                var command = $"{args} /DIR=\"{targetInstallDir}\"{logArg}";
                _logger?.Info($"Launching installer (auto silent): {setupPath} {command}");
                await ProcessRunner.RunAsync(setupPath, command, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var logArg = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" /LOG=\"{innoLogPath}\"";
                var command = inno ? $"/DIR=\"{targetInstallDir}\"{logArg}" : string.Empty;
                _logger?.Info($"Launching installer manually: {setupPath} {command}".Trim());
                await ProcessRunner.RunAsync(setupPath, command, cancellationToken, useShellExecute: true).ConfigureAwait(false);
            }

            var success = ConfirmInstallerSuccess(targetInstallDir, wasEmpty, innoLogPath, null);
            var resolution = _executableResolver.Resolve(targetInstallDir, gameName, RootFolders);
            if (!success)
            {
                var recovered = TryRecoverInstallerResult(installDir, resolution);
                if (recovered.Success)
                {
                    return recovered;
                }

                var logHint = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" See installer log: {innoLogPath}";
                return WindowsInstallResult.Failed($"Installer did not complete successfully.{logHint}");
            }

            if (!resolution.Success)
            {
                return WindowsInstallResult.Failed(resolution.Message ?? "Executable resolution failed after install.");
            }

            if (resolution.RequiresConfirmation)
            {
                var selection = SelectExecutableCandidate(gameName, targetInstallDir, resolution, mapping);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath, resolution.Arguments, InstallType.Installer);
        }

        private async Task<WindowsInstallResult> TryRunCombinedInstallerBatchAsync(
            string extractedPath,
            string updateRoot,
            string dlcRoot,
            string installDir,
            PlatformMapping mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<double> installProgress = null)
        {
            var setupPath = ResolveInstallerSetupPath(extractedPath);
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return await InstallBaseAsync(extractedPath, installDir, mapping, gameName, skipAlreadyInstalled: true, cancellationToken, InstallType.Installer, installProgress)
                    .ConfigureAwait(false);
            }

            var isBaseInno = IsInnoInstallerCached(setupPath) || _classifier.IsInnoInstaller(extractedPath);
            if (!isBaseInno)
            {
                return await InstallBaseAsync(extractedPath, installDir, mapping, gameName, skipAlreadyInstalled: true, cancellationToken, InstallType.Installer, installProgress)
                    .ConfigureAwait(false);
            }

            var updateInstallers = CollectAutoSilentInstallers(updateRoot);
            var dlcInstallers = CollectAutoSilentInstallers(dlcRoot);
            if (updateInstallers == null || dlcInstallers == null)
            {
                return await InstallBaseAsync(extractedPath, installDir, mapping, gameName, skipAlreadyInstalled: true, cancellationToken, InstallType.Installer, installProgress)
                    .ConfigureAwait(false);
            }

            var targetInstallDir = ResolveGameInstallDir(installDir, gameName);
            ValidateTargetInstallDir(installDir, targetInstallDir, gameName);
            var wasEmpty = IsDirectoryEmpty(targetInstallDir);
            Directory.CreateDirectory(targetInstallDir);

            var installers = new List<(string Label, string Path)>
            {
                ("Base", setupPath)
            };

            installers.AddRange(updateInstallers.Select(path => ("Update", path)));
            installers.AddRange(dlcInstallers.Select(path => ("DLC", path)));

            var argumentList = BuildArgumentList(targetInstallDir, mapping?.InstallerSilentArgs);
            var innoLogPath = BuildInnoLogPath(installDir, gameName);
            if (!string.IsNullOrWhiteSpace(innoLogPath))
            {
                argumentList.Add($"/LOG=\"{innoLogPath}\"");
                _logger?.Info($"Installer batch log configured: {innoLogPath}");
            }

            _logger?.Info($"Installer batch starting for '{gameName}'. TargetDir='{targetInstallDir}'. Args='{string.Join(" ", argumentList)}'.");
            foreach (var installer in installers)
            {
                _logger?.Info($"Installing {installer.Label}: {installer.Path}");
            }

            int? batchExitCode = null;
            try
            {
                batchExitCode = await ProcessRunner.RunElevatedBatchAsync(installers, argumentList, cancellationToken, _logger, innoLogPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var logHint = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" Installer log: {innoLogPath}";
                _logger?.Warning($"Installer batch execution failed: {ex.Message}.{logHint}");
            }

            var success = ConfirmInstallerSuccess(targetInstallDir, wasEmpty, innoLogPath, batchExitCode);
            if (success && batchExitCode.HasValue && batchExitCode.Value != 0)
            {
                batchExitCode = 0;
            }
            var resolution = _executableResolver.Resolve(targetInstallDir, gameName, RootFolders);
            if (!success)
            {
                var recovered = TryRecoverInstallerResult(installDir, resolution);
                if (recovered.Success)
                {
                    return recovered;
                }

                return WindowsInstallResult.Failed("Installer batch did not complete successfully.");
            }

            if (!resolution.Success)
            {
                return WindowsInstallResult.Failed(resolution.Message ?? "Executable resolution failed after install batch.");
            }

            if (resolution.RequiresConfirmation)
            {
                var selection = SelectExecutableCandidate(gameName, targetInstallDir, resolution, mapping);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath, resolution.Arguments, InstallType.Installer);
        }

        private string ResolveInstallerSetupPath(string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return null;
            }

            var setupPath = Directory.EnumerateFiles(extractedPath, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(setupPath))
            {
                return setupPath;
            }

            return FindInnoInstallerInRoot(extractedPath);
        }

        private IReadOnlyList<string> CollectAutoSilentInstallers(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var setups = Directory.EnumerateFiles(root, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(path => path)
                .ToList();
            if (setups.Count == 0)
            {
                setups = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                    .Where(path => IsInnoInstallerCached(path))
                    .OrderBy(path => path)
                    .ToList();
            }

            if (setups.Count == 0)
            {
                return Array.Empty<string>();
            }

            var rootIsInno = _classifier.IsInnoInstaller(root);
            foreach (var setup in setups)
            {
                var isInno = IsInnoInstallerCached(setup) || rootIsInno;
                if (!isInno)
                {
                    return null;
                }
            }

            return setups;
        }

        private static List<string> TokenizeArguments(string commandLine)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return result;
            }

            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            foreach (var ch in commandLine)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    current.Append(ch);
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        private static List<string> BuildArgumentList(string targetInstallDir, string silentArgs = null)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(silentArgs))
            {
                args.AddRange(new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART" });
            }
            else
            {
                args.AddRange(TokenizeArguments(silentArgs));
            }

            args.Add($"/DIR=\"{targetInstallDir}\"");
            return args;
        }

        internal static string NormalizeGameFolderNameInternal(string gameName, string fallback)
        {
            return NormalizeGameFolderName(gameName, fallback);
        }

        private static string BuildInnoLogPath(string installDir, string gameName)
        {
            try
            {
                var logRoot = Path.Combine(Paths.PluginPaths.GetPluginRootDirectory(), "temp", "install", "installer-logs");
                Directory.CreateDirectory(logRoot);
                var safeName = NormalizeGameFolderName(gameName, "Game");
                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                var fileName = $"{safeName}-{timestamp}.log";
                return Path.Combine(logRoot, fileName);
            }
            catch
            {
                return string.Empty;
            }
        }


        private bool IsInnoInstallerCached(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return false;
            }

            if (_innoSignatureCache.TryGetValue(exePath, out var cached))
            {
                return cached;
            }

            var detected = _classifier.IsInnoInstallerExe(exePath);
            _innoSignatureCache[exePath] = detected;
            return detected;
        }

        /// <summary>
        /// Finds an Inno Setup installer in the extracted root.
        /// </summary>
        private string FindInnoInstallerInRoot(string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return null;
            }

            var candidates = Directory.EnumerateFiles(extractedPath, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path.Length)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (IsInnoInstallerCached(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Installs update or DLC content if available.
        /// </summary>
        private async Task InstallUpdateAndDlcAsync(string root, string installDir, PlatformMapping mapping, string gameName, string label, CancellationToken cancellationToken, IProgress<double> installProgress = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    _logger?.Info($"{label} install skipped: source missing. Root='{root ?? "<empty>"}'.");
                    return;
                }

                var targetInstallDir = ResolveGameInstallDir(installDir, gameName);
                ValidateTargetInstallDir(installDir, targetInstallDir, gameName);
                _logger?.Info($"{label} install starting. Root='{root}'. InstallDir='{installDir}'. TargetGameDir='{targetInstallDir}'.");
                var setups = Directory.EnumerateFiles(root, "setup.exe", SearchOption.AllDirectories)
                    .OrderBy(path => path)
                    .ToList();
                if (setups.Count == 0)
                {
                    var innoCandidates = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                        .Where(path => IsInnoInstallerCached(path))
                        .OrderBy(path => path)
                        .ToList();
                    if (innoCandidates.Count == 0)
                    {
                        _logger?.Warning($"{label} install skipped: no setup.exe or Inno installers found under '{root}'.");
                    }
                    else
                    {
                        setups = innoCandidates;
                        var setupList = string.Join("; ", setups);
                        _logger?.Info($"{label} Inno installers detected ({setups.Count}): {setupList}");
                    }
                }
                else
                {
                    var setupList = string.Join("; ", setups);
                    _logger?.Info($"{label} installers detected ({setups.Count}): {setupList}");
                }
                var autoSilent = mapping?.InstallerMode == InstallerMode.AutoInnoSilent;
                var rootIsInno = _classifier.IsInnoInstaller(root);
                var innoSetups = setups
                    .Where(path => IsInnoInstallerCached(path) || rootIsInno)
                    .ToList();

                if (setups.Count > 1)
                {
                    var useSilentArgs = autoSilent && innoSetups.Count == setups.Count;
                    var argumentList = useSilentArgs
                        ? BuildArgumentList(targetInstallDir, mapping?.InstallerSilentArgs)
                        : new List<string>();
                    var innoLogPath = useSilentArgs ? BuildInnoLogPath(installDir, gameName) : string.Empty;
                    if (useSilentArgs && !string.IsNullOrWhiteSpace(innoLogPath))
                    {
                        argumentList.Add($"/LOG=\"{innoLogPath}\"");
                        _logger?.Info($"{label} installer log configured: {innoLogPath}");
                    }
                    _logger?.Info($"{label} running {setups.Count} installers in elevated helper batch to avoid repeated UAC prompts. Args='{string.Join(" ", argumentList)}'.");
                    var labeledSetups = setups.Select(path => (Label: label, Path: path));
                    int? batchExitCode = null;
                    try
                    {
                        batchExitCode = await ProcessRunner.RunElevatedBatchAsync(labeledSetups, argumentList, cancellationToken, _logger, innoLogPath).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var logHint = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" Installer log: {innoLogPath}";
                        _logger?.Warning($"{label} installer batch failed: {ex.Message}.{logHint}");
                        throw;
                    }
                    var confirmed = ConfirmInstallerSuccess(targetInstallDir, false, innoLogPath, batchExitCode);
                    if (confirmed && batchExitCode.HasValue && batchExitCode.Value != 0)
                    {
                        batchExitCode = 0;
                    }
                    if (!confirmed)
                    {
                        _logger?.Warning($"{label} installers did not complete successfully.");
                    }
                    else
                    {
                        _logger?.Info($"{label} installers completed successfully. Installed to '{targetInstallDir}'.");
                    }

                    _logger?.Info($"{label} install completed. Root='{root}'.");
                    return;
                }

                foreach (var setup in setups)
                {
                    if (!File.Exists(setup))
                    {
                        _logger?.Warning($"{label} installer missing on disk; skipping: {setup}");
                        continue;
                    }

                    _logger?.Info($"Running {label} installer: {setup}");
                    var isInno = IsInnoInstallerCached(setup) || rootIsInno;
                    var args = autoSilent && isInno
                        ? $"{(string.IsNullOrWhiteSpace(mapping?.InstallerSilentArgs) ? "/SILENT /SUPPRESSMSGBOXES /NORESTART" : mapping.InstallerSilentArgs)} /DIR=\"{targetInstallDir}\""
                        : string.Empty;
                    try
                    {
                        await ProcessRunner.RunAsync(setup, args, cancellationToken, useShellExecute: args.Length == 0).ConfigureAwait(false);
                        var confirmed = ConfirmInstallerSuccess(targetInstallDir, false, string.Empty, null);
                        if (!confirmed)
                        {
                            _logger?.Warning($"{label} installer did not complete successfully: {setup}");
                        }
                        else
                        {
                            _logger?.Info($"{label} installer completed successfully: {setup}. Installed to '{targetInstallDir}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"{label} installer failed: {setup}. {ex.Message}");
                    }
                }

                _logger?.Info($"{label} install completed. Root='{root}'.");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"{label} install encountered an error: {ex.Message}");
            }
        }

        /// <summary>
        /// Installs optional content such as OST, bonus, or prerequisites.
        /// </summary>
        private async Task InstallOptionalContentAsync(
            string root,
            string installDir,
            PlatformMapping mapping,
            string targetRoot,
            bool enabled,
            string gameName,
            string label,
            CancellationToken cancellationToken,
            bool deleteSource = false,
            bool perGame = true)
        {
            try
            {
                if (!enabled || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    if (!enabled)
                    {
                        _logger?.Debug($"{label} install skipped: disabled.");
                    }
                    else
                    {
                        _logger?.Debug($"{label} install skipped: source missing. Root='{root ?? "<empty>"}'.");
                    }
                    return;
                }

                var resolvedTargetRoot = ResolveOptionalContentRoot(installDir, mapping, targetRoot, label, perGame);
                if (string.IsNullOrWhiteSpace(resolvedTargetRoot))
                {
                    _logger?.Warning($"{label} install skipped: target root not configured.");
                    return;
                }

                var destination = perGame
                    ? Path.Combine(resolvedTargetRoot, NormalizeGameFolderName(gameName, "Game"))
                    : resolvedTargetRoot;
                Directory.CreateDirectory(destination);

                foreach (var entry in Directory.EnumerateFileSystemEntries(root))
                {
                    if (Directory.Exists(entry))
                    {
                        var destDir = Path.Combine(destination, Path.GetFileName(entry));
                        CopyDirectory(entry, destDir);
                    }
                    else
                    {
                        var destFile = Path.Combine(destination, Path.GetFileName(entry));
                        File.Copy(entry, destFile, overwrite: true);
                    }
                }

                await Task.CompletedTask.ConfigureAwait(false);
                _logger?.Info($"{label} content installed to '{destination}'.");

                if (deleteSource)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                        _logger?.Info($"{label} source '{root}' removed after install.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to remove {label} source '{root}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"{label} install encountered an error: {ex.Message}");
            }
        }

        private string ResolveOptionalContentRoot(string installDir, PlatformMapping mapping, string targetRoot, string label, bool perGame)
        {
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return targetRoot;
            }

            if (mapping == null)
            {
                return targetRoot;
            }

            if (label.Equals("Pre-Reqs", StringComparison.OrdinalIgnoreCase))
            {
                return targetRoot;
            }

            var location = label.Equals("OST", StringComparison.OrdinalIgnoreCase)
                ? mapping.OstInstallLocation
                : mapping.BonusInstallLocation;

            if (location == OptionalContentLocation.GameFolder)
            {
                return Path.Combine(installDir, label == "OST" ? "Soundtracks" : "Bonus Content");
            }

            return targetRoot;
        }

        /// <summary>
        /// Attempts to relocate a portable game folder into the install directory.
        /// </summary>
        private (bool Success, string GameFilesPath, string BaseRootPath, string Message) TryRelocatePortableGameFolder(string baseRoot, string installDir, PlatformMapping mapping, string gameName)
        {
            if (string.IsNullOrWhiteSpace(baseRoot) || string.IsNullOrWhiteSpace(installDir))
            {
                return (false, string.Empty, baseRoot ?? string.Empty, "Portable relocation failed: invalid paths.");
            }

            var safeGameName = NormalizeGameFolderName(gameName, Path.GetFileName(baseRoot));
            var gameFolder = Directory.EnumerateDirectories(baseRoot)
                .FirstOrDefault(dir => string.Equals(Path.GetFileName(dir), safeGameName, StringComparison.OrdinalIgnoreCase))
                ?? Directory.EnumerateDirectories(baseRoot)
                    .FirstOrDefault(dir => !IsReservedRoot(Path.GetFileName(dir)));

            if (string.IsNullOrWhiteSpace(gameFolder))
            {
                return (false, string.Empty, baseRoot, "Portable relocation failed: game folder not found.");
            }

            if (!string.IsNullOrWhiteSpace(gameFolder)
                && string.Equals(gameFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), baseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return (true, baseRoot, baseRoot, string.Empty);
            }

            var targetGameRoot = Path.Combine(installDir, safeGameName);
            var gameFilesPath = targetGameRoot;
            _logger?.Info($"Relocating portable game content from '{gameFolder}' to '{gameFilesPath}'.");
            CopyDirectory(gameFolder, gameFilesPath);
            try
            {
                Directory.Delete(gameFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to remove portable source '{gameFolder}': {ex.Message}");
            }

            return (true, gameFilesPath, targetGameRoot, string.Empty);
        }

        /// <summary>
        /// Flattens nested portable roots when the archive already contains a game-named folder.
        /// </summary>
        private string FlattenPortableRootIfNested(string baseRoot, string installDir, string gameName)
        {
            if (string.IsNullOrWhiteSpace(baseRoot) || !Directory.Exists(baseRoot))
            {
                return baseRoot;
            }

            var safeGameName = NormalizeGameFolderName(gameName, Path.GetFileName(baseRoot));
            var targetRoot = Path.Combine(installDir, safeGameName);
            if (string.Equals(baseRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                var nestedRoot = Path.Combine(baseRoot, safeGameName);
                if (Directory.Exists(nestedRoot))
                {
                    _logger?.Info($"Flattening nested portable root '{nestedRoot}' into '{baseRoot}'.");
                    foreach (var entry in Directory.EnumerateFileSystemEntries(nestedRoot))
                    {
                        var name = Path.GetFileName(entry);
                        var destination = Path.Combine(baseRoot, name);
                        if (Directory.Exists(entry))
                        {
                            if (Directory.Exists(destination))
                            {
                                Directory.Delete(destination, recursive: true);
                            }
                            Directory.Move(entry, destination);
                        }
                        else
                        {
                            if (File.Exists(destination))
                            {
                                File.Delete(destination);
                            }
                            File.Move(entry, destination);
                        }
                    }

                    try
                    {
                        Directory.Delete(nestedRoot, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"Nested portable root cleanup skipped: {ex.Message}");
                    }
                }
            }

            return baseRoot;
        }

        /// <summary>
        /// Resolves the temp root from the archive or extracted path if it lives under plugin temp.
        /// </summary>
        private static string ResolveTempRoot(string archivePath, string extractedPath)
        {
            var candidate = !string.IsNullOrWhiteSpace(extractedPath)
                ? extractedPath
                : archivePath;

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            try
            {
                var filePath = File.Exists(candidate) ? candidate : null;
                var directoryPath = Directory.Exists(candidate) ? candidate : null;
                var nameSource = filePath ?? directoryPath;
                if (string.IsNullOrWhiteSpace(nameSource))
                {
                    return null;
                }

                var pluginRoot = Paths.PluginPaths.GetPluginRootDirectory();
                var tempRoot = Path.Combine(pluginRoot, "temp");
                var fullPath = Path.GetFullPath(nameSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!fullPath.StartsWith(Path.GetFullPath(tempRoot), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var current = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
                while (!string.IsNullOrWhiteSpace(current))
                {
                    var folderName = Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.Equals(folderName, "downloads", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(folderName, "extracted", StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetDirectoryName(current);
                    }

                    current = Path.GetDirectoryName(current);
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Copies a directory recursively.
        /// </summary>
        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, overwrite: true);
            }
        }

        /// <summary>
        /// Confirms installer success by checking for files and prompting the user if needed.
        /// </summary>
        private bool ConfirmInstallerSuccess(string installDir, bool wasEmpty, string installerLogPath, int? exitCode)
        {
            if (WasInstallerLogSuccessful(installerLogPath))
            {
                if (exitCode.HasValue && exitCode.Value != 0)
                {
                    var detail = ProcessRunner.MapNtStatusCode(exitCode.Value);
                    _logger?.Warning($"Installer log indicates success despite exit code {exitCode.Value} ({detail}).");
                }
                else if (exitCode.HasValue)
                {
                    _logger?.Info($"Installer log indicates success (exit code {exitCode.Value}).");
                }
                else
                {
                    _logger?.Info("Installer log indicates success.");
                }
                return true;
            }

            if (exitCode.HasValue && exitCode.Value != 0)
            {
                var detail = ProcessRunner.MapNtStatusCode(exitCode.Value);
                _logger?.Warning($"Installer exited with code {exitCode.Value} ({detail}) and no success indicator was found in the installer log.");
            }

            if (IsProgramRegisteredWithInstallLocation(installDir))
            {
                _logger?.Info("Install location found in installed programs list.");
                return true;
            }

            var hasFiles = Directory.Exists(installDir) && Directory.EnumerateFileSystemEntries(installDir).Any();
            if (!hasFiles)
            {
                return false;
            }

            if (wasEmpty)
            {
                return true;
            }

            return AskUserConfirmation("Installer Confirmation", "Did the installer complete successfully?", installDir);
        }

        private bool WasInstallerLogSuccessful(string installerLogPath)
        {
            if (string.IsNullOrWhiteSpace(installerLogPath))
            {
                return false;
            }

            try
            {
                if (!File.Exists(installerLogPath))
                {
                    return false;
                }

                foreach (var line in File.ReadLines(installerLogPath))
                {
                    if (line.IndexOf("Installation process succeeded", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to read installer log '{installerLogPath}': {ex.Message}");
            }

            return false;
        }

        private bool IsProgramRegisteredWithInstallLocation(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return false;
            }

            try
            {
                var normalizedInstallDir = Path.GetFullPath(installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    + Path.DirectorySeparatorChar;
                return IsProgramRegisteredWithInstallLocation(Microsoft.Win32.Registry.LocalMachine, normalizedInstallDir)
                    || IsProgramRegisteredWithInstallLocation(Microsoft.Win32.Registry.CurrentUser, normalizedInstallDir);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to query installed programs list: {ex.Message}");
                return false;
            }
        }

        private bool IsProgramRegisteredWithInstallLocation(Microsoft.Win32.RegistryKey root, string normalizedInstallDir)
        {
            return IsProgramRegisteredWithInstallLocation(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", normalizedInstallDir)
                || IsProgramRegisteredWithInstallLocation(root, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", normalizedInstallDir);
        }

        private bool IsProgramRegisteredWithInstallLocation(Microsoft.Win32.RegistryKey root, string subKeyPath, string normalizedInstallDir)
        {
            using var uninstallKey = root.OpenSubKey(subKeyPath);
            if (uninstallKey == null)
            {
                return false;
            }

            foreach (var name in uninstallKey.GetSubKeyNames())
            {
                using var entryKey = uninstallKey.OpenSubKey(name);
                if (entryKey == null)
                {
                    continue;
                }

                var installLocation = entryKey.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(installLocation))
                {
                    continue;
                }

                var normalizedLocation = Path.GetFullPath(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    + Path.DirectorySeparatorChar;
                if (normalizedLocation.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Prompts the user for confirmation using a modal dialog.
        /// </summary>
        private bool AskUserConfirmation(string title, string message, string detail)
        {
            _logger?.Info($"User confirmation requested: {message} ({detail}).");
            if (Application.Current == null)
            {
                var result = MessageBox.Show($"{message}\n{detail}", title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                return result == MessageBoxResult.Yes;
            }

            return Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new RomMbox.UI.Views.ConfirmDialog(title, message, detail)
                {
                    Owner = Application.Current.MainWindow,
                    Topmost = true
                };
                dialog.ShowActivated = true;
                dialog.Activate();
                var result = dialog.ShowDialog();
                return result == true;
            });
        }

        private (bool Confirmed, string SelectedPath) SelectExecutableCandidate(
            string gameName,
            string installRoot,
            IReadOnlyList<string> candidates,
            PlatformMapping mapping)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return (false, null);
            }

            if (candidates.Count == 1)
            {
                return (true, candidates[0]);
            }

            var recommended = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?? candidates[0];

            var rows = BuildExecutableCandidateRows(installRoot, candidates, recommended).ToList();
            var recommendedRow = rows.FirstOrDefault(row => string.Equals(row.FullPath, recommended, StringComparison.OrdinalIgnoreCase))
                ?? rows.FirstOrDefault();

            _logger?.Info($"Executable selection required for '{gameName}'. Recommended='{recommended}'. Candidates=[{string.Join(", ", candidates)}]");

            if (Application.Current == null)
            {
                return (AskUserConfirmation("Executable Selection", "Multiple executable candidates detected. Use selected executable?", recommended), recommended);
            }

            return Application.Current.Dispatcher.Invoke(() =>
            {
                var viewModel = new RomMbox.UI.ViewModels.ExecutableSelectionViewModel(
                    "Executable Selection",
                    "Multiple executables were detected. Choose the executable to launch.",
                    rows,
                    recommendedRow);

                var dialog = new RomMbox.UI.Views.ExecutableSelectionDialog
                {
                    Owner = Application.Current.MainWindow,
                    Topmost = true,
                    DataContext = viewModel
                };

                viewModel.RequestClose += confirmed =>
                {
                    dialog.DialogResult = confirmed;
                    dialog.Close();
                };

                dialog.ShowActivated = true;
                dialog.Activate();
                var result = dialog.ShowDialog();
                var selected = viewModel.SelectedCandidate?.FullPath ?? recommendedRow?.FullPath ?? recommended;
                return (result == true, selected);
            });
        }

        private IEnumerable<RomMbox.UI.Models.ExecutableCandidateRow> BuildExecutableCandidateRows(
            string installRoot,
            IReadOnlyList<string> candidates,
            string recommended)
        {
            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var fileName = Path.GetFileName(path) ?? path;
                var displayPath = path;
                try
                {
                    if (!string.IsNullOrWhiteSpace(installRoot))
                    {
                        var relative = Path.GetRelativePath(installRoot, path);
                        displayPath = relative.StartsWith("..", StringComparison.OrdinalIgnoreCase) ? path : relative;
                    }
                }
                catch
                {
                    displayPath = path;
                }

                long fileSize = 0;
                string fileSizeDisplay = string.Empty;
                string version = string.Empty;
                DateTime lastModified = DateTime.MinValue;
                string lastModifiedDisplay = string.Empty;
                var architecture = ExecutableArchitectureDetector.GetArchitecture(path);
                var architectureDisplay = ExecutableArchitectureDetector.GetDisplayName(architecture);
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        fileSize = info.Length;
                        fileSizeDisplay = FormatFileSize(fileSize);
                        lastModified = info.LastWriteTime;
                        lastModifiedDisplay = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                }
                catch
                {
                    fileSizeDisplay = string.Empty;
                    lastModifiedDisplay = string.Empty;
                }

                try
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                    version = string.IsNullOrWhiteSpace(versionInfo.FileVersion)
                        ? versionInfo.ProductVersion ?? string.Empty
                        : versionInfo.FileVersion;
                }
                catch
                {
                    version = string.Empty;
                }

                yield return new RomMbox.UI.Models.ExecutableCandidateRow
                {
                    IsRecommended = string.Equals(path, recommended, StringComparison.OrdinalIgnoreCase),
                    FileName = fileName,
                    FullPath = path,
                    DisplayPath = displayPath,
                    FileSizeBytes = fileSize,
                    FileSizeDisplay = fileSizeDisplay,
                    Version = version,
                    LastModified = lastModified,
                    LastModifiedDisplay = lastModifiedDisplay,
                    ArchitectureValue = architecture,
                    Architecture = architectureDisplay
                };
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0)
            {
                return string.Empty;
            }

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            var size = (double)bytes;
            var index = 0;
            while (size >= 1024 && index < suffixes.Length - 1)
            {
                size /= 1024;
                index++;
            }

            return $"{size:0.##} {suffixes[index]}";
        }

        /// <summary>
        /// Determines if a game appears to already be installed.
        /// </summary>
        private static bool IsAlreadyInstalled(string installDir, string gameName, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            {
                reason = $"InstallDir missing or not found. InstallDir='{installDir ?? string.Empty}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var safeGameName = NormalizeGameFolderName(gameName, Path.GetFileName(installDir));
                var normalizedInstallDir = installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var installLeaf = Path.GetFileName(normalizedInstallDir);
                if (string.Equals(installLeaf, safeGameName, StringComparison.OrdinalIgnoreCase))
                {
                    var hasEntries = Directory.EnumerateFileSystemEntries(installDir).Any();
                    reason = hasEntries
                        ? $"InstallDir matches game name and contains entries. InstallDir='{installDir}'."
                        : $"InstallDir matches game name but is empty. InstallDir='{installDir}'.";
                    return hasEntries;
                }

                var gameRoot = Path.Combine(installDir, safeGameName);
                if (Directory.Exists(gameRoot))
                {
                    var hasEntries = Directory.EnumerateFileSystemEntries(gameRoot).Any();
                    reason = hasEntries
                        ? $"Game root already exists and contains entries. GameRoot='{gameRoot}'."
                        : $"Game root exists but is empty. GameRoot='{gameRoot}'.";
                    return hasEntries;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if a directory is empty.
        /// </summary>
        private static bool IsDirectoryEmpty(string path)
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        private static string ResolveGameInstallDir(string installDir, string gameName)
        {
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return installDir;
            }

            var safeGameName = NormalizeGameFolderName(gameName, Path.GetFileName(installDir));
            return Path.Combine(installDir, safeGameName);
        }

        private WindowsInstallResult TryRecoverInstallerResult(string installDir, ExecutableResolutionResult resolution)
        {
            if (resolution == null || !resolution.Success)
            {
                return WindowsInstallResult.Failed("Installer did not complete successfully (no executable resolved).");
            }

            if (string.IsNullOrWhiteSpace(resolution.ExecutablePath))
            {
                return WindowsInstallResult.Failed("Installer did not complete successfully (no executable path resolved).");
            }

            var executableDir = Path.GetDirectoryName(resolution.ExecutablePath);
            if (string.IsNullOrWhiteSpace(executableDir))
            {
                return WindowsInstallResult.Failed("Installer did not complete successfully (executable path missing directory).");
            }

            if (!string.IsNullOrWhiteSpace(installDir))
            {
                var normalizedRoot = installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!executableDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return WindowsInstallResult.Failed("Installer did not complete successfully (executable outside install root).");
                }
            }

            _logger?.Warning("Installer confirmation failed, but executable resolution succeeded. Proceeding with resolved executable.");
            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath, resolution.Arguments, InstallType.Installer);
        }

        private (bool Confirmed, string SelectedPath) SelectExecutableCandidate(
            string gameName,
            string installRoot,
            ExecutableResolutionResult resolution,
            PlatformMapping mapping)
        {
            return SelectExecutableCandidate(gameName, installRoot, resolution?.Candidates, mapping);
        }

        private void ValidateTargetInstallDir(string installDir, string targetInstallDir, string gameName)
        {
            if (string.IsNullOrWhiteSpace(installDir) || string.IsNullOrWhiteSpace(targetInstallDir))
            {
                return;
            }

            var expectedLeaf = NormalizeGameFolderName(gameName, Path.GetFileName(installDir));
            var normalizedInstallDir = Path.GetFullPath(installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                + Path.DirectorySeparatorChar;
            var normalizedTarget = Path.GetFullPath(targetInstallDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                + Path.DirectorySeparatorChar;

            if (!normalizedTarget.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Resolved install directory '{targetInstallDir}' is outside of install root '{installDir}'.");
            }

            var targetLeaf = Path.GetFileName(targetInstallDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(targetLeaf, expectedLeaf, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Resolved install directory '{targetInstallDir}' does not match expected game folder '{expectedLeaf}'.");
            }
        }

        /// <summary>
        /// Finds optional content roots (dlc/update/ost/bonus/pre-reqs) under the base root.
        /// </summary>
        private static (string Dlc, string Update, string Ost, string Bonus, string PreReqs) DiscoverContentRoots(string baseRoot)
        {
            string dlc = null;
            string update = null;
            string ost = null;
            string bonus = null;
            string preReqs = null;

            foreach (var entry in Directory.EnumerateDirectories(baseRoot))
            {
                var name = Path.GetFileName(entry).Trim();
                if (string.Equals(name, "dlc", StringComparison.OrdinalIgnoreCase))
                {
                    dlc = entry;
                }
                else if (string.Equals(name, "update", StringComparison.OrdinalIgnoreCase))
                {
                    update = entry;
                }
                else if (string.Equals(name, "ost", StringComparison.OrdinalIgnoreCase))
                {
                    ost = entry;
                }
                else if (string.Equals(name, "bonus", StringComparison.OrdinalIgnoreCase))
                {
                    bonus = entry;
                }
                else if (string.Equals(name, "pre-reqs", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "prereqs", StringComparison.OrdinalIgnoreCase))
                {
                    preReqs = entry;
                }
            }

            return (dlc, update, ost, bonus, preReqs);
        }

        /// <summary>
        /// Determines whether a folder name is reserved for optional content.
        /// </summary>
        private static bool IsReservedRoot(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return false;
            }

            return RootFolders.Contains(folderName.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes a game name into a safe folder name.
        /// </summary>
        private static string NormalizeGameFolderName(string gameName, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(gameName) ? fallback : gameName;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Game";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Game" : cleaned.Trim();
        }
    }

    /// <summary>
    /// Represents the result of a Windows install operation.
    /// </summary>
    internal sealed class WindowsInstallResult
    {
        /// <summary>
        /// Gets whether the install succeeded.
        /// </summary>
        public bool Success { get; private set; }
        /// <summary>
        /// Gets the resolved executable path.
        /// </summary>
        public string ExecutablePath { get; private set; }
        /// <summary>
        /// Gets any arguments needed to launch the executable.
        /// </summary>
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();
        /// <summary>
        /// Gets a message describing the result or failure.
        /// </summary>
        public string Message { get; private set; }
        /// <summary>
        /// Gets the detected install type, if available.
        /// </summary>
        public InstallType? InstallType { get; private set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="executablePath">The executable path.</param>
        /// <param name="args">Arguments to pass.</param>
        /// <param name="installType">Detected install type.</param>
        /// <returns>The result.</returns>
        public static WindowsInstallResult CreateSuccess(string executablePath, IReadOnlyList<string> args, InstallType? installType = null)
        {
            return new WindowsInstallResult
            {
                Success = true,
                ExecutablePath = executablePath,
                Arguments = args ?? Array.Empty<string>(),
                InstallType = installType
            };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        /// <param name="message">Failure message.</param>
        /// <returns>The result.</returns>
        public static WindowsInstallResult Failed(string message)
        {
            return new WindowsInstallResult
            {
                Success = false,
                Message = message
            };
        }
    }

    /// <summary>
    /// Runs external processes with optional shell execution.
    /// </summary>
    internal static class ProcessRunner
    {
        /// <summary>
        /// Runs a process asynchronously and waits for exit.
        /// </summary>
        /// <param name="fileName">Executable to run.</param>
        /// <param name="arguments">Arguments to pass.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="useShellExecute">Whether to use shell execution.</param>
        public static async Task RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken,
            bool useShellExecute = false)
        {
            await Task.Run(async () =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = useShellExecute,
                    CreateNoWindow = !useShellExecute,
                    RedirectStandardOutput = !useShellExecute,
                    RedirectStandardError = !useShellExecute
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start installer process.");
                }

                if (!useShellExecute)
                {
                    _ = process.StandardOutput.ReadToEnd();
                    _ = process.StandardError.ReadToEnd();
                }

                process.WaitForExit();
            }, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<int> RunElevatedBatchAsync(
            IEnumerable<(string Label, string Path)> installers,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            LoggingService logger = null,
            string installerLogPath = null)
        {
            if (installers == null)
            {
                throw new ArgumentNullException(nameof(installers));
            }

            var installerList = installers
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .ToList();
            if (installerList.Count == 0)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var batchRoot = Path.Combine(Paths.PluginPaths.GetPluginRootDirectory(), "temp", "install");
            Directory.CreateDirectory(batchRoot);
            var batchId = Guid.NewGuid().ToString("N");
            var batchLogPath = Path.Combine(batchRoot, $"installer-batch-{batchId}.log");
            var batchScriptPath = Path.Combine(batchRoot, $"installer-batch-{batchId}.ps1");

            var scriptContent = BuildInstallerBatchScript(installerList, arguments, batchLogPath);
            File.WriteAllText(batchScriptPath, scriptContent, System.Text.Encoding.UTF8);

            logger?.Info($"Installer batch script path: {batchScriptPath}");
            logger?.Info($"Installer batch log path: {batchLogPath}");
            if (arguments != null && arguments.Count > 0)
            {
                logger?.Info($"Installer batch argument list: {string.Join(" ", arguments)}");
                foreach (var arg in arguments)
                {
                    logger?.Debug($"Installer batch argument: {arg}");
                }
            }
            foreach (var installer in installerList)
            {
                logger?.Info($"Installer batch item: {installer.Label} -> {installer.Path}");
            }

            var exitCode = await Task.Run(() =>
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{batchScriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start elevated installer batch.");
                }

                process.WaitForExit();
                logger?.Info($"Installer batch process exit code: {process.ExitCode}.");
                try
                {
                    if (File.Exists(batchLogPath))
                    {
                        var lines = File.ReadAllLines(batchLogPath);
                        foreach (var line in lines)
                        {
                            logger?.Info($"Installer batch result: {line}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning($"Failed to read installer batch log '{batchLogPath}': {ex.Message}");
                }
                return process.ExitCode;
            }, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                var detail = MapNtStatusCode(exitCode);
                var logHint = string.IsNullOrWhiteSpace(installerLogPath) ? string.Empty : $" Installer log: {installerLogPath}.";
                logger?.Warning($"Elevated installer batch exit code: {exitCode} ({detail}).{logHint}");
            }

            return exitCode;
        }

        internal static string MapNtStatusCode(int exitCode)
        {
            unchecked
            {
                switch ((uint)exitCode)
                {
                    case 0xC000041D:
                        return "STATUS_FATAL_USER_CALLBACK_EXCEPTION";
                    case 0xC0000005:
                        return "STATUS_ACCESS_VIOLATION";
                    case 0xC0000135:
                        return "STATUS_DLL_NOT_FOUND";
                    case 0xC0000142:
                        return "STATUS_DLL_INIT_FAILED";
                    default:
                        return "UNKNOWN_NTSTATUS";
                }
            }
        }

        private static string BuildInstallerBatchScript(
            IReadOnlyList<(string Label, string Path)> installers,
            IReadOnlyList<string> arguments,
            string batchLogPath)
        {
            var installerEntries = installers
                .Select(installer =>
                {
                    var safePath = EscapePowerShellSingleQuoted(installer.Path);
                    var safeLabel = EscapePowerShellSingleQuoted(installer.Label ?? "Installer");
                    return $"@{{ Label = '{safeLabel}'; Path = '{safePath}' }}";
                });

            var argsList = arguments == null
                ? Array.Empty<string>()
                : arguments.ToArray();
            var safeArgs = argsList.Select(arg => $"'{EscapePowerShellSingleQuoted(arg)}'");
            var argumentsArray = string.Join(", ", safeArgs);

            var safeLogPath = EscapePowerShellSingleQuoted(batchLogPath);
            var installersArray = string.Join(", ", installerEntries);

            var script = $@"$ErrorActionPreference = 'Stop'
$batchExit = 0
$logPath = '{safeLogPath}'
if (Test-Path $logPath) {{ Remove-Item -Path $logPath -Force }}
$arguments = @({argumentsArray})
$installers = @({installersArray})
foreach ($installer in $installers) {{
    if ([string]::IsNullOrWhiteSpace($installer.Path)) {{ continue }}
    Add-Content -Path $logPath -Value (""Installer "" + $installer.Label + "" Starting="" + $installer.Path)
    $p = Start-Process -FilePath $installer.Path -ArgumentList $arguments -Wait -PassThru
    Add-Content -Path $logPath -Value (""Installer "" + $installer.Label + "" Completed ExitCode="" + $p.ExitCode)
    if ($p.ExitCode -ne 0 -and $batchExit -eq 0) {{ $batchExit = $p.ExitCode }}
}}
exit $batchExit
";

            return script;
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }

    }
}
