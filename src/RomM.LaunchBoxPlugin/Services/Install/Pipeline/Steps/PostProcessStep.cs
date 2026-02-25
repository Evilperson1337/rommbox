using System;
using System.Threading;
using System.Threading.Tasks;
using RomMbox.Services.Install;

namespace RomMbox.Services.Install.Pipeline.Steps
{
    internal sealed class PostProcessStep : IInstallStep
    {
        public InstallPhase Phase => InstallPhase.PostProcessing;

        public Task<InstallResult> ExecuteAsync(InstallContext context, IProgress<InstallProgressEvent> progress, CancellationToken cancellationToken)
        {
            if (context?.InstallStateSnapshot == null)
            {
                return Task.FromResult(InstallResult.Failed(Phase, "Install state missing."));
            }

            var platform = context.DataManager.GetPlatformByName(context.Game.Platform);
            if (platform == null)
            {
                return Task.FromResult(InstallResult.Failed(Phase, "LaunchBox platform not found."));
            }

            var finalPath = context.InstalledExecutablePath;
            if (!string.IsNullOrWhiteSpace(finalPath))
            {
                context.Game.ApplicationPath = ToLaunchBoxRelativePath(finalPath);
            }

            if (context.InstallerArguments != null && context.InstallerArguments.Length > 0)
            {
                context.Game.CommandLine = string.Join(" ", context.InstallerArguments);
            }

            context.Game.Installed = true;
            context.Game.Status = "Installed";

            var emulatorId = ResolveEmulatorId(context.DataManager, context.Game.Platform);
            if (!string.IsNullOrWhiteSpace(emulatorId))
            {
                context.Game.EmulatorId = emulatorId;
            }

            context.InstallStateSnapshot.InstalledPath = finalPath;
            context.InstallStateSnapshot.RommLaunchPath = finalPath;
            context.InstallStateSnapshot.RommLaunchArgs = context.InstallerArguments != null && context.InstallerArguments.Length > 0
                ? string.Join(" ", context.InstallerArguments)
                : string.Empty;
            context.InstallStateSnapshot.ArchivePath = context.ArchivePath;
            if (string.IsNullOrWhiteSpace(context.InstallStateSnapshot.InstallRootPath))
            {
                context.InstallStateSnapshot.InstallRootPath = context.InstallDirectory;
            }
            context.InstallStateSnapshot.IsInstalled = true;
            context.InstallStateSnapshot.InstalledUtc = DateTimeOffset.UtcNow;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;

            return Task.FromResult(InstallResult.Successful());
        }

        private static string ToLaunchBoxRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                var root = Paths.PluginPaths.GetLaunchBoxRootDirectory();
                if (string.IsNullOrWhiteSpace(root))
                {
                    return absolutePath;
                }

                var normalizedRoot = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    + System.IO.Path.DirectorySeparatorChar;
                if (absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = absolutePath.Substring(normalizedRoot.Length);
                    return relative.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
                }
            }
            catch
            {
            }

            return absolutePath;
        }

        private static string ResolveEmulatorId(Unbroken.LaunchBox.Plugins.Data.IDataManager dataManager, string platformName)
        {
            if (dataManager == null || string.IsNullOrWhiteSpace(platformName))
            {
                return string.Empty;
            }

            var emulators = dataManager.GetAllEmulators() ?? Array.Empty<Unbroken.LaunchBox.Plugins.Data.IEmulator>();
            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<Unbroken.LaunchBox.Plugins.Data.IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase)
                        && platform?.IsDefault == true)
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            foreach (var emulator in emulators)
            {
                var platforms = emulator?.GetAllEmulatorPlatforms() ?? Array.Empty<Unbroken.LaunchBox.Plugins.Data.IEmulatorPlatform>();
                foreach (var platform in platforms)
                {
                    if (string.Equals(platform?.Platform, platformName, StringComparison.OrdinalIgnoreCase))
                    {
                        return emulator?.Id ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }
    }
}
