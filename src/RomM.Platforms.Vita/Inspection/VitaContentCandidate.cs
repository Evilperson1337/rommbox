namespace RomM.Platforms.Vita.Inspection
{
    public sealed class VitaContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public VitaContentFormat Format { get; set; }
        public VitaContentFormat NormalizedFormat { get; set; }
        public VitaContentRole Role { get; set; }
        public bool IsInstallableArtifact { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}

