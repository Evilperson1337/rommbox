namespace RomM.Platforms.PS3.Inspection
{
    public sealed class Ps3PackageFile
    {
        public string Path { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public Ps3PackageKind Kind { get; set; } = Ps3PackageKind.Unknown;
        public Ps3GameRegion Region { get; set; } = Ps3GameRegion.Unknown;
    }
}
