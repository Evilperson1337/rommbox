using System;

namespace RomMbox.Services
{
    /// <summary>
    /// Describes a RomM web-play endpoint for a platform.
    /// </summary>
    internal sealed class RommPlayEndpointProfile
    {
        public bool IsPlayableOnRomM { get; init; }

        public string PathSuffix { get; init; } = string.Empty;

        public string BuildPath(string romId)
        {
            if (!IsPlayableOnRomM || string.IsNullOrWhiteSpace(romId) || string.IsNullOrWhiteSpace(PathSuffix))
            {
                return string.Empty;
            }

            return $"/rom/{romId.Trim()}/{PathSuffix.Trim('/')}";
        }
    }
}
