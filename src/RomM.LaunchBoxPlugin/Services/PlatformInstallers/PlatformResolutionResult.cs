using System;
using System.Collections.Generic;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformResolutionResult
    {
        public string ResolvedPlatformKey { get; init; } = string.Empty;

        public string ResolutionReason { get; init; } = string.Empty;

        public bool IsAmbiguous { get; init; }

        public bool UsedExtensionEvidence { get; init; }

        public IReadOnlyList<string> CandidateDiagnostics { get; init; } = Array.Empty<string>();
    }
}
