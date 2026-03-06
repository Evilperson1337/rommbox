namespace RomM.Platforms.Abstractions.Models.Rom
{
    public sealed class RomEmulatorMetadata
    {
        public string? EmulatorId { get; set; }
        public string? EmulatorName { get; set; }
        public string? CoreId { get; set; }
        public string? CoreName { get; set; }
        public string? CorePath { get; set; }
        public string? LaunchArguments { get; set; }
    }
}
