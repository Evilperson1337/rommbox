using System.Collections.Generic;

namespace RomM.Platforms.PS4.Inspection
{
    public sealed class Ps4ContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public Ps4ContentRole ContentRole { get; set; }
        public Ps4ContentFormat ContentFormat { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool RequiresExternalExtractor { get; set; }
        public bool IsSupportedForInstall { get; set; }
        public List<string> Warnings { get; } = new();
    }
}

