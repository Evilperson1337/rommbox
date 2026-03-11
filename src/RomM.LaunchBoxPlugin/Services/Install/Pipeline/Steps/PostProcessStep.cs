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

            var serializedArguments = SerializeInstallerArguments(context.InstallerArguments, finalPath);
            if (!string.IsNullOrWhiteSpace(serializedArguments))
            {
                context.Game.CommandLine = serializedArguments;
            }
            else
            {
                context.Game.CommandLine = string.Empty;
            }

            context.Game.Installed = true;
            context.Game.Status = "Installed";

            var emulatorId = ResolveEmulatorId(context.DataManager, context.Game.Platform);
            if (!string.IsNullOrWhiteSpace(emulatorId))
            {
                context.Game.EmulatorId = emulatorId;
            }
            else if (!string.IsNullOrWhiteSpace(context.PlatformMapping?.AssociatedEmulatorId))
            {
                context.Game.EmulatorId = context.PlatformMapping.AssociatedEmulatorId;
            }

            context.InstallStateSnapshot.InstalledPath = finalPath;
            context.InstallStateSnapshot.RommLaunchPath = finalPath;
            context.InstallStateSnapshot.RommLaunchArgs = serializedArguments;
            if (string.IsNullOrWhiteSpace(context.InstallStateSnapshot.RommLaunchArgs)
                && !string.IsNullOrWhiteSpace(context.PlatformMapping?.EmulatorLaunchArgs))
            {
                context.InstallStateSnapshot.RommLaunchArgs = context.PlatformMapping.EmulatorLaunchArgs;
            }
            context.InstallStateSnapshot.ArchivePath = context.ArchivePath;
            if (string.IsNullOrWhiteSpace(context.InstallStateSnapshot.InstallRootPath))
            {
                context.InstallStateSnapshot.InstallRootPath = context.InstallDirectory;
            }
            context.InstallStateSnapshot.IsInstalled = true;
            context.InstallStateSnapshot.InstalledUtc = DateTimeOffset.UtcNow;
            context.InstallStateSnapshot.LastValidatedUtc = DateTimeOffset.UtcNow;
            context.InstallStateSnapshot.InstallStatus = "Completed";
            context.InstallStateSnapshot.InstallPhase = Phase.ToString();

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

        private static string SerializeInstallerArguments(string[] installerArguments, string finalPath)
        {
            if (installerArguments == null || installerArguments.Length == 0)
            {
                return string.Empty;
            }

            var serialized = string.Join(" ", installerArguments).Trim();
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return string.Empty;
            }

            if (IsRomOnlyArgument(serialized, finalPath))
            {
                return string.Empty;
            }

            return serialized;
        }

        private static bool IsRomOnlyArgument(string serializedArguments, string finalPath)
        {
            if (string.IsNullOrWhiteSpace(serializedArguments) || string.IsNullOrWhiteSpace(finalPath))
            {
                return false;
            }

            var trimmedArguments = serializedArguments.Trim();
            var trimmedPath = finalPath.Trim();
            var quotedPath = trimmedPath.Contains(" ", StringComparison.Ordinal)
                ? $"\"{trimmedPath}\""
                : trimmedPath;

            return string.Equals(trimmedArguments, trimmedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmedArguments, quotedPath, StringComparison.OrdinalIgnoreCase);
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
