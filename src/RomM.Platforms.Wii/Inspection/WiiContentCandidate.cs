namespace RomM.Platforms.Wii.Inspection
{
    public sealed class WiiContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public WiiContentFormat Format { get; set; }
        public WiiContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}

