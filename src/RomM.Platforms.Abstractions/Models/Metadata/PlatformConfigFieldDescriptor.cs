namespace RomM.Platforms.Abstractions.Models.Metadata
{
    public sealed class PlatformConfigFieldDescriptor
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Description { get; set; }
        public PlatformConfigFieldType Type { get; set; } = PlatformConfigFieldType.String;
        public bool Required { get; set; }
        public bool Advanced { get; set; }
        public string? DefaultValue { get; set; }
    }
}

