namespace RomM.Platforms.WiiU.Inspection
{
    public sealed class WiiUContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public WiiUContentFormat Format { get; set; }
        public WiiUContentFormat NormalizedFormat { get; set; }
        public WiiUContentRole Role { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public string LaunchArtifactPath { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}

