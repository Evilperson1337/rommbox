namespace RomM.Platforms.Xbox360.Inspection
{
    public sealed class Xbox360ContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public Xbox360ContentFormat Format { get; set; }
        public Xbox360ContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public bool IsExtractedLayoutSignal { get; set; }
        public bool IsGodLayoutSignal { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string MediaId { get; set; } = string.Empty;
    }
}

