using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomM.Platforms.Abstractions;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models;
using RomM.Platforms.Abstractions.Models.Detection;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;
using RomM.Platforms.Windows.Install;

namespace RomM.Platforms.Windows
{
    public sealed class WindowsPlatformInstaller : IPlatformInstaller
    {
        public string PlatformKey => "windows";
        public string DisplayName => "Windows";

        public Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError> { new() { Code = "context_missing", Message = "Platform context missing." } };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            var installRoot = ResolveInstallRoot(ctx.InstallRootPath, ctx.InstalledPath);
            if (!string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
            {
                var resolver = new ExecutableResolver(ctx.Logger);
                var resolution = resolver.Resolve(installRoot, ctx.GameName, null);
                if (resolution.Success)
                {
                    result.RecommendedExecutablePath = resolution.ExecutablePath;
                    result.CandidateExecutablePaths = resolution.Candidates.Count > 0
                        ? resolution.Candidates
                        : new List<string> { resolution.ExecutablePath ?? string.Empty };
                }
                else
                {
                    result.Warnings = new List<DetectionWarning> { new() { Code = "no_executable", Message = resolution.Message } };
                }
            }
            else
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "missing_install_root", Message = "Install root not found on disk." } };
            }

            return Task.FromResult(result);
        }

        public async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return new InstallResult { Success = false, Message = "Install context missing." };
            }

            if (string.IsNullOrWhiteSpace(ctx.InstallDirectory))
            {
                return new InstallResult { Success = false, Message = "Install directory missing." };
            }

            if (string.IsNullOrWhiteSpace(ctx.ArchivePath) && string.IsNullOrWhiteSpace(ctx.ExtractedPath))
            {
                return new InstallResult { Success = false, Message = "Archive path missing." };
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Starting install...", 0));

            var subsystem = new WindowsInstallSubsystem(ctx.Logger, ctx.SelectExecutableAsync, ctx.ConfirmAsync);
            var result = await subsystem.InstallAsync(
                    ctx.ArchivePath,
                    ctx.ExtractedPath,
                    ctx.StagingDirectory ?? ctx.InstallDirectory,
                    ctx.Settings,
                    ctx.GameName ?? "Game",
                    ct,
                    new Progress<double>(value => progress?.Report(new InstallProgress("Installing", "Installing...", value, false))),
                    ctx.InstallDirectory,
                    preferFinalInstallDirForInstaller: true)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return new InstallResult { Success = false, Message = result.Message ?? "Windows install failed." };
            }

            var installRootPath = ResolveInstallRootPath(result, ctx.InstallDirectory ?? string.Empty);
            if (string.IsNullOrWhiteSpace(installRootPath))
            {
                installRootPath = ctx.InstallDirectory;
            }

            return new InstallResult
            {
                Success = true,
                Message = "Install completed.",
                ExecutablePath = result.ExecutablePath,
                Arguments = result.Arguments,
                InstallType = result.InstallType,
                InstallRootPath = installRootPath
            };
        }

        public Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "Preparing uninstall...", 0, true));

            var removed = 0;
            var notes = new List<string>();

            var installRoot = ResolveInstallRoot(ctx.InstallRootPath, ctx.InstalledPath);
            var installType = ctx.WindowsInstallType;

            if (string.Equals(installType, "Installer", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(installRoot))
                {
                    notes.Add("Install root missing; cannot locate Inno uninstaller.");
                }
                else
                {
                    var ran = TryRunInnoUninstaller(installRoot, notes, ctx.Logger);
                    if (!ran)
                    {
                        notes.Add("Inno uninstaller not found or failed; falling back to delete.");
                    }

                    removed += EnsureDirectoryRemoved(installRoot, notes, ctx.Logger);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(installRoot))
                {
                    notes.Add("Install root missing; portable uninstall skipped.");
                }
                else
                {
                    removed += TryDeletePath(installRoot, notes, ctx.Logger);
                }
            }

            removed += TryDeletePath(ctx.InstalledPath, notes, ctx.Logger);
            removed += TryDeletePath(ctx.ArchivePath, notes, ctx.Logger);

            progress?.Report(new InstallProgress("Uninstall", "Uninstall completed.", 100, false));

            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "Uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        public Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath)
                && (File.Exists(ctx.InstalledPath) || Directory.Exists(ctx.InstalledPath));
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "Install verified." : "Install missing on disk."
            });
        }

        private static string? ResolveInstallRoot(string? installRootPath, string? installedPath)
        {
            var root = installRootPath;
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            if (string.IsNullOrWhiteSpace(installedPath))
            {
                return string.Empty;
            }

            if (Directory.Exists(installedPath))
            {
                return installedPath;
            }

            if (File.Exists(installedPath))
            {
                return Path.GetDirectoryName(installedPath) ?? string.Empty;
            }

            return string.Empty;
        }

        private static string? ResolveInstallRootPath(WindowsInstallResult result, string installDirectory)
        {
            if (!string.IsNullOrWhiteSpace(result?.ExecutablePath))
            {
                var directory = Path.GetDirectoryName(result.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    if (!string.IsNullOrWhiteSpace(installDirectory))
                    {
                        var normalizedRoot = installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        if (directory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            var remainder = directory.Substring(normalizedRoot.Length);
                            var segments = remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            var firstSegment = segments.FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
                            if (!string.IsNullOrWhiteSpace(firstSegment))
                            {
                                return Path.Combine(installDirectory, firstSegment);
                            }
                        }
                    }

                    return directory;
                }
            }

            return installDirectory;
        }

        private static int TryDeletePath(string? path, List<string> messages, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            try
            {
                if (File.Exists(path))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Deleting file '{path}'.");
                    File.Delete(path);
                    return 1;
                }

                if (Directory.Exists(path))
                {
                    logger?.Write(PlatformLogLevel.Info, $"Deleting directory '{path}'.");
                    Directory.Delete(path, true);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to delete '{path}': {ex.Message}");
                messages?.Add($"Failed to delete '{path}': {ex.Message}");
            }

            return 0;
        }

        private static bool TryRunInnoUninstaller(string installRoot, List<string> messages, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                return false;
            }

            var uninstaller = FindInnoUninstaller(installRoot);
            if (string.IsNullOrWhiteSpace(uninstaller))
            {
                logger?.Write(PlatformLogLevel.Warning, $"Inno uninstaller not found in '{installRoot}'.");
                return false;
            }

            try
            {
                var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /FORCECLOSEAPPLICATIONS /LOG";
                logger?.Write(PlatformLogLevel.Info, $"Running Inno uninstaller: {uninstaller} {args}");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uninstaller,
                    Arguments = args,
                    UseShellExecute = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Failed to start Inno uninstaller '{uninstaller}'.");
                    messages?.Add($"Failed to start Inno uninstaller '{uninstaller}'.");
                    return false;
                }

                var timeout = TimeSpan.FromMinutes(10);
                logger?.Write(PlatformLogLevel.Info, $"Waiting for Inno uninstaller to exit (timeout {timeout.TotalMinutes:0} minutes).");
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    logger?.Write(PlatformLogLevel.Warning, $"Inno uninstaller did not exit within timeout ({timeout.TotalMinutes:0} minutes). Attempting to continue cleanup.");
                    messages?.Add($"Inno uninstaller timed out after {timeout.TotalMinutes:0} minutes.");
                    try
                    {
                        if (!process.HasExited)
                        {
                            _ = process.CloseMainWindow();
                        }
                    }
                    catch
                    {
                    }
                    return false;
                }

                logger?.Write(PlatformLogLevel.Info, $"Inno uninstaller exited with code {process.ExitCode}.");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Write(PlatformLogLevel.Warning, $"Failed to run Inno uninstaller '{uninstaller}': {ex.Message}");
                messages?.Add($"Failed to run Inno uninstaller '{uninstaller}': {ex.Message}");
                return false;
            }
        }

        private static string? FindInnoUninstaller(string installRoot)
        {
            try
            {
                var topLevel = Directory.EnumerateFiles(installRoot, "unins*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(topLevel))
                {
                    return topLevel;
                }

                var nested = Directory.EnumerateFiles(installRoot, "unins*.exe", SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                return string.IsNullOrWhiteSpace(nested) ? null : nested;
            }
            catch
            {
                return null;
            }
        }

        private static int EnsureDirectoryRemoved(string installRoot, List<string> messages, IPlatformLogger? logger)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return 0;
            }

            if (!Directory.Exists(installRoot))
            {
                return 0;
            }

            logger?.Write(PlatformLogLevel.Info, $"Installer uninstall left directory '{installRoot}'. Removing now.");
            return TryDeletePath(installRoot, messages, logger);
        }
    }
}
