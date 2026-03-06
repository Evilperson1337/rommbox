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
using RomM.Platforms.Abstractions.Models.Rom;
using RomM.Platforms.Abstractions.Models.Uninstall;
using RomM.Platforms.Abstractions.Models.Verify;

namespace RomM.Platforms.RomBase
{
    public abstract class RomPlatformInstallerBase : IPlatformInstaller
    {
        public abstract string PlatformKey { get; }
        public abstract string DisplayName { get; }

        protected abstract RomInstallProfile BuildProfile();

        public virtual Task<DetectionResult> DetectAsync(PlatformContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new DetectionResult();
            if (ctx == null)
            {
                result.Errors = new List<DetectionError> { new() { Code = "context_missing", Message = "Platform context missing." } };
                return Task.FromResult(result);
            }

            result.IsInstalled = ctx.IsInstalled;
            if (string.IsNullOrWhiteSpace(ctx.InstalledPath))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "missing_installed_path", Message = "Installed path missing." } };
                return Task.FromResult(result);
            }

            if (!File.Exists(ctx.InstalledPath))
            {
                result.Warnings = new List<DetectionWarning> { new() { Code = "installed_missing", Message = "Installed ROM not found on disk." } };
                result.IsInstalled = false;
                return Task.FromResult(result);
            }

            return Task.FromResult(result);
        }

        public virtual async Task<InstallResult> InstallAsync(InstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return new InstallResult { Success = false, Message = "Install context missing." };
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Installing", "Preparing ROM install...", 0));

            var profile = BuildProfile();
            var planner = new RomInstallPlanner(profile);
            var selection = planner.Plan(
                ctx.GameName,
                ctx.ArchivePath,
                ctx.ExtractedPath,
                ctx.InstallDirectory,
                ctx.RomSettings,
                ctx.Logger);

            if (string.IsNullOrWhiteSpace(selection.SourcePath) || string.IsNullOrWhiteSpace(selection.TargetPath))
            {
                var details = selection.Candidates.Count == 0
                    ? "No ROM candidates detected."
                    : $"Found {selection.Candidates.Count} candidate(s) but none were selected.";
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"ROM install failed. {details}");
                return new InstallResult { Success = false, Message = selection.Message ?? details };
            }

            if (selection.Candidates.Count > 1)
            {
                var joined = string.Join("; ", selection.Candidates.Select(path => path ?? string.Empty));
                ctx.Logger?.Write(PlatformLogLevel.Info, $"ROM install candidates: {joined}");
            }

            var mover = new RomInstallFileMover();
            string finalPath;
            try
            {
                progress?.Report(new InstallProgress("Installing", "Copying ROM to destination...", 50));
                finalPath = mover.StageAndMove(selection.SourcePath, selection.TargetPath, ctx.Logger);
            }
            catch (Exception ex)
            {
                ctx.Logger?.Write(PlatformLogLevel.Warning, $"ROM install failed while moving file: {ex.Message}");
                return new InstallResult { Success = false, Message = ex.Message };
            }

            progress?.Report(new InstallProgress("Installing", "ROM install completed.", 100));
            var romLaunchArgs = BuildLaunchArguments(ctx.RomSettings, profile, finalPath);
            return new InstallResult
            {
                Success = true,
                Message = "ROM install completed.",
                ExecutablePath = finalPath,
                Arguments = string.IsNullOrWhiteSpace(romLaunchArgs) ? Array.Empty<string>() : new[] { romLaunchArgs },
                InstallType = InstallType.Portable,
                InstallRootPath = Path.GetDirectoryName(finalPath)
            };
        }

        public virtual Task<UninstallResult> UninstallAsync(UninstallContext ctx, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            if (ctx == null)
            {
                return Task.FromResult(new UninstallResult { Success = false, Message = "Uninstall context missing." });
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress("Uninstall", "Removing ROM...", 0, true));

            var removed = 0;
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(ctx.InstalledPath) && File.Exists(ctx.InstalledPath))
            {
                try
                {
                    File.Delete(ctx.InstalledPath);
                    removed++;
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to delete ROM: {ex.Message}");
                    ctx.Logger?.Write(PlatformLogLevel.Warning, $"ROM uninstall failed to delete '{ctx.InstalledPath}': {ex.Message}");
                }
            }

            progress?.Report(new InstallProgress("Uninstall", "ROM uninstall completed.", 100, false));
            return Task.FromResult(new UninstallResult
            {
                Success = true,
                Message = "ROM uninstall completed.",
                RemovedCount = removed,
                Notes = notes
            });
        }

        public virtual Task<VerifyResult> VerifyAsync(VerifyContext ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var valid = !string.IsNullOrWhiteSpace(ctx?.InstalledPath) && File.Exists(ctx.InstalledPath);
            return Task.FromResult(new VerifyResult
            {
                IsValid = valid,
                Message = valid ? "ROM install verified." : "ROM install missing on disk."
            });
        }

        protected virtual string BuildLaunchArguments(RomInstallSettings? settings, RomInstallProfile profile, string romPath)
        {
            var arguments = settings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = profile.Emulator?.LaunchArguments;
            }

            if (string.IsNullOrWhiteSpace(arguments))
            {
                return string.Empty;
            }

            return arguments.Replace("{rom}", QuoteArgument(romPath));
        }

        protected static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ") ? $"\"{value}\"" : value;
        }
    }
}
