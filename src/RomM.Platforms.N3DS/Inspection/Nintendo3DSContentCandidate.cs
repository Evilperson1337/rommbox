namespace RomM.Platforms.N3DS.Inspection
{
    public sealed class Nintendo3DSContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public Nintendo3DSContentFormat Format { get; set; }
        public Nintendo3DSContentFormat NormalizedFormat { get; set; }
        public bool IsDirectLaunchArtifact { get; set; }
        public bool RequiresEmulatorImport { get; set; }
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}

