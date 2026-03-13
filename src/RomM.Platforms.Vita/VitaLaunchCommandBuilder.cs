using System;
using System.Collections.Generic;
using RomM.Platforms.Abstractions.Logging;
using RomM.Platforms.Abstractions.Models.Install;
using RomM.Platforms.Abstractions.Models.Rom;

namespace RomM.Platforms.Vita
{
    internal static class VitaLaunchCommandBuilder
    {
        public static VitaLaunchCommand Build(VitaTitleResolution resolution, PlatformInstallSettings? installSettings, RomInstallSettings? romSettings, IPlatformLogger? logger)
        {
            if (resolution == null)
            {
                throw new ArgumentNullException(nameof(resolution));
            }

            if (!resolution.IsValid)
            {
                throw new InvalidOperationException(resolution.ValidationError ?? "Vita launch resolution is invalid.");
            }

            if (string.IsNullOrWhiteSpace(resolution.TitleId))
            {
                throw new InvalidOperationException("Vita launch resolution is missing title id.");
            }

            var template = romSettings?.LaunchArguments;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "--title-id {titleId}";
            }

            var serializedArguments = template
                .Replace("{titleId}", resolution.TitleId, StringComparison.Ordinal)
                .Replace("{launchTarget}", resolution.LaunchTarget, StringComparison.Ordinal)
                .Replace("{titleName}", QuoteArgument(resolution.TitleName), StringComparison.Ordinal)
                .Replace("{rom}", QuoteArgument(resolution.LaunchTarget), StringComparison.Ordinal)
                .Trim();

            if (string.IsNullOrWhiteSpace(serializedArguments))
            {
                throw new InvalidOperationException("Resolved Vita launch arguments are empty.");
            }

            var workingDirectory = ResolveWorkingDirectory(installSettings, romSettings);
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita launch mode: {resolution.LaunchMode}");
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita launch target: {resolution.LaunchTarget}");
            logger?.Write(PlatformLogLevel.Info, $"Resolved Vita launch arguments: {serializedArguments}");
            logger?.Write(PlatformLogLevel.Info, string.IsNullOrWhiteSpace(workingDirectory)
                ? "Resolved Vita launch working directory: LaunchBox emulator-managed"
                : $"Resolved Vita launch working directory: {workingDirectory}");

            return new VitaLaunchCommand(serializedArguments, workingDirectory);
        }

        private static string ResolveWorkingDirectory(PlatformInstallSettings? installSettings, RomInstallSettings? romSettings)
        {
            var executable = !string.IsNullOrWhiteSpace(installSettings?.Vita3kExecutablePath)
                ? installSettings!.Vita3kExecutablePath!
                : (romSettings?.EmulatorExecutablePath ?? string.Empty);
            return string.IsNullOrWhiteSpace(executable)
                ? string.Empty
                : (System.IO.Path.GetDirectoryName(executable) ?? string.Empty);
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.Contains(" ", StringComparison.Ordinal) ? $"\"{value}\"" : value;
        }
    }

    internal sealed class VitaLaunchCommand
    {
        public VitaLaunchCommand(string serializedArguments, string workingDirectory)
        {
            SerializedArguments = serializedArguments ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
        }

        public string SerializedArguments { get; }

        public string WorkingDirectory { get; }

        public IReadOnlyList<string> ToInstallArguments()
        {
            return string.IsNullOrWhiteSpace(SerializedArguments)
                ? Array.Empty<string>()
                : new[] { SerializedArguments };
        }
    }
}
