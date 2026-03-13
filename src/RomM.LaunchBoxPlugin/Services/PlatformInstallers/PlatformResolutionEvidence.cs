using System;
using System.Linq;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformResolutionEvidence
    {
        public string PlatformKey { get; init; } = string.Empty;

        public string PlatformDisplayName { get; init; } = string.Empty;

        public string LaunchBoxPlatformName { get; init; } = string.Empty;

        public string FileExtension { get; init; } = string.Empty;

        public bool HasMeaningfulNameEvidence()
        {
            return IsMeaningfulName(PlatformDisplayName) || IsMeaningfulName(LaunchBoxPlatformName);
        }

        public static bool IsMeaningfulName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return !value.Trim().All(char.IsDigit);
        }
    }
}
