namespace RomM.Platforms.Switch.Inspection
{
    public sealed class SwitchContentCandidate
    {
        public string Path { get; set; } = string.Empty;
        public SwitchContentFormat Format { get; set; }
        public SwitchPackageType PackageType { get; set; }
        public bool RequiresDecompression { get; set; }
        public string NormalizedExtension { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}

