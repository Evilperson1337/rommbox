namespace RomM.Platforms.Xbox.Inspection
{
    public sealed class XboxContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public XboxContentFormat Format { get; set; }
        public XboxContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public bool IsExtractedLayoutSignal { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}

