namespace RomM.Platforms.PS3.Inspection
{
    public sealed class Ps3RapFile
    {
        public string Path { get; set; } = string.Empty;
        public string TitleId { get; set; } = string.Empty;
        public Ps3GameRegion Region { get; set; } = Ps3GameRegion.Unknown;
    }
}
