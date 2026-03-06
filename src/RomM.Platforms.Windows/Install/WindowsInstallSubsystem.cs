using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models.Install;

namespace RomM.Platforms.Windows.Install
{
    internal sealed class WindowsInstallSubsystem
    {
        private static readonly string[] RootFolders = { "dlc", "update", "ost", "bonus", "pre-reqs" };
        private readonly IPlatformLogger? _logger;
        private readonly WindowsInstallClassifier _classifier;
        private readonly ExecutableResolver _executableResolver;
        private readonly Dictionary<string, bool> _innoSignatureCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<ExecutableSelectionRequest, Task<ExecutableSelectionResult>>? _selectExecutableAsync;
        private readonly Func<ConfirmationRequest, Task<bool>>? _confirmAsync;

        public WindowsInstallSubsystem(
            IPlatformLogger? logger,
            Func<ExecutableSelectionRequest, Task<ExecutableSelectionResult>>? selectExecutableAsync = null,
            Func<ConfirmationRequest, Task<bool>>? confirmAsync = null)
        {
            _logger = logger;
            _classifier = new WindowsInstallClassifier(logger);
            _executableResolver = new ExecutableResolver(logger);
            _selectExecutableAsync = selectExecutableAsync;
            _confirmAsync = confirmAsync;
        }

        public async Task<WindowsInstallResult> InstallAsync(
            string? archivePath,
            string? extractedPath,
            string installDir,
            PlatformInstallSettings? mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<double>? installProgress = null,
            string? finalInstallDir = null,
            bool preferFinalInstallDirForInstaller = false)
        {
            if (string.IsNullOrWhiteSpace(archivePath) && string.IsNullOrWhiteSpace(extractedPath))
            {
                return WindowsInstallResult.Failed("Archive path not found.");
            }

            if (string.IsNullOrWhiteSpace(installDir))
            {
                return WindowsInstallResult.Failed("Install directory is required.");
            }

            _logger?.Write(PlatformLogLevel.Info, $"Windows install start. Archive='{archivePath ?? string.Empty}', InstallDir='{installDir}', ExtractedPath='{extractedPath ?? string.Empty}'.");
            Directory.CreateDirectory(installDir);

            var tempRoot = ResolveTempRoot(archivePath, extractedPath)
                ?? Path.Combine(Path.GetTempPath(), "RomM", "install", Guid.NewGuid().ToString("N"));
            var tempExtractDir = Path.Combine(tempRoot, "extracted");
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(tempExtractDir);

            var cleanupTempRoot = false;
            try
            {
                var extractRoot = !string.IsNullOrWhiteSpace(extractedPath) && Directory.Exists(extractedPath)
                    ? EnsureExtractedInTemp(extractedPath, tempExtractDir)
                    : extractedPath ?? string.Empty;

                if (string.IsNullOrWhiteSpace(extractRoot))
                {
                    return WindowsInstallResult.Failed("Extraction failed.");
                }

                installProgress?.Report(0);

                var archiveInstallType = _classifier.DetectInstallType(archivePath, extractRoot);
                _logger?.Write(PlatformLogLevel.Info, $"Windows archive install classification: {archiveInstallType}.");

                var baseRoot = archiveInstallType == InstallType.Installer
                    ? extractRoot
                    : NormalizePortableRoot(extractRoot, installDir, gameName, archivePath);
                _logger?.Write(PlatformLogLevel.Info, $"Windows base root selected: '{baseRoot}'. ArchiveInstallType={archiveInstallType}.");
                var contentRoots = DiscoverContentRoots(baseRoot);
                _logger?.Write(PlatformLogLevel.Info, $"Windows content roots: Base='{baseRoot}', PreReqs='{contentRoots.PreReqs ?? "<none>"}', Bonus='{contentRoots.Bonus ?? "<none>"}', Ost='{contentRoots.Ost ?? "<none>"}', Update='{contentRoots.Update ?? "<none>"}', Dlc='{contentRoots.Dlc ?? "<none>"}'.");

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
                _logger?.Write(PlatformLogLevel.Error, "Windows install failed; leaving temp install root for troubleshooting.", ex);
                throw;
            }
            finally
            {
                try
                {
                    if (cleanupTempRoot && Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                        _logger?.Write(PlatformLogLevel.Info, $"Temp install root cleaned up: '{tempRoot}'.");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Write(PlatformLogLevel.Warning, $"Failed to clean up temp install root '{tempRoot}': {ex.Message}");
                }
            }
        }

        private string NormalizePortableRoot(string baseRoot, string installDir, string gameName, string? archivePath)
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

                _logger?.Write(PlatformLogLevel.Info, $"Relocated extracted folder '{baseRoot}' -> '{targetRoot}'.");
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
                _logger?.Write(PlatformLogLevel.Debug, $"Portable root cleanup skipped: {ex.Message}");
            }

            return targetRoot;
        }

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

                _logger?.Write(PlatformLogLevel.Info, $"Normalized extracted content into temp extract folder: '{target}'.");
                return target;
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, $"Failed to normalize extracted content into temp folder. Using original path. {ex.Message}");
                return extractedPath;
            }
        }

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

        private async Task<WindowsInstallResult> InstallBaseAsync(
            string baseRoot,
            string installDir,
            PlatformInstallSettings? mapping,
            string gameName,
            bool skipAlreadyInstalled,
            CancellationToken cancellationToken,
            InstallType? archiveInstallType = null,
            IProgress<double>? installProgress = null)
        {
            if (!skipAlreadyInstalled && IsAlreadyInstalled(installDir, gameName, out var alreadyInstalledReason))
            {
                if (!string.IsNullOrWhiteSpace(alreadyInstalledReason))
                {
                    _logger?.Write(PlatformLogLevel.Info, $"Install skipped: {alreadyInstalledReason}");
                }
                return WindowsInstallResult.Failed("This game is already installed.");
            }

            var installType = _classifier.DetectInstallType(baseRoot, baseRoot);
            if (archiveInstallType == InstallType.Installer && installType != InstallType.Installer)
            {
                _logger?.Write(PlatformLogLevel.Info, "Archive detection marked installer; overriding extracted classification to Installer.");
                installType = InstallType.Installer;
            }
            _logger?.Write(PlatformLogLevel.Info, $"Base install classification: {installType}.");

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
                _logger?.Write(PlatformLogLevel.Info, $"Portable base root updated after relocation: '{baseRoot}'.");
            }

            if (resolution.RequiresConfirmation)
            {
                var selection = await SelectExecutableCandidateAsync(gameName, baseRoot, resolution).ConfigureAwait(false);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath ?? string.Empty, resolution.Arguments, installType);
        }

        private async Task<WindowsInstallResult> RunInstallerAsync(
            string extractedPath,
            string installDir,
            PlatformInstallSettings? mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<double>? installProgress = null)
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
                _logger?.Write(PlatformLogLevel.Info, $"Using Inno installer in extracted root: {setupPath}");
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
                    : mapping!.InstallerSilentArgs;
                var logArg = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" /LOG=\"{innoLogPath}\"";
                var command = $"{args} /DIR=\"{targetInstallDir}\"{logArg}";
                _logger?.Write(PlatformLogLevel.Info, $"Launching installer (auto silent): {setupPath} {command}");
                await ProcessRunner.RunAsync(setupPath, command, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var logArg = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" /LOG=\"{innoLogPath}\"";
                var command = inno ? $"/DIR=\"{targetInstallDir}\"{logArg}" : string.Empty;
                _logger?.Write(PlatformLogLevel.Info, $"Launching installer manually: {setupPath} {command}".Trim());
                await ProcessRunner.RunAsync(setupPath, command, cancellationToken, useShellExecute: true).ConfigureAwait(false);
            }

            var success = await ConfirmInstallerSuccessAsync(targetInstallDir, wasEmpty, innoLogPath, null).ConfigureAwait(false);
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
                var selection = await SelectExecutableCandidateAsync(gameName, targetInstallDir, resolution).ConfigureAwait(false);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath ?? string.Empty, resolution.Arguments, InstallType.Installer);
        }

        private async Task<WindowsInstallResult> TryRunCombinedInstallerBatchAsync(
            string extractedPath,
            string? updateRoot,
            string? dlcRoot,
            string installDir,
            PlatformInstallSettings? mapping,
            string gameName,
            CancellationToken cancellationToken,
            IProgress<double>? installProgress = null)
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
                _logger?.Write(PlatformLogLevel.Info, $"Installer batch log configured: {innoLogPath}");
            }

            _logger?.Write(PlatformLogLevel.Info, $"Installer batch starting for '{gameName}'. TargetDir='{targetInstallDir}'. Args='{string.Join(" ", argumentList)}'.");
            foreach (var installer in installers)
            {
                _logger?.Write(PlatformLogLevel.Info, $"Installing {installer.Label}: {installer.Path}");
            }

            int? batchExitCode = null;
            try
            {
                batchExitCode = await ProcessRunner.RunElevatedBatchAsync(installers, argumentList, cancellationToken, _logger, innoLogPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var logHint = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" Installer log: {innoLogPath}";
                _logger?.Write(PlatformLogLevel.Warning, $"Installer batch execution failed: {ex.Message}.{logHint}");
            }

            var success = await ConfirmInstallerSuccessAsync(targetInstallDir, wasEmpty, innoLogPath, batchExitCode).ConfigureAwait(false);
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
                var selection = await SelectExecutableCandidateAsync(gameName, targetInstallDir, resolution).ConfigureAwait(false);
                if (!selection.Confirmed)
                {
                    return WindowsInstallResult.Failed("Executable selection not confirmed.");
                }
                resolution = resolution.WithExecutable(selection.SelectedPath);
            }

            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath ?? string.Empty, resolution.Arguments, InstallType.Installer);
        }

        private string? ResolveInstallerSetupPath(string extractedPath)
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

        private IReadOnlyList<string>? CollectAutoSilentInstallers(string? root)
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

        private static List<string> BuildArgumentList(string targetInstallDir, string? silentArgs = null)
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
                var logRoot = Path.Combine(Path.GetTempPath(), "RomM", "install", "installer-logs");
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

        private string? FindInnoInstallerInRoot(string extractedPath)
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

        private async Task InstallUpdateAndDlcAsync(string? root, string installDir, PlatformInstallSettings? mapping, string gameName, string label, CancellationToken cancellationToken, IProgress<double>? installProgress = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    _logger?.Write(PlatformLogLevel.Info, $"{label} install skipped: source missing. Root='{root ?? "<empty>"}'.");
                    return;
                }

                var targetInstallDir = ResolveGameInstallDir(installDir, gameName);
                ValidateTargetInstallDir(installDir, targetInstallDir, gameName);
                _logger?.Write(PlatformLogLevel.Info, $"{label} install starting. Root='{root}'. InstallDir='{installDir}'. TargetGameDir='{targetInstallDir}'.");
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
                        _logger?.Write(PlatformLogLevel.Warning, $"{label} install skipped: no setup.exe or Inno installers found under '{root}'.");
                    }
                    else
                    {
                        setups = innoCandidates;
                        var setupList = string.Join("; ", setups);
                        _logger?.Write(PlatformLogLevel.Info, $"{label} Inno installers detected ({setups.Count}): {setupList}");
                    }
                }
                else
                {
                    var setupList = string.Join("; ", setups);
                    _logger?.Write(PlatformLogLevel.Info, $"{label} installers detected ({setups.Count}): {setupList}");
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
                        _logger?.Write(PlatformLogLevel.Info, $"{label} installer log configured: {innoLogPath}");
                    }
                    _logger?.Write(PlatformLogLevel.Info, $"{label} running {setups.Count} installers in elevated helper batch to avoid repeated UAC prompts. Args='{string.Join(" ", argumentList)}'.");
                    var labeledSetups = setups.Select(path => (Label: label, Path: path));
                    int? batchExitCode = null;
                    try
                    {
                        batchExitCode = await ProcessRunner.RunElevatedBatchAsync(labeledSetups, argumentList, cancellationToken, _logger, innoLogPath).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var logHint = string.IsNullOrWhiteSpace(innoLogPath) ? string.Empty : $" Installer log: {innoLogPath}";
                        _logger?.Write(PlatformLogLevel.Warning, $"{label} installer batch failed: {ex.Message}.{logHint}");
                        throw;
                    }
                    var confirmed = await ConfirmInstallerSuccessAsync(targetInstallDir, false, innoLogPath, batchExitCode).ConfigureAwait(false);
                    if (confirmed && batchExitCode.HasValue && batchExitCode.Value != 0)
                    {
                        batchExitCode = 0;
                    }
                    if (!confirmed)
                    {
                        _logger?.Write(PlatformLogLevel.Warning, $"{label} installers did not complete successfully.");
                    }
                    else
                    {
                        _logger?.Write(PlatformLogLevel.Info, $"{label} installers completed successfully. Installed to '{targetInstallDir}'.");
                    }

                    _logger?.Write(PlatformLogLevel.Info, $"{label} install completed. Root='{root}'.");
                    return;
                }

                foreach (var setup in setups)
                {
                    if (!File.Exists(setup))
                    {
                        _logger?.Write(PlatformLogLevel.Warning, $"{label} installer missing on disk; skipping: {setup}");
                        continue;
                    }

                    _logger?.Write(PlatformLogLevel.Info, $"Running {label} installer: {setup}");
                    var isInno = IsInnoInstallerCached(setup) || rootIsInno;
                    var args = autoSilent && isInno
                        ? $"{(string.IsNullOrWhiteSpace(mapping?.InstallerSilentArgs) ? "/SILENT /SUPPRESSMSGBOXES /NORESTART" : mapping!.InstallerSilentArgs)} /DIR=\"{targetInstallDir}\""
                        : string.Empty;
                    try
                    {
                        await ProcessRunner.RunAsync(setup, args, cancellationToken, useShellExecute: args.Length == 0).ConfigureAwait(false);
                        var confirmed = await ConfirmInstallerSuccessAsync(targetInstallDir, false, string.Empty, null).ConfigureAwait(false);
                        if (!confirmed)
                        {
                            _logger?.Write(PlatformLogLevel.Warning, $"{label} installer did not complete successfully: {setup}");
                        }
                        else
                        {
                            _logger?.Write(PlatformLogLevel.Info, $"{label} installer completed successfully: {setup}. Installed to '{targetInstallDir}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Write(PlatformLogLevel.Warning, $"{label} installer failed: {setup}. {ex.Message}");
                    }
                }

                _logger?.Write(PlatformLogLevel.Info, $"{label} install completed. Root='{root}'.");
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, $"{label} install encountered an error: {ex.Message}");
            }
        }

        private async Task InstallOptionalContentAsync(
            string? root,
            string installDir,
            PlatformInstallSettings? mapping,
            string? targetRoot,
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
                        _logger?.Write(PlatformLogLevel.Debug, $"{label} install skipped: disabled.");
                    }
                    else
                    {
                        _logger?.Write(PlatformLogLevel.Debug, $"{label} install skipped: source missing. Root='{root ?? "<empty>"}'.");
                    }
                    return;
                }

                var resolvedTargetRoot = ResolveOptionalContentRoot(installDir, mapping, targetRoot, label, perGame);
                if (string.IsNullOrWhiteSpace(resolvedTargetRoot))
                {
                    _logger?.Write(PlatformLogLevel.Warning, $"{label} install skipped: target root not configured.");
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
                _logger?.Write(PlatformLogLevel.Info, $"{label} content installed to '{destination}'.");

                if (deleteSource)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                        _logger?.Write(PlatformLogLevel.Info, $"{label} source '{root}' removed after install.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Write(PlatformLogLevel.Warning, $"Failed to remove {label} source '{root}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, $"{label} install encountered an error: {ex.Message}");
            }
        }

        private string? ResolveOptionalContentRoot(string installDir, PlatformInstallSettings? mapping, string? targetRoot, string label, bool perGame)
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

        private (bool Success, string GameFilesPath, string BaseRootPath, string Message) TryRelocatePortableGameFolder(string baseRoot, string installDir, PlatformInstallSettings? mapping, string gameName)
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
            _logger?.Write(PlatformLogLevel.Info, $"Relocating portable game content from '{gameFolder}' to '{gameFilesPath}'.");
            CopyDirectory(gameFolder, gameFilesPath);
            try
            {
                Directory.Delete(gameFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, $"Failed to remove portable source '{gameFolder}': {ex.Message}");
            }

            return (true, gameFilesPath, targetGameRoot, string.Empty);
        }

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
                    _logger?.Write(PlatformLogLevel.Info, $"Flattening nested portable root '{nestedRoot}' into '{baseRoot}'.");
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
                        _logger?.Write(PlatformLogLevel.Debug, $"Nested portable root cleanup skipped: {ex.Message}");
                    }
                }
            }

            return baseRoot;
        }

        private static string? ResolveTempRoot(string? archivePath, string? extractedPath)
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

                var fullPath = Path.GetFullPath(nameSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var tempRoot = Path.GetTempPath();
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

        private async Task<bool> ConfirmInstallerSuccessAsync(string installDir, bool wasEmpty, string installerLogPath, int? exitCode)
        {
            if (WasInstallerLogSuccessful(installerLogPath))
            {
                if (exitCode.HasValue && exitCode.Value != 0)
                {
                    var detail = ProcessRunner.MapNtStatusCode(exitCode.Value);
                    _logger?.Write(PlatformLogLevel.Warning, $"Installer log indicates success despite exit code {exitCode.Value} ({detail}).");
                }
                else if (exitCode.HasValue)
                {
                    _logger?.Write(PlatformLogLevel.Info, $"Installer log indicates success (exit code {exitCode.Value}).");
                }
                else
                {
                    _logger?.Write(PlatformLogLevel.Info, "Installer log indicates success.");
                }
                return true;
            }

            if (exitCode.HasValue && exitCode.Value != 0)
            {
                var detail = ProcessRunner.MapNtStatusCode(exitCode.Value);
                _logger?.Write(PlatformLogLevel.Warning, $"Installer exited with code {exitCode.Value} ({detail}) and no success indicator was found in the installer log.");
            }

            if (IsProgramRegisteredWithInstallLocation(installDir))
            {
                _logger?.Write(PlatformLogLevel.Info, "Install location found in installed programs list.");
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

            if (_confirmAsync == null)
            {
                return false;
            }

            try
            {
                var request = new ConfirmationRequest
                {
                    Title = "Installer Confirmation",
                    Message = "Did the installer complete successfully?",
                    Detail = installDir
                };
                return await _confirmAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Write(PlatformLogLevel.Warning, "Installer confirmation failed.", ex);
                return false;
            }
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
                _logger?.Write(PlatformLogLevel.Warning, $"Failed to read installer log '{installerLogPath}': {ex.Message}");
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
                _logger?.Write(PlatformLogLevel.Warning, $"Failed to query installed programs list: {ex.Message}");
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

                var location = entryKey.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var normalizedLocation = location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (normalizedLocation.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<(bool Confirmed, string SelectedPath)> SelectExecutableCandidateAsync(
            string gameName,
            string installRoot,
            ExecutableResolutionResult resolution)
        {
            return await SelectExecutableCandidateAsync(gameName, installRoot, resolution?.Candidates).ConfigureAwait(false);
        }

        private async Task<(bool Confirmed, string SelectedPath)> SelectExecutableCandidateAsync(
            string gameName,
            string installRoot,
            IReadOnlyList<string>? candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return (false, string.Empty);
            }

            if (candidates.Count == 1)
            {
                return (true, candidates[0]);
            }

            var recommended = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?? candidates[0];
            _logger?.Write(PlatformLogLevel.Info, $"Executable selection required for '{gameName}'. Recommended='{recommended}'. Candidates=[{string.Join(", ", candidates)}]");

            if (_selectExecutableAsync == null)
            {
                return (false, recommended);
            }

            var request = new ExecutableSelectionRequest
            {
                Title = "Executable Selection",
                Message = "Multiple executables were detected. Choose the executable to launch.",
                InstallRoot = installRoot,
                Candidates = BuildExecutableCandidates(installRoot, candidates, recommended),
                Recommended = new ExecutableCandidate { FullPath = recommended, IsRecommended = true }
            };

            var response = await _selectExecutableAsync(request).ConfigureAwait(false);
            var selected = response?.Selected?.FullPath ?? recommended;
            return (response?.Confirmed == true, selected);
        }

        private static IReadOnlyList<ExecutableCandidate> BuildExecutableCandidates(
            string installRoot,
            IReadOnlyList<string> candidates,
            string recommended)
        {
            var list = new List<ExecutableCandidate>();
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
                string lastModifiedDisplay = string.Empty;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        fileSize = info.Length;
                        fileSizeDisplay = FormatFileSize(fileSize);
                        lastModifiedDisplay = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                }
                catch
                {
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
                }

                var architecture = ExecutableArchitectureDetector.GetArchitecture(path);
                var architectureDisplay = ExecutableArchitectureDetector.GetDisplayName(architecture);

                list.Add(new ExecutableCandidate
                {
                    IsRecommended = string.Equals(path, recommended, StringComparison.OrdinalIgnoreCase),
                    FileName = fileName,
                    FullPath = path,
                    DisplayPath = displayPath,
                    FileSizeBytes = fileSize,
                    FileSizeDisplay = fileSizeDisplay,
                    Version = version,
                    LastModifiedDisplay = lastModifiedDisplay,
                    Architecture = architectureDisplay
                });
            }

            return list;
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

            _logger?.Write(PlatformLogLevel.Warning, "Installer confirmation failed, but executable resolution succeeded. Proceeding with resolved executable.");
            return WindowsInstallResult.CreateSuccess(resolution.ExecutablePath, resolution.Arguments, InstallType.Installer);
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

        private static (string? Dlc, string? Update, string? Ost, string? Bonus, string? PreReqs) DiscoverContentRoots(string baseRoot)
        {
            string? dlc = null;
            string? update = null;
            string? ost = null;
            string? bonus = null;
            string? preReqs = null;

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

        private static bool IsReservedRoot(string? folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return false;
            }

            return RootFolders.Contains(folderName.Trim(), StringComparer.OrdinalIgnoreCase);
        }

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
}
