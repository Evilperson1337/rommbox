namespace RomM.Platforms.PS2.Inspection
{
    public sealed class Ps2ContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public Ps2ContentFormat Format { get; set; }
        public Ps2ContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public string DiscId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}

